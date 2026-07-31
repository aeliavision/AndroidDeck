package com.aeliavision.androiddeck.feature.filesystem.data

import com.aeliavision.androiddeck.feature.filesystem.model.StreamInitRequest
import java.util.UUID
import java.util.concurrent.ConcurrentHashMap
import javax.inject.Inject
import javax.inject.Singleton

/**
 */
@Singleton
class StreamTransferManager @Inject constructor() {

    companion object {
        private const val SESSION_TTL_MS = 30 * 60 * 1000L  // 30 minutes
    }

    data class TransferSession(
        val transferId: String,
        val fileName: String,
        val destinationDirectory: String,
        val totalSize: Long,
        val expectedChecksum: String,
        val chunkSize: Int,
        val totalChunks: Int,
        val createdAt: Long = System.currentTimeMillis()
    ) {
        val isExpired: Boolean
            get() = System.currentTimeMillis() - createdAt > SESSION_TTL_MS

        val destPath: String
            get() = "$destinationDirectory/$fileName"
    }

    private val sessions = ConcurrentHashMap<String, TransferSession>()

    fun createSession(request: StreamInitRequest): TransferSession {
        evictExpired()
        val totalChunks = ((request.totalSize + request.chunkSize - 1) / request.chunkSize).toInt()
        val session = TransferSession(
            transferId = UUID.randomUUID().toString(),
            fileName = request.fileName,
            destinationDirectory = request.destinationDirectory,
            totalSize = request.totalSize,
            expectedChecksum = request.checksum,
            chunkSize = request.chunkSize,
            totalChunks = totalChunks
        )
        sessions[session.transferId] = session
        return session
    }

    fun getSession(transferId: String): TransferSession? {
        val session = sessions[transferId] ?: return null
        if (session.isExpired) {
            sessions.remove(transferId)
            return null
        }
        return session
    }

    fun removeSession(transferId: String) {
        sessions.remove(transferId)
    }

    private fun evictExpired() {
        sessions.entries.removeIf { it.value.isExpired }
    }
}
