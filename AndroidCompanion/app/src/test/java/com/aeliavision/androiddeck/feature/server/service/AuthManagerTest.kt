package com.aeliavision.androiddeck.feature.server.service

import java.util.Base64
import javax.crypto.Mac
import javax.crypto.spec.SecretKeySpec
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNotEquals
import org.junit.Assert.assertNotNull
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test

class AuthManagerTest {

    @Test
    fun pair_rejectsInvalidCodeWithoutRotatingCurrentCode() {
        val clock = MutableClock(1_000L)
        val manager = AuthManager(clock::now)
        val originalCode = manager.currentPairingCode

        val session = manager.pair("000000", "desktop-1")

        assertNull(session)
        assertEquals(originalCode, manager.currentPairingCode)
        assertEquals(0, manager.sessionCount.value)
    }

    @Test
    fun pair_rotatesCodeAndCreatesExpiringSession() {
        val clock = MutableClock(5_000L)
        val manager = AuthManager(clock::now)
        val originalCode = manager.currentPairingCode

        val session = manager.pair(originalCode, "desktop-1")

        assertNotNull(session)
        assertNotEquals(originalCode, manager.currentPairingCode)
        assertEquals(clock.now(), session!!.createdAt)
        assertTrue(session.expiresAt > session.createdAt)
        assertEquals(1, manager.sessionCount.value)
    }

    @Test
    fun verifyRequest_removesExpiredSession() {
        val clock = MutableClock(10_000L)
        val manager = AuthManager(clock::now)
        val session = requireNotNull(manager.pair(manager.currentPairingCode, "desktop-1"))
        clock.advance(days(31))
        val timestamp = clock.now().toString()
        val authorization = authorization(
            session.hmacSecret,
            "GET",
            "/api/v2/status",
            timestamp,
            "expiry-nonce",
            EMPTY_BODY_HASH
        )

        val verified = manager.verifyRequest(
            "desktop-1",
            timestamp,
            "expiry-nonce",
            authorization,
            "GET",
            "/api/v2/status",
            EMPTY_BODY_HASH
        )

        assertFalse(verified)
        assertEquals(0, manager.getActiveSessionCount())
        assertEquals(0, manager.sessionCount.value)
    }

    @Test
    fun pair_capsSessionsAndEvictsLeastRecentlyUsedClient() {
        val clock = MutableClock(100_000L)
        val manager = AuthManager(clock::now)

        repeat(11) { index ->
            requireNotNull(manager.pair(manager.currentPairingCode, "desktop-$index"))
            clock.advance(1L)
        }

        assertEquals(10, manager.getActiveSessionCount())
        assertEquals(10, manager.sessionCount.value)
        assertFalse(manager.revokeSession("desktop-0"))
        assertTrue(manager.revokeSession("desktop-10"))
    }

    @Test
    fun verifyRequest_acceptsValidHmacAndRejectsCurrentNonceReplay() {
        val clock = MutableClock(300_000L)
        val manager = AuthManager(clock::now)
        val session = requireNotNull(manager.pair(manager.currentPairingCode, "desktop-1"))
        val timestamp = clock.now().toString()
        val authorization = authorization(
            session.hmacSecret,
            "POST",
            "/api/v2/contacts",
            timestamp,
            "nonce-1",
            "body-hash"
        )

        val first = manager.verifyRequest(
            "desktop-1", timestamp, "nonce-1", authorization,
            "POST", "/api/v2/contacts?sort=name", "body-hash"
        )
        val replay = manager.verifyRequest(
            "desktop-1", timestamp, "nonce-1", authorization,
            "POST", "/api/v2/contacts?sort=name", "body-hash"
        )

        assertTrue(first)
        assertFalse(replay)
    }

