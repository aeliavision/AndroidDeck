package com.aeliavision.androiddeck.feature.server.service

import org.junit.Assert.*
import org.junit.Test

class RateLimiterTest {
    @Test fun generalLimitIsIndependentPerClient() {
        var now = 1_000L
        val limiter = RateLimiter(RateLimiter.Config().apply {
            maxRequestsPerWindow = 2; windowMs = 1_000
        }) { now }
        assertTrue(limiter.allowRequest("a"))
        assertTrue(limiter.allowRequest("a"))
        assertFalse(limiter.allowRequest("a"))
        assertTrue(limiter.allowRequest("b"))
        now += 1_001
        assertTrue(limiter.allowRequest("a"))
    }

    @Test fun pairingLimitIsSeparateFromGeneralLimit() {
        val limiter = RateLimiter(RateLimiter.Config().apply {
            maxRequestsPerWindow = 100; pairingMaxRequestsPerWindow = 1
        }) { 5_000L }
        assertTrue(limiter.allowPairingRequest("client"))
        assertFalse(limiter.allowPairingRequest("client"))
        assertTrue(limiter.allowRequest("client"))
    }

    @Test fun transferSlotsReleaseAndRemoveZeroEntries() {
        val limiter = RateLimiter(RateLimiter.Config().apply { maxConcurrentTransfers = 2 })
        assertTrue(limiter.acquireTransferSlot("client"))
        assertTrue(limiter.acquireTransferSlot("client"))
        assertFalse(limiter.acquireTransferSlot("client"))
        limiter.releaseTransferSlot("client")
        assertTrue(limiter.acquireTransferSlot("client"))
        limiter.releaseTransferSlot("client")
        limiter.releaseTransferSlot("client")
        assertEquals(0, limiter.activeTransferIdentityCount())
    }

    @Test fun expiredWindowsAreCleanedOpportunistically() {
        var now = 0L
        val limiter = RateLimiter(RateLimiter.Config().apply { windowMs = 100 }) { now }
        assertTrue(limiter.allowRequest("old"))
        now = 250
        assertTrue(limiter.allowRequest("new"))
        assertEquals(1, limiter.trackedWindowCount())
    }
}
