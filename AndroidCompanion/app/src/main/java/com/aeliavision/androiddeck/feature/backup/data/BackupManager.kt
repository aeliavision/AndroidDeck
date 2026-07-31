package com.aeliavision.androiddeck.feature.backup.data

import android.content.ContentUris
import android.content.Context
import android.os.Environment
import android.provider.ContactsContract
import android.provider.MediaStore
import android.util.Log
import com.aeliavision.androiddeck.feature.backup.model.BackupPhase
import com.aeliavision.androiddeck.feature.backup.model.BackupState
import com.aeliavision.androiddeck.feature.backup.model.BackupType
import com.aeliavision.androiddeck.feature.contacts.data.ContactsRepository
import com.aeliavision.androiddeck.feature.dashboard.data.ActivityLogRepository
import com.aeliavision.androiddeck.feature.dashboard.model.ActivityType
import dagger.hilt.android.qualifiers.ApplicationContext
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.cancel
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext
import org.json.JSONArray
import org.json.JSONObject
import java.io.File
import java.io.InputStream
import java.nio.ByteBuffer
import java.security.MessageDigest
import java.security.SecureRandom
import java.util.UUID
import java.util.concurrent.ConcurrentHashMap
import java.util.zip.ZipEntry
import java.util.zip.ZipOutputStream
import javax.crypto.Cipher
import javax.crypto.CipherOutputStream
import com.aeliavision.androiddeck.feature.backup.crypto.Hkdf
import javax.crypto.spec.GCMParameterSpec
import javax.crypto.spec.SecretKeySpec
/**
 *
 * Creates encrypted backup archives (.vcfbak) containing any combination of:
 *   - contacts/ — all contacts exported as a single contacts.vcf
 *   - gallery/  — original-resolution media files from MediaStore
 *   - files/    — arbitrary file-system paths (sandboxed)
 *
 * Archive format
 * --------------
 * The outer file is a raw binary with the following layout:
 *
 *   [4 bytes]  magic  = 0x56 0x43 0x46 0x42  ("VCFB")
 *   [1 byte]   version = 0x03
 *   [16 bytes] seedId (UUID bytes)
 *   [16 bytes] PBKDF2 salt
 *   [12 bytes] AES-GCM IV
 *   [32 bytes] SHA-256 of the plaintext ZIP stream
 *   [8 bytes]  ZIP length (big-endian)
 *   [N bytes]  AES-256-GCM ciphertext of a ZIP stream
 *
 * The ZIP stream contains:
 *   manifest.json           — metadata (types, itemCount, createdAt, ...)
 *   contacts/contacts.vcf   — vCard 3.0 dump of all contacts
 *   gallery/<name>          — original media files
 *   files/<relPath>         — arbitrary files
 *
 * Encryption
 * ----------
 * Key = HKDF-SHA256(backupSeed, salt, info="VCFEditor Backup Archive v1", 32 bytes)
 * Cipher = AES/GCM/NoPadding, 256-bit key, 128-bit tag, 12-byte IV.
 *
 * backupSeed is a persistent 32-byte secret stored securely on the device (Android Keystore-wrapped)
 * so archives remain decryptable across restarts and pairing/session expiry.
 */
