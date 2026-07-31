package com.aeliavision.androiddeck.feature.backup.data

import android.content.ContentValues
import android.content.Context
import android.os.Environment
import android.provider.MediaStore
import android.util.Log
import android.util.Base64
import com.aeliavision.androiddeck.feature.backup.model.RestorePhase
import com.aeliavision.androiddeck.feature.backup.model.RestoreState
import com.aeliavision.androiddeck.feature.contacts.data.ContactsRepository
import dagger.hilt.android.qualifiers.ApplicationContext
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.cancel
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext
import java.io.BufferedInputStream
import java.io.File
import java.io.InputStream
import java.io.IOException
import java.nio.ByteBuffer
import java.security.MessageDigest
import java.security.DigestOutputStream
import java.util.UUID
import java.util.concurrent.ConcurrentHashMap
import java.util.zip.ZipInputStream
import javax.crypto.Cipher
import javax.crypto.CipherInputStream
import com.aeliavision.androiddeck.feature.backup.crypto.Hkdf
import javax.crypto.spec.GCMParameterSpec
import javax.crypto.spec.SecretKeySpec
/**
 *
 * Decrypts and unpacks a .vcfbak archive produced by [BackupManager] and
 * restores its contents to the device:
 *
 *   contacts/contacts.vcf  → ContactsRepository.importContactsFromVcf()
 *   gallery/(all)          → MediaStore insert (Images or Video based on MIME)
 *   files/(all)            → External storage (Downloads by default)
 *
 * Conflict resolution: "skip-if-exists" for files; contacts use the existing
 * dedup logic in ContactsRepository (normalised name match).
 */
