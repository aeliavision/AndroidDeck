package com.aeliavision.androiddeck.feature.backup.data

/**
 * Maps backup phases to one monotonic 0..1 progress value for the desktop UI.
 *
 * One hundred percent is reserved for a READY archive. This prevents the UI from
 * jumping backwards to 0% when the backup moves from packaging to encryption.
 */
internal object BackupProgress {
    private const val INDEXING_PROGRESS = 0.01f
    private const val PACKAGING_START = 0.05f
    private const val PACKAGING_END = 0.85f
    private const val ENCRYPTING_START = PACKAGING_END
    private const val ENCRYPTING_END = 0.99f

    fun indexing(): Float = INDEXING_PROGRESS

    fun packaging(processedItems: Int, totalItems: Int): Float {
        val fraction = if (totalItems <= 0) 0f
        else (processedItems.toFloat() / totalItems.toFloat()).coerceIn(0f, 1f)
        return PACKAGING_START + (PACKAGING_END - PACKAGING_START) * fraction
    }

    fun encrypting(bytesCopied: Long, totalBytes: Long): Float {
        val fraction = if (totalBytes <= 0L) 0f
        else (bytesCopied.toDouble() / totalBytes.toDouble()).coerceIn(0.0, 1.0).toFloat()
        return ENCRYPTING_START + (ENCRYPTING_END - ENCRYPTING_START) * fraction
    }

    fun ready(): Float = 1f
}
