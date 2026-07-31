package com.aeliavision.androiddeck.feature.filesystem.data

import android.content.Context
import android.webkit.MimeTypeMap
import com.aeliavision.androiddeck.feature.filesystem.model.FileEntry
import com.aeliavision.androiddeck.feature.server.service.PathSandbox
import dagger.hilt.android.qualifiers.ApplicationContext
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext
import java.io.File
import java.io.InputStream
import java.nio.file.Files
import java.nio.file.StandardCopyOption
import java.security.MessageDigest
import javax.inject.Inject
import javax.inject.Singleton

@Singleton
class FileSystemRepository @Inject constructor(
    @ApplicationContext private val context: Context
) {
    val defaultRoot: String
        get() = android.os.Environment.getExternalStorageDirectory().canonicalPath

    suspend fun listDirectory(path: String): List<FileEntry> = withContext(Dispatchers.IO) {
        val canonical = PathSandbox.validateDirectory(path)
        val dir = File(canonical)
        require(dir.isDirectory) { "Not a directory: $canonical" }
        (dir.listFiles() ?: emptyArray())
            .map { file -> file.toFileEntry() }
            .sortedWith(compareBy<FileEntry> { !it.isDirectory }.thenBy { it.name.lowercase() })
    }

    suspend fun openFileForRead(path: String): Pair<File, String> = withContext(Dispatchers.IO) {
        val canonical = PathSandbox.validateFile(path)
        val file = File(canonical)
        val checksum = computeSha256(file)
        Pair(file, checksum)
    }

    suspend fun uploadFile(
        destPath: String,
        inputStream: InputStream,
        expectedChecksum: String? = null
    ): Pair<FileEntry, String> = withContext(Dispatchers.IO) {
        val canonical = PathSandbox.validate(destPath)
        val dest = File(canonical)
        dest.parentFile?.mkdirs()
        inputStream.use { input ->
            dest.outputStream().use { output ->
                input.copyTo(output, bufferSize = 512 * 1024)
            }
        }
        val checksum = computeSha256(dest)
        if (expectedChecksum != null && !checksum.equals(expectedChecksum, ignoreCase = true)) {
            dest.delete()
            throw IllegalStateException("Upload checksum mismatch. Expected=$expectedChecksum actual=$checksum")
        }
        Pair(dest.toFileEntry(), checksum)
    }

    suspend fun delete(path: String): Boolean = withContext(Dispatchers.IO) {
        val canonical = PathSandbox.validate(path)
        val file = File(canonical)
        require(file.exists()) { "Path does not exist: $canonical" }
        if (file.isDirectory && (file.listFiles()?.isNotEmpty() == true)) {
            throw IllegalStateException("Directory is not empty: $canonical")
        }
        file.delete()
    }

    suspend fun deleteRecursive(path: String): Boolean = withContext(Dispatchers.IO) {
        val canonical = PathSandbox.validate(path)
        File(canonical).deleteRecursively()
    }

    suspend fun mkdir(path: String): FileEntry = withContext(Dispatchers.IO) {
        val canonical = PathSandbox.validate(path)
        val dir = File(canonical)
        if (!dir.exists()) {
            val ok = dir.mkdirs()
            check(ok) { "Failed to create directory: $canonical" }
        }
        dir.toFileEntry()
    }

    suspend fun rename(path: String, newName: String, overwrite: Boolean = false): FileEntry =
        withContext(Dispatchers.IO) {
            val canonical = PathSandbox.validate(path)
            val src = File(canonical)
            require(src.exists()) { "Path does not exist: $canonical" }
            require(newName.isNotBlank()) { "New name cannot be blank" }
            require(!newName.contains('/') && !newName.contains('\\')) { "Invalid name: $newName" }

            val dest = File(src.parentFile ?: throw IllegalStateException("No parent directory"), newName)
            moveInternal(src, dest, overwrite)
        }

    suspend fun move(fromPath: String, toPath: String, overwrite: Boolean = false): FileEntry =
        withContext(Dispatchers.IO) {
            val fromCanonical = PathSandbox.validate(fromPath)
            val toCanonical = PathSandbox.validate(toPath)
            val src = File(fromCanonical)
            val dest = File(toCanonical)
            require(src.exists()) { "Path does not exist: $fromCanonical" }
            moveInternal(src, dest, overwrite)
        }

    private fun moveInternal(src: File, dest: File, overwrite: Boolean): FileEntry {
        dest.parentFile?.mkdirs()

        if (dest.exists()) {
            if (!overwrite) {
                throw IllegalStateException("Destination already exists: ${dest.absolutePath}")
            }
            if (dest.isDirectory) dest.deleteRecursively() else dest.delete()
        }

        return try {
            if (overwrite) {
                Files.move(src.toPath(), dest.toPath(), StandardCopyOption.REPLACE_EXISTING)
            } else {
                Files.move(src.toPath(), dest.toPath())
            }
            dest.toFileEntry()
        } catch (_: Exception) {
            val ok = src.renameTo(dest)
            check(ok) { "Failed to move '${src.absolutePath}' -> '${dest.absolutePath}'" }
            dest.toFileEntry()
        }
    }

    suspend fun writeChunk(
        transferId: String,
        chunkIndex: Int,
        chunkBytes: ByteArray
    ): Unit = withContext(Dispatchers.IO) {
        val chunkDir = getChunkDir(transferId)
        chunkDir.mkdirs()
        File(chunkDir, "chunk_$chunkIndex").writeBytes(chunkBytes)
    }

    suspend fun finalizeChunkedUpload(
        transferId: String,
        destPath: String,
        totalChunks: Int,
        expectedChecksum: String
    ): Pair<FileEntry, String> = withContext(Dispatchers.IO) {
        val canonical = PathSandbox.validate(destPath)
        val dest = File(canonical)
        dest.parentFile?.mkdirs()
        val chunkDir = getChunkDir(transferId)
        dest.outputStream().use { out ->
            for (i in 0 until totalChunks) {
                val chunk = File(chunkDir, "chunk_$i")
                require(chunk.exists()) { "Missing chunk $i for transfer $transferId" }
                chunk.inputStream().use { it.copyTo(out) }
            }
        }
        val actualChecksum = computeSha256(dest)
        if (!actualChecksum.equals(expectedChecksum, ignoreCase = true)) {
            dest.delete()
            chunkDir.deleteRecursively()
            throw IllegalStateException(
                "Chunked upload checksum mismatch for $transferId. Expected=$expectedChecksum actual=$actualChecksum"
            )
        }
        chunkDir.deleteRecursively()
        Pair(dest.toFileEntry(), actualChecksum)
    }

    suspend fun getReceivedChunks(transferId: String): List<Int> = withContext(Dispatchers.IO) {
        val chunkDir = getChunkDir(transferId)
        if (!chunkDir.exists()) return@withContext emptyList()
        chunkDir.listFiles()
            ?.filter { it.name.startsWith("chunk_") }
            ?.mapNotNull { it.name.removePrefix("chunk_").toIntOrNull() }
            ?.sorted()
            ?: emptyList()
    }

    private fun getChunkDir(transferId: String): File =
        File(context.cacheDir, "uploads/$transferId")

    private fun computeSha256(file: File): String {
        val digest = MessageDigest.getInstance("SHA-256")
        file.inputStream().use { input ->
            val buf = ByteArray(512 * 1024)
            var read: Int
            while (input.read(buf).also { read = it } != -1)
                digest.update(buf, 0, read)
        }
        return digest.digest().joinToString("") { "%02x".format(it) }
    }

    private fun File.toFileEntry(): FileEntry {
        val mime = if (isDirectory) {
            "inode/directory"
        } else {
            MimeTypeMap.getSingleton().getMimeTypeFromExtension(extension.lowercase())
                ?: "application/octet-stream"
        }
        return FileEntry(
            name = name,
            path = absolutePath,
            isDirectory = isDirectory,
            size = if (isFile) length() else 0L,
            lastModified = lastModified(),
            mimeType = mime
        )
    }
}