@javax.inject.Singleton
class RestoreManager @javax.inject.Inject constructor(
    @ApplicationContext private val context: Context,
    private val contactsRepository: ContactsRepository,
    private val backupKeyStore: BackupKeyStore
) {
    companion object {
        private const val TAG = "RestoreManager"
        private val MAGIC = byteArrayOf(0x56, 0x43, 0x46, 0x42)
        private const val VERSION_V1 = 0x01
        private const val VERSION_V2 = 0x02
        private const val VERSION_V3 = 0x03
        private const val KEY_BITS = 256
        private const val GCM_TAG_BITS = 128
        private const val IV_BYTES = 12
        private const val SALT_BYTES = 16
        private const val SEED_ID_BYTES = 16
        private const val ZIP_HASH_BYTES = 32
        private const val ZIP_SIZE_BYTES = 8
    }

    private val restores = ConcurrentHashMap<String, RestoreState>()

    /** Dedicated scope for background restore jobs (do not block request handlers). */
    @Volatile
    private var scopeJob = SupervisorJob()

    @Volatile
    private var jobScope = CoroutineScope(scopeJob + Dispatchers.IO)

    fun getState(restoreId: String): RestoreState? = restores[restoreId]

    fun shutdown() {
        scopeJob.cancel("RestoreManager shutdown")
        scopeJob = SupervisorJob()
        jobScope = CoroutineScope(scopeJob + Dispatchers.IO)
    }

    /**
     * Start a restore from the supplied [archiveStream] (the raw .vcfbak binary).
     * The stream is NOT closed by this function — the caller (Ktor route handler)
     * owns the stream lifecycle.
     *
     * Spools the upload to a temp file in cacheDir, then runs the restore asynchronously
     * on a dedicated IO scope so the HTTP handler can respond immediately.
     *
     * Returns the new [RestoreState.restoreId].
     */
    suspend fun startRestore(
        archiveStream: InputStream,
        providedSeedId: String? = null,
        providedSeedBase64: String? = null
    ): String = withContext(Dispatchers.IO) {
        val restoreId = UUID.randomUUID().toString()
        val state = RestoreState(restoreId = restoreId)
        restores[restoreId] = state

        val tempArchive = File(context.cacheDir, "restore_${restoreId}.upload.tmp")
        try {
            tempArchive.outputStream().buffered().use { out ->
                archiveStream.copyTo(out, bufferSize = 512 * 1024)
            }
        } catch (e: Exception) {
            tempArchive.delete()
            Log.e(TAG, "Restore $restoreId upload spool failed", e)
            state.phase = RestorePhase.FAILED
            state.error = formatError("Upload failed", e)
            return@withContext restoreId
        }

        jobScope.launch {
            try {
                tempArchive.inputStream().use { input ->
                    runRestore(state, input, providedSeedId, providedSeedBase64)
                }
            } catch (e: Exception) {
                Log.e(TAG, "Restore $restoreId failed", e)
                state.phase = RestorePhase.FAILED
                state.error = formatError("Restore failed", e)
            } finally {
                tempArchive.delete()
            }
        }

        restoreId
    }

    private fun formatError(prefix: String, e: Exception): String {
        val type = e::class.java.simpleName
        val msg = e.message ?: "(no message)"
        val stack = e.stackTrace
            .take(6)
            .joinToString("\n") { "at ${it.className}.${it.methodName}(${it.fileName}:${it.lineNumber})" }
        return "$prefix: $type: $msg\n$stack"
    }

    // -- Private — restore engine -------------------------------------------------

    private suspend fun runRestore(
        state: RestoreState,
        archiveStream: InputStream,
        providedSeedId: String?,
        providedSeedBase64: String?
    ) = withContext(Dispatchers.IO) {
        archiveStream.use { rawIn ->
            // -- Step 1: Decrypt -------------------------------------------------
            state.phase = RestorePhase.EXTRACTING
            state.progress = 0f

            val buffered = if (rawIn is BufferedInputStream) rawIn
            else BufferedInputStream(rawIn)

            // Plain ZIP support (no encryption): magic starts with 'P' 'K'.
            buffered.mark(8)
            val p0 = buffered.read()
            val p1 = buffered.read()
            buffered.reset()

            if (p0 == 0x50 && p1 == 0x4B) {
                ZipInputStream(buffered).use { zis ->
                    restoreFromZipStream(state, zis, version = null, plainZipStream = null, expectedZipSha256 = null, expectedZipSize = null)
                }
                finalizeRestore(state)
                return@withContext
            }

            // Parse header: MAGIC(4) + VERSION(1) + SALT(16) + IV(12) + optional v2 fields
            val magic = buffered.readExactly(4)
            require(magic.contentEquals(MAGIC)) { "Invalid archive: bad magic bytes" }
            val version = buffered.read()
            require(version == VERSION_V1 || version == VERSION_V2 || version == VERSION_V3) { "Unsupported archive version: $version" }

            val seed: ByteArray
            if (version == VERSION_V3) {
                val seedIdBytes = buffered.readExactly(SEED_ID_BYTES)
                val seedId = bytesToUuid(seedIdBytes)

                seed = try {
                    backupKeyStore.getBackupSeedById(seedId)
                } catch (e: IllegalStateException) {
                    if (!providedSeedId.isNullOrBlank() &&
                        !providedSeedBase64.isNullOrBlank() &&
                        providedSeedId.equals(seedId, ignoreCase = true)) {

                        val decoded = Base64.decode(providedSeedBase64, Base64.DEFAULT)
                        backupKeyStore.importBackupSeed(seedId, decoded)
                        backupKeyStore.getBackupSeedById(seedId)
                    } else {
                        throw e
                    }
                }
            } else {
                seed = backupKeyStore.getOrCreateBackupSeed()
            }

            val salt = buffered.readExactly(SALT_BYTES)
            val iv   = buffered.readExactly(IV_BYTES)

            val expectedZipSha256: ByteArray?
            val expectedZipSize: Long?
            if (version == VERSION_V2 || version == VERSION_V3) {
                expectedZipSha256 = buffered.readExactly(ZIP_HASH_BYTES)
                expectedZipSize = ByteBuffer.wrap(buffered.readExactly(ZIP_SIZE_BYTES)).long
            } else {
                expectedZipSha256 = null
                expectedZipSize = null
            }

            val key = deriveKey(seed, salt)
            val cipher = Cipher.getInstance("AES/GCM/NoPadding")
            cipher.init(Cipher.DECRYPT_MODE, key, GCMParameterSpec(GCM_TAG_BITS, iv))

            val plainZipStream: InputStream = if (version == VERSION_V2 || version == VERSION_V3) {
                HashingCountingInputStream(CipherInputStream(buffered, cipher))
            } else {
                CipherInputStream(buffered, cipher)
            }

            ZipInputStream(plainZipStream).use { zis ->
                restoreFromZipStream(
                    state = state,
                    zipStream = zis,
                    version = version,
                    plainZipStream = plainZipStream,
                    expectedZipSha256 = expectedZipSha256,
                    expectedZipSize = expectedZipSize
                )
            }

            finalizeRestore(state)
        }
    }

    private fun finalizeRestore(state: RestoreState) {
        state.phase = RestorePhase.DONE
        state.progress = 1f
        Log.i(TAG, "Restore ${state.restoreId} complete — " +
                "restored=${state.restoredItems} failed=${state.failedItems} skipped=${state.skippedItems}")
    }

    private suspend fun restoreFromZipStream(
        state: RestoreState,
        zipStream: ZipInputStream,
        version: Int?,
        plainZipStream: InputStream?,
        expectedZipSha256: ByteArray?,
        expectedZipSize: Long?
    ) {
        // -- Step 2: Walk ZIP entries ----------------------------------------
        var entry = zipStream.nextEntry
        while (entry != null) {
            val name = entry.name
            when {
                name == "manifest.json" -> {
                    // Read and discard manifest (metadata only, not needed for restore).
                    val buf = ByteArray(64 * 1024)
                    while (true) {
                        val n = zipStream.read(buf)
                        if (n <= 0) break
                    }
                }
                name.startsWith("contacts/") && name.endsWith(".vcf") -> {
                    state.phase = RestorePhase.RESTORING_CONTACTS
                    restoreContacts(state, zipStream)
                }
                name.startsWith("gallery/") -> {
                    val fileName = name.removePrefix("gallery/")
                    if (fileName.isNotBlank()) {
                        restoreGalleryItem(state, zipStream, fileName)
                    }
                }
                name.startsWith("files/") -> {
                    val relPath = name.removePrefix("files/")
                    if (relPath.isNotBlank()) {
                        restoreFile(state, zipStream, relPath)
                    }
                }
            }
            zipStream.closeEntry()
            entry = zipStream.nextEntry
        }

        // B-IMP-01: Verify integrity for v2 archives.
        if (version == VERSION_V2 || version == VERSION_V3) {
            val hashing = plainZipStream as HashingCountingInputStream
            val actualHash = hashing.digest()
            val actualSize = hashing.count
            require(expectedZipSha256 != null && expectedZipSize != null)

            if (!actualHash.contentEquals(expectedZipSha256))
                throw IllegalStateException("Backup integrity check failed (ZIP hash mismatch)")
            if (actualSize != expectedZipSize)
                throw IllegalStateException("Backup integrity check failed (ZIP size mismatch)")
        }
    }

    private class HashingCountingInputStream(
        private val inner: InputStream
    ) : InputStream() {
        private val digest = MessageDigest.getInstance("SHA-256")
        var count: Long = 0
            private set

        override fun read(): Int {
            val b = inner.read()
            if (b >= 0) {
                digest.update(b.toByte())
                count++
            }
            return b
        }

        override fun read(b: ByteArray, off: Int, len: Int): Int {
            val n = inner.read(b, off, len)
            if (n > 0) {
                digest.update(b, off, n)
                count += n.toLong()
            }
            return n
        }

        override fun close() {
            inner.close()
        }

        fun digest(): ByteArray = digest.digest()
    }

    private fun bytesToUuid(buf: ByteArray): String {
        val bb = ByteBuffer.wrap(buf)
        val msb = bb.long
        val lsb = bb.long
        return UUID(msb, lsb).toString()
    }

    private class NonClosingInputStream(private val inner: InputStream) : InputStream() {
        override fun read(): Int = inner.read()
        override fun read(b: ByteArray, off: Int, len: Int): Int = inner.read(b, off, len)
        override fun available(): Int = inner.available()
        override fun skip(n: Long): Long = inner.skip(n)
        override fun markSupported(): Boolean = inner.markSupported()
        override fun mark(readlimit: Int) = inner.mark(readlimit)
        override fun reset() = inner.reset()
        override fun close() { }
    }

    // -- Contacts restore ------------------------------------------------------

    private suspend fun restoreContacts(state: RestoreState, stream: ZipInputStream) {
        try {
            val result = contactsRepository.importVcf(NonClosingInputStream(stream), accountName = null, accountType = null)
            state.restoredItems += result.imported
            state.failedItems   += result.failed
            state.skippedItems  += result.skipped
            Log.d(TAG, "Contacts restore: imported=${result.imported} skipped=${result.skipped} failed=${result.failed}")
        } catch (e: Exception) {
            Log.e(TAG, "Failed to restore contacts: ${e.message}")
            state.failedItems++
        }
    }

    // -- Gallery restore -------------------------------------------------------

    private suspend fun restoreGalleryItem(state: RestoreState, stream: ZipInputStream, fileName: String) {
        val mimeType = guessMimeType(fileName)
        val isVideo  = mimeType.startsWith("video/")

        var itemUri: android.net.Uri? = null
        try {
            val collectionUri = if (isVideo) {
                MediaStore.Video.Media.getContentUri(MediaStore.VOLUME_EXTERNAL_PRIMARY)
            } else {
                MediaStore.Images.Media.getContentUri(MediaStore.VOLUME_EXTERNAL_PRIMARY)
            }

            val relativePath = if (isVideo) Environment.DIRECTORY_MOVIES else Environment.DIRECTORY_PICTURES
            val selection = "${MediaStore.MediaColumns.DISPLAY_NAME}=? AND ${MediaStore.MediaColumns.RELATIVE_PATH}=?"
            val selectionArgs = arrayOf(fileName, "$relativePath/")
            val alreadyExists = context.contentResolver.query(
                collectionUri,
                arrayOf(MediaStore.MediaColumns._ID),
                selection,
                selectionArgs,
                null
            )?.use { it.moveToFirst() } == true

            if (alreadyExists) {
                Log.d(TAG, "Skipping existing gallery item: $relativePath/$fileName")
                state.skippedItems++
                return
            }

            val values = ContentValues().apply {
                put(MediaStore.MediaColumns.DISPLAY_NAME, fileName)
                put(MediaStore.MediaColumns.MIME_TYPE, mimeType)
                put(MediaStore.MediaColumns.RELATIVE_PATH, relativePath)
                put(MediaStore.MediaColumns.IS_PENDING, 1)
            }

            val insertedUri = context.contentResolver.insert(collectionUri, values)
            if (insertedUri == null) {
                Log.w(TAG, "Could not insert gallery item: $fileName")
                state.failedItems++
                return
            }
            itemUri = insertedUri

            val digest = MessageDigest.getInstance("SHA-256")
            val out = context.contentResolver.openOutputStream(insertedUri)
                ?: throw IOException("Could not open output stream for gallery item: $fileName")
            out.use { rawOut ->
                DigestOutputStream(rawOut, digest).use { dout ->
                    stream.copyTo(dout, bufferSize = 512 * 1024)
                }
            }

            val sha256Hex = digest.digest().joinToString("") { b -> "%02x".format(b) }
            if (backupKeyStore.isGalleryHashRestored(sha256Hex)) {
                Log.d(TAG, "Skipping duplicate gallery content: $relativePath/$fileName")
                context.contentResolver.delete(insertedUri, null, null)
                itemUri = null
                state.skippedItems++
                return
            }

            // Clear IS_PENDING so the media scanner indexes the file.
            val clearValues = ContentValues().apply { put(MediaStore.MediaColumns.IS_PENDING, 0) }
            context.contentResolver.update(insertedUri, clearValues, null, null)

            backupKeyStore.addGalleryRestoredHash(sha256Hex)

            state.restoredItems++
        } catch (e: Exception) {
            Log.w(TAG, "Failed to restore gallery item $fileName: ${e.message}")
            itemUri?.let { uri ->
                runCatching { context.contentResolver.delete(uri, null, null) }
            }
            state.failedItems++
        }
    }

    // -- File restore ----------------------------------------------------------

    private fun restoreFile(state: RestoreState, stream: ZipInputStream, relPath: String) {
        val destDir = Environment.getExternalStoragePublicDirectory(Environment.DIRECTORY_DOWNLOADS)
        val dest = File(destDir, "VcfEditorRestore/$relPath")

        if (dest.exists()) {
            Log.d(TAG, "Skipping existing file: ${dest.absolutePath}")
            state.skippedItems++
            return
        }

        try {
            dest.parentFile?.mkdirs()
            dest.outputStream().use { out ->
                stream.copyTo(out, bufferSize = 512 * 1024)
            }
            state.restoredItems++
        } catch (e: Exception) {
            Log.w(TAG, "Failed to restore file $relPath: ${e.message}")
            state.failedItems++
        }
    }

    // -- Stream helpers --------------------------------------------------------

    /**
     * Reads exactly [n] bytes from the stream. Unlike [InputStream.readNBytes] (API 33+),
     * this works on all API levels by looping until all bytes are consumed.
     */
    private fun InputStream.readExactly(n: Int): ByteArray {
        val buf = ByteArray(n)
        var offset = 0
        while (offset < n) {
            val read = read(buf, offset, n - offset)
            if (read == -1) throw java.io.EOFException("Stream ended after $offset bytes, expected $n")
            offset += read
        }
        return buf
    }

    // -- Crypto helpers --------------------------------------------------------

    private suspend fun deriveKey(seed: ByteArray, salt: ByteArray): SecretKeySpec {
        val keyBytes = Hkdf.hkdfSha256(
            ikm = seed,
            salt = salt,
            info = "VCFEditor Backup Archive v1".toByteArray(Charsets.UTF_8),
            length = KEY_BITS / 8
        )
        return SecretKeySpec(keyBytes, "AES")
    }

    private fun guessMimeType(fileName: String): String {
        val ext = fileName.substringAfterLast('.', "").lowercase()
        return when (ext) {
            "jpg", "jpeg" -> "image/jpeg"
            "png"         -> "image/png"
            "gif"         -> "image/gif"
            "webp"        -> "image/webp"
            "heic","heif" -> "image/heic"
            "mp4"         -> "video/mp4"
            "mov"         -> "video/quicktime"
            "mkv"         -> "video/x-matroska"
            "avi"         -> "video/x-msvideo"
            "webm"        -> "video/webm"
            else          -> "application/octet-stream"
        }
    }
}
