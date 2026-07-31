package com.aeliavision.androiddeck.feature.backup.data

import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Test

class BackupProgressTest {
    @Test
    fun indexingStartsWithVisibleProgress() {
        assertTrue(BackupProgress.indexing() > 0f)
    }

    @Test
    fun enteringEncryptionDoesNotResetProgressToZero() {
        val packaged = BackupProgress.packaging(processedItems = 10, totalItems = 10)
        val encrypting = BackupProgress.encrypting(bytesCopied = 0L, totalBytes = 1_000L)

        assertTrue(encrypting >= packaged)
        assertTrue(encrypting > 0f)
    }

    @Test
    fun encryptionProgressAdvancesAndReservesOneHundredPercentForReady() {
        val start = BackupProgress.encrypting(bytesCopied = 0L, totalBytes = 1_000L)
        val halfway = BackupProgress.encrypting(bytesCopied = 500L, totalBytes = 1_000L)
        val finishedCopy = BackupProgress.encrypting(bytesCopied = 1_000L, totalBytes = 1_000L)

        assertTrue(halfway > start)
        assertTrue(finishedCopy > halfway)
        assertTrue(finishedCopy < 1f)
        assertEquals(1f, BackupProgress.ready(), 0f)
    }
}