    @Test
    fun verifyRequest_rejectsNonceReplayAcrossWindowBoundary() {
        val clock = MutableClock(599_999L)
        val manager = AuthManager(clock::now)
        val session = requireNotNull(manager.pair(manager.currentPairingCode, "desktop-1"))
        val firstTimestamp = clock.now().toString()
        val firstAuthorization = authorization(
            session.hmacSecret,
            "GET",
            "/api/v2/status",
            firstTimestamp,
            "boundary-nonce",
            EMPTY_BODY_HASH
        )
        assertTrue(
            manager.verifyRequest(
                "desktop-1", firstTimestamp, "boundary-nonce", firstAuthorization,
                "GET", "/api/v2/status", EMPTY_BODY_HASH
            )
        )

        clock.advance(2L)
        val nextTimestamp = clock.now().toString()
        val nextAuthorization = authorization(
            session.hmacSecret,
            "GET",
            "/api/v2/status",
            nextTimestamp,
            "boundary-nonce",
            EMPTY_BODY_HASH
        )

        assertFalse(
            manager.verifyRequest(
                "desktop-1", nextTimestamp, "boundary-nonce", nextAuthorization,
                "GET", "/api/v2/status", EMPTY_BODY_HASH
            )
        )
    }

    @Test
    fun verifyRequest_rejectsMalformedBase64Authorization() {
        val clock = MutableClock(900_000L)
        val manager = AuthManager(clock::now)
        requireNotNull(manager.pair(manager.currentPairingCode, "desktop-1"))

        val verified = manager.verifyRequest(
            "desktop-1", clock.now().toString(), "nonce-malformed", "HMAC not-base64!",
            "GET", "/api/v2/status", EMPTY_BODY_HASH
        )

        assertFalse(verified)
    }

    @Test
    fun verifyRequest_invalidHmacDoesNotConsumeNonce() {
        val clock = MutableClock(1_200_000L)
        val manager = AuthManager(clock::now)
        val session = requireNotNull(manager.pair(manager.currentPairingCode, "desktop-1"))
        val timestamp = clock.now().toString()
        val validAuthorization = authorization(
            session.hmacSecret,
            "GET",
            "/api/v2/status",
            timestamp,
            "nonce-retry",
            EMPTY_BODY_HASH
        )

        assertFalse(
            manager.verifyRequest(
                "desktop-1", timestamp, "nonce-retry", "HMAC ${Base64.getEncoder().encodeToString(ByteArray(32))}",
                "GET", "/api/v2/status", EMPTY_BODY_HASH
            )
        )
        assertTrue(
            manager.verifyRequest(
                "desktop-1", timestamp, "nonce-retry", validAuthorization,
                "GET", "/api/v2/status", EMPTY_BODY_HASH
            )
        )
    }

    @Test
    fun revokeAllSessions_clearsSessionsAndStableSecretFallbackIsRemoved() {
        val manager = AuthManager { 1_500_000L }
        requireNotNull(manager.pair(manager.currentPairingCode, "desktop-1"))
        requireNotNull(manager.pair(manager.currentPairingCode, "desktop-2"))

        val revoked = manager.revokeAllSessions()

        assertEquals(2, revoked)
        assertEquals(0, manager.sessionCount.value)
        assertNull(manager.currentSecretOrNull())
    }

    private fun authorization(
        secret: ByteArray,
        method: String,
        path: String,
        timestamp: String,
        nonce: String,
        bodyHash: String
    ): String {
        val normalizedPath = path.substringBefore('?')
        val input = "$method\n$normalizedPath\n$timestamp\n$nonce\n$bodyHash"
        val mac = Mac.getInstance("HmacSHA256")
        mac.init(SecretKeySpec(secret, "HmacSHA256"))
        return "HMAC ${Base64.getEncoder().encodeToString(mac.doFinal(input.toByteArray(Charsets.UTF_8)))}"
    }

    private class MutableClock(private var value: Long) {
        fun now(): Long = value
        fun advance(milliseconds: Long) {
            value += milliseconds
        }
    }

    private companion object {
        const val EMPTY_BODY_HASH = "47DEQpj8HBSa+/TImW+5JCeuQeRkm5NMpJWZG3hSuFU="
        fun days(value: Int): Long = value.toLong() * 24L * 60L * 60L * 1_000L
    }
}
