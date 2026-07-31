package com.aeliavision.androiddeck.feature.filesystem.model

import androidx.annotation.Keep

/**
 */

@Keep
data class FileEntry(
    val name: String,
    val path: String,
    val isDirectory: Boolean,
    val size: Long,
    val lastModified: Long,
    val mimeType: String
)

@Keep
data class DirectoryListing(
    val path: String,
    val parent: String?,
    val items: List<FileEntry>
)

@Keep
data class UploadResult(
    val path: String,
    val size: Long,
    val checksum: String
)

@Keep
data class DeleteResult(
    val success: Boolean,
    val path: String
)

@Keep
data class MkdirResult(
    val path: String,
    val created: Boolean
)

@Keep
data class StreamInitRequest(
    val fileName: String,
    val destinationDirectory: String,
    val totalSize: Long,
    val checksum: String,
    val chunkSize: Int = 1024 * 1024
)

@Keep
data class StreamInitResponse(
    val transferId: String,
    val chunkSize: Int
)

@Keep
data class ChunkAck(
    val received: Boolean,
    val chunkIndex: Int
)

@Keep
data class StreamCompleteResponse(
    val success: Boolean,
    val finalPath: String,
    val verifiedChecksum: Boolean
)

@Keep
data class StreamStatusResponse(
    val transferId: String,
    val chunksReceived: List<Int>,
    val totalChunks: Int,
    val bytesReceived: Long
)