@javax.inject.Singleton
class BackupManager @javax.inject.Inject constructor(
    @ApplicationContext private val context: Context,
    private val contactsRepository: ContactsRepository,
    private val backupKeyStore: BackupKeyStore,
    private val activityLogRepository: ActivityLogRepository
) {
    companion object {
        private const val TAG = "BackupManager"

        // File magic + version
        private val MAGIC = byteArrayOf(0x56, 0x43, 0x46, 0x42)
        private const val VERSION: Byte = 0x03

        private const val KEY_BITS = 256
        private const val GCM_TAG_BITS = 128
        private const val IV_BYTES = 12
        private const val SALT_BYTES = 16
        private const val SEED_ID_BYTES = 16
        private const val ZIP_HASH_BYTES = 32
        private const val ZIP_SIZE_BYTES = 8
    }

    // --- In-memory state maps ----------------------------------------------------

    /** Active and recently-completed backup jobs. */
    private val backups = ConcurrentHashMap<String, BackupState>()

    /** Persisted history entries (completed backups). */
    private val history = ConcurrentHashMap<String, BackupState>()

    /** Dedicated scope for background backup jobs (do not block request handlers). */
    @Volatile
    private var scopeJob = SupervisorJob()

    @Volatile
    private var jobScope = CoroutineScope(scopeJob + Dispatchers.IO)

    fun shutdown() {
        // THR-W01: Cancel in-flight backups when the service/server stops.
        // Keep it renewable so future server restarts in the same process can still back up.
        scopeJob.cancel("BackupManager shutdown")
        scopeJob = SupervisorJob()
        jobScope = CoroutineScope(scopeJob + Dispatchers.IO)
    }

    // --- Public API --------------------------------------------------------------

    fun getState(backupId: String): BackupState? = backups[backupId]

    fun getHistory(): List<BackupState> =
        history.values.sortedByDescending { it.createdAt }

    /**
     * Launch a backup asynchronously. The caller should poll [getState] for progress.
     * Runs entirely on [Dispatchers.IO].
     *
     * @param types    which data categories to include ("contacts", "gallery", "files")
     * @param paths    extra file-system paths (used when "files" is in types)
     * @param secret   HMAC session secret — used as PBKDF2 password material
     * @return the new [BackupState.backupId]
     */
    fun startBackup(
        types: List<String>,
        paths: List<String>,
        encrypt: Boolean = true,
        incremental: Boolean = false,
        sinceMs: Long? = null
    ): String {
        val backupId = UUID.randomUUID().toString()
        val state = BackupState(backupId = backupId, types = types)
        backups[backupId] = state

        // Run asynchronously so POST /backup/create can respond immediately.
        jobScope.launch {
            try {
                runBackup(state, types, paths, encrypt, incremental, sinceMs)
            } catch (e: Exception) {
                Log.e(TAG, "Backup $backupId failed", e)
                state.phase = BackupPhase.FAILED
                state.error = e.message ?: "Unknown error"
            }
        }

        return backupId
    }

    /**
     * Open an [InputStream] to the completed, encrypted archive.
     * Throws [IllegalStateException] if the backup is not ready.
     */
    fun openArchiveStream(backupId: String): Pair<InputStream, Long> {
        val state = backups[backupId]
            ?: throw IllegalStateException("Backup $backupId not found")
        if (state.phase != BackupPhase.READY)
            throw IllegalStateException("Backup $backupId is not ready (phase=${state.phase})")
        val file = state.archiveFile
            ?: throw IllegalStateException("Backup $backupId has no archive file")
        return Pair(file.inputStream(), file.length())
    }

    // --- Private — backup engine -------------------------------------------------

    private suspend fun runBackup(
        state: BackupState,
        types: List<String>,
        extraPaths: List<String>,
        encrypt: Boolean,
        incremental: Boolean,
        sinceMs: Long?,
    ) = withContext(Dispatchers.IO) {

        val backupTypes = types.mapNotNull { t ->
            runCatching { BackupType.valueOf(t.uppercase()) }.getOrNull()
        }
        state.phase = BackupPhase.INDEXING
        state.progress = BackupProgress.indexing()

        val gallerySince = if (incremental) (sinceMs ?: backupKeyStore.getIncrementalWatermark("gallery")) else null
        val filesSince = if (incremental) (sinceMs ?: backupKeyStore.getIncrementalWatermark("files")) else null

        val contactIds = if (BackupType.CONTACTS in backupTypes)
            contactsRepository.getAllContactIds() else emptyList()

        val galleryIndex = if (BackupType.GALLERY in backupTypes)
            indexGalleryMedia(gallerySince) else GalleryIndex(itemCount = 0, maxModifiedMs = gallerySince)
        val filesIndexed = if (BackupType.FILES in backupTypes)
            indexFiles(extraPaths, filesSince) else Indexed(items = emptyList(), maxModifiedMs = filesSince)

        val filePaths = filesIndexed.items

        state.itemCount = contactIds.size + galleryIndex.itemCount + filePaths.size
        Log.d(TAG, "Backup ${state.backupId}: ${state.itemCount} items to pack")
        state.phase = BackupPhase.PACKAGING
        state.progress = BackupProgress.packaging(processedItems = 0, totalItems = state.itemCount)
        val tempZip = File(context.cacheDir, "backup_${state.backupId}.zip.tmp")

        try {
            tempZip.outputStream().buffered().use { rawOut ->
                ZipOutputStream(rawOut).use { zos ->
                    // manifest.json
                    zos.putNextEntry(ZipEntry("manifest.json"))
                    val manifest = buildManifest(state, types)
                    zos.write(manifest.toByteArray(Charsets.UTF_8))
                    zos.closeEntry()

                    var processed = 0

                    // contacts/contacts.vcf
                    if (BackupType.CONTACTS in backupTypes && contactIds.isNotEmpty()) {
                        state.currentItem = "contacts/contacts.vcf"
                        zos.putNextEntry(ZipEntry("contacts/contacts.vcf"))
                        // Stream-export all contacts using the repository paging path to avoid
                        // per-contact vCard resolver calls and large in-memory allocations.
                        // (BackupType.CONTACTS represents a full export.)
                        contactsRepository.exportVcfTo(zos, contactIds = null)

                        // Keep item-based progress consistent with the precomputed itemCount.
                        processed += contactIds.size
                        state.processedItems = processed
                        state.progress = BackupProgress.packaging(processed, state.itemCount)
                        zos.closeEntry()
                        state.processedItems = processed
                        state.progress = BackupProgress.packaging(processed, state.itemCount)
                    }

                    // gallery/<filename>
                    if (BackupType.GALLERY in backupTypes) {
                        processed = packGalleryMedia(
                            zos = zos,
                            sinceMs = gallerySince,
                            state = state,
                            processed = processed
                        )
                    }

                    // files/<relPath>
                    if (BackupType.FILES in backupTypes) {
                        val baseRoot = Environment.getExternalStorageDirectory().canonicalPath
                        for (path in filePaths) {
                            val file = File(path)
                            val rel = try {
                                "files/" + file.canonicalPath.removePrefix(baseRoot).trimStart('/')
                            } catch (e: Exception) {
                                "files/${file.name}"
                            }
                            state.currentItem = rel
                            try {
                                file.inputStream().use { input ->
                                    zos.putNextEntry(ZipEntry(rel))
                                    input.copyTo(zos, bufferSize = 512 * 1024)
                                    zos.closeEntry()
                                }
                            } catch (e: Exception) {
                                Log.w(TAG, "Skipping file $path: ${e.message}")
                            }
                            processed++
                            state.processedItems = processed
                            state.progress = BackupProgress.packaging(processed, state.itemCount)
                        }
                    }
                }
            }
            state.phase = BackupPhase.ENCRYPTING
            val zipBytes = tempZip.length()
            state.progress = BackupProgress.encrypting(bytesCopied = 0L, totalBytes = zipBytes)

            val archiveFile = if (encrypt) {
                val out = File(context.cacheDir, "backup_${state.backupId}.deckbak")
                encryptZipToArchive(tempZip, out) { copied, total ->
                    state.progress = BackupProgress.encrypting(copied, total)
                }
                tempZip.delete()
                out
            } else {
                // Plain ZIP archive (no encryption).
                val out = File(context.cacheDir, "backup_${state.backupId}.zip")
                if (out.exists()) out.delete()
                tempZip.renameTo(out)
                state.progress = BackupProgress.encrypting(zipBytes, zipBytes)
                out
            }

            state.archiveFile = archiveFile
            state.archiveSize = archiveFile.length()
            state.phase = BackupPhase.READY
            state.progress = BackupProgress.ready()
            state.currentItem = ""

            // Move to history
            history[state.backupId] = state
            Log.i(TAG, "Backup ${state.backupId} complete — ${state.archiveSize} bytes")

            activityLogRepository.logActivity(
                title = "Backup successful (${state.processedItems} items)",
                type = ActivityType.BACKUP
            )

            if (incremental) {
                galleryIndex.maxModifiedMs?.let { backupKeyStore.setIncrementalWatermark("gallery", it) }
                filesIndexed.maxModifiedMs?.let { backupKeyStore.setIncrementalWatermark("files", it) }
            }

        } catch (e: Exception) {
            tempZip.delete()
            throw e
        }
    }

    // --- Gallery indexing --------------------------------------------------------

    private data class Indexed<T>(
        val items: List<T>,
        val maxModifiedMs: Long?
    )

    private data class GalleryIndex(
        val itemCount: Int,
        val maxModifiedMs: Long?
    )

    private fun indexGalleryMedia(sinceMs: Long?): GalleryIndex {
        val images = queryGalleryCountAndMaxModified(MediaStore.Images.Media.EXTERNAL_CONTENT_URI, sinceMs)
        val videos = queryGalleryCountAndMaxModified(MediaStore.Video.Media.EXTERNAL_CONTENT_URI, sinceMs)
        val maxModified = listOfNotNull(images.maxModifiedMs, videos.maxModifiedMs, sinceMs).maxOrNull()
        return GalleryIndex(itemCount = images.itemCount + videos.itemCount, maxModifiedMs = maxModified)
    }

    private data class CountAndMaxModified(
        val itemCount: Int,
        val maxModifiedMs: Long?
    )

    private fun queryGalleryCountAndMaxModified(
        baseUri: android.net.Uri,
        sinceMs: Long?
    ): CountAndMaxModified {
        val projection = arrayOf(MediaStore.MediaColumns.DATE_MODIFIED)

        val selection: String?
        val selectionArgs: Array<String>?

        if (sinceMs != null) {
            // DATE_MODIFIED is seconds.
            val sinceSec = (sinceMs / 1000L)
            selection = "${MediaStore.MediaColumns.DATE_MODIFIED} > ?"
            selectionArgs = arrayOf(sinceSec.toString())
        } else {
            selection = null
            selectionArgs = null
        }

        var count = 0
        var maxModified: Long? = sinceMs

        context.contentResolver.query(
            baseUri,
            projection,
            selection,
            selectionArgs,
            "${MediaStore.MediaColumns.DATE_MODIFIED} DESC"
        )?.use { cursor ->
            count = cursor.count
            if (cursor.moveToFirst()) {
                val dateIdx = cursor.getColumnIndex(MediaStore.MediaColumns.DATE_MODIFIED)
                if (dateIdx >= 0) {
                    val modSec = cursor.getLong(dateIdx)
                    val modMs = modSec * 1000L
                    if (maxModified == null || modMs > maxModified!!) maxModified = modMs
                }
            }
        }

        return CountAndMaxModified(itemCount = count, maxModifiedMs = maxModified)
    }

    private fun packGalleryMedia(
        zos: ZipOutputStream,
        sinceMs: Long?,
        state: BackupState,
        processed: Int
    ): Int {
        var processedLocal = processed

        fun packFromStore(baseUri: android.net.Uri) {
            val proj = arrayOf(
                MediaStore.MediaColumns._ID,
                MediaStore.MediaColumns.DISPLAY_NAME,
                MediaStore.MediaColumns.DATE_MODIFIED
            )

            val selection: String?
            val selectionArgs: Array<String>?
            if (sinceMs != null) {
                val sinceSec = (sinceMs / 1000L)
                selection = "${MediaStore.MediaColumns.DATE_MODIFIED} > ?"
                selectionArgs = arrayOf(sinceSec.toString())
            } else {
                selection = null
                selectionArgs = null
            }

            context.contentResolver.query(
                baseUri,
                proj,
                selection,
                selectionArgs,
                "${MediaStore.MediaColumns.DATE_TAKEN} DESC"
            )?.use { cursor ->
                val idIdx = cursor.getColumnIndex(MediaStore.MediaColumns._ID)
                val nameIdx = cursor.getColumnIndex(MediaStore.MediaColumns.DISPLAY_NAME)

                while (cursor.moveToNext()) {
                    val id = cursor.getLong(idIdx)
                    val rawName = cursor.getString(nameIdx) ?: "media_$id"
                    val name = "${id}_$rawName" // ensures uniqueness without a large dedupe map
                    val uri = ContentUris.withAppendedId(baseUri, id)

                    state.currentItem = "gallery/$name"
                    try {
                        context.contentResolver.openInputStream(uri)?.use { input ->
                            zos.putNextEntry(ZipEntry("gallery/$name"))
                            input.copyTo(zos, bufferSize = 512 * 1024)
                            zos.closeEntry()
                        }
                    } catch (e: Exception) {
                        Log.w(TAG, "Skipping gallery item $name: ${e.message}")
                    }

                    processedLocal++
                    state.processedItems = processedLocal
                    state.progress = BackupProgress.packaging(processedLocal, state.itemCount)
                }
            }
        }

        packFromStore(MediaStore.Images.Media.EXTERNAL_CONTENT_URI)
        packFromStore(MediaStore.Video.Media.EXTERNAL_CONTENT_URI)

        return processedLocal
    }

    // --- File system indexing ----------------------------------------------------

    private fun indexFiles(extraPaths: List<String>, sinceMs: Long?): Indexed<String> {
        val defaultPaths = listOf(
            Environment.getExternalStoragePublicDirectory(Environment.DIRECTORY_DOWNLOADS).canonicalPath,
            Environment.getExternalStoragePublicDirectory(Environment.DIRECTORY_DOCUMENTS).canonicalPath
        )
        val roots = (defaultPaths + extraPaths).distinct()
        val results = mutableListOf<String>()
        var maxModified: Long? = sinceMs
        for (root in roots) {
            val dir = File(root)
            if (dir.exists() && dir.isDirectory) {
                dir.walkTopDown()
                    .filter { it.isFile }
                    .forEach {
                        val lm = it.lastModified()
                        if (sinceMs != null && lm <= sinceMs) return@forEach
                        if (maxModified == null || lm > maxModified!!) maxModified = lm
                        results.add(it.canonicalPath)
                    }
            }
        }
        return Indexed(items = results, maxModifiedMs = maxModified)
    }

    // --- Encryption --------------------------------------------------------------

    /**
     * Encrypts [plainZip] using AES-256-GCM and writes the result to [dest].
     * Format: MAGIC (4) | VERSION (1) | SEED_ID (16) | SALT (16) | IV (12) | ZIP_SHA256 (32) | ZIP_SIZE (8) | GCM ciphertext
     */
    private suspend fun encryptZipToArchive(
        plainZip: File,
        dest: File,
        onProgress: (bytesCopied: Long, totalBytes: Long) -> Unit
    ) {
        val zipSize = plainZip.length()
        val zipSha256 = sha256OfFile(plainZip)

        val (seedId, seed) = backupKeyStore.getOrCreateCurrentBackupSeed()
        val seedBytes = uuidToBytes(seedId)

        val rng  = SecureRandom()
        val salt = ByteArray(SALT_BYTES).also { rng.nextBytes(it) }
        val iv   = ByteArray(IV_BYTES).also   { rng.nextBytes(it) }
        val key = deriveKey(seed, salt)

        val cipher = Cipher.getInstance("AES/GCM/NoPadding")
        cipher.init(Cipher.ENCRYPT_MODE, key, GCMParameterSpec(GCM_TAG_BITS, iv))

        dest.outputStream().buffered().use { out ->
            out.write(MAGIC)
            out.write(VERSION.toInt())
            out.write(seedBytes)
            out.write(salt)
            out.write(iv)
            out.write(zipSha256)
            out.write(ByteBuffer.allocate(ZIP_SIZE_BYTES).putLong(zipSize).array())
            CipherOutputStream(out, cipher).use { cipherOut ->
                plainZip.inputStream().buffered().use { input ->
                    val buffer = ByteArray(512 * 1024)
                    var copied = 0L
                    while (true) {
                        val read = input.read(buffer)
                        if (read <= 0) break
                        cipherOut.write(buffer, 0, read)
                        copied += read
                        onProgress(copied, zipSize)
                    }
                }
            }
        }
    }

    private fun sha256OfFile(file: File): ByteArray {
        val digest = MessageDigest.getInstance("SHA-256")
        val buffer = ByteArray(512 * 1024)
        file.inputStream().use { input ->
            while (true) {
                val read = input.read(buffer)
                if (read <= 0) break
                digest.update(buffer, 0, read)
            }
        }
        return digest.digest()
    }

    private suspend fun deriveKey(seed: ByteArray, salt: ByteArray): SecretKeySpec {
        val keyBytes = Hkdf.hkdfSha256(
            ikm = seed,
            salt = salt,
            info = "VCFEditor Backup Archive v1".toByteArray(Charsets.UTF_8),
            length = KEY_BITS / 8
        )
        return SecretKeySpec(keyBytes, "AES")
    }

    private fun uuidToBytes(seedId: String): ByteArray {
        val uuid = UUID.fromString(seedId)
        return ByteBuffer.allocate(SEED_ID_BYTES)
            .putLong(uuid.mostSignificantBits)
            .putLong(uuid.leastSignificantBits)
            .array()
    }

    // --- Manifest ----------------------------------------------------------------

    private fun buildManifest(state: BackupState, types: List<String>): String {
        val obj = JSONObject()
        obj.put("version", 1)
        obj.put("backupId", state.backupId)
        obj.put("createdAt", state.createdAt)
        obj.put("types", JSONArray(types))
        obj.put("itemCount", state.itemCount)
        return obj.toString()
    }
}
