package com.aeliavision.androiddeck.feature.server.service

import java.security.MessageDigest
import java.security.SecureRandom
import java.util.Base64
import java.util.UUID
import java.util.concurrent.locks.ReentrantReadWriteLock
import javax.crypto.Mac
import javax.crypto.spec.SecretKeySpec
import javax.inject.Inject
import javax.inject.Singleton
import kotlin.concurrent.write
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow

/**
 * Manages short-lived pairing codes, bounded authenticated sessions, and replay-safe
 * HMAC request verification for the local companion server.
 */
@Singleton
class AuthManager @Inject constructor() {

    companion object {
        const val PAIRING_CODE_LENGTH: Int = 6
        const val HMAC_SECRET_BYTES: Int = 32
        const val MAX_TIMESTAMP_DRIFT_MS: Long = 5L * 60L * 1_000L
        const val NONCE_WINDOW_MS: Long = 5L * 60L * 1_000L
        const val SESSION_TTL_MS: Long = 30L * 24L * 60L * 60L * 1_000L
        const val MAX_SESSIONS: Int = 10
        const val MAX_NONCE_LENGTH: Int = 256

        fun computeSha256(input: String): String {
            val digest = MessageDigest.getInstance("SHA-256")
            val hash = digest.digest(input.toByteArray(Charsets.UTF_8))
            return Base64.getEncoder().encodeToString(hash)
        }
    }

    private val random: SecureRandom = SecureRandom()
    private var nowProvider: () -> Long = System::currentTimeMillis

    /** Test seam for deterministic expiry and nonce-window tests. */
    internal constructor(nowProvider: () -> Long) : this() {
        this.nowProvider = nowProvider
    }

    private val pairingLock: Any = Any()

    @Volatile
    var currentPairingCode: String = generatePairingCode()
        private set

    private val sessionLock: ReentrantReadWriteLock = ReentrantReadWriteLock()
    private val sessions: HashMap<String, SessionInfo> = HashMap()
    private val _sessionCount: MutableStateFlow<Int> = MutableStateFlow(0)

    /** Live count of non-expired sessions after the latest auth/session operation. */
    val sessionCount: StateFlow<Int> = _sessionCount.asStateFlow()

    private val nonceLock: Any = Any()
    private var activeBucketIndex: Long = -1L
    private val nonceBuckets: Array<HashSet<String>> = arrayOf(HashSet(), HashSet())

    data class SessionInfo(
        val sessionId: String,
        val hmacSecret: ByteArray,
        val createdAt: Long,
        val expiresAt: Long,
        val lastSeenAt: Long
    )

    data class SessionSummary(
        val clientId: String,
        val sessionId: String,
        val createdAt: Long,
        val expiresAt: Long,
        val lastSeenAt: Long
    )

    fun getSessionsList(): List<SessionSummary> = sessionLock.write {
        removeExpiredSessionsLocked(nowProvider())
        sessions.map { (clientId, info) ->
            SessionSummary(
                clientId = clientId,
                sessionId = info.sessionId,
                createdAt = info.createdAt,
                expiresAt = info.expiresAt,
                lastSeenAt = info.lastSeenAt
            )
        }
    }

    fun regeneratePairingCode(): String = synchronized(pairingLock) {
        rotatePairingCodeLocked()
    }

    fun pair(pairingCode: String, clientId: String): SessionInfo? = synchronized(pairingLock) {
        if (pairingCode != currentPairingCode || clientId.isBlank()) {
            return@synchronized null
        }

        val now = nowProvider()
        val storedSecret = ByteArray(HMAC_SECRET_BYTES).also(random::nextBytes)
        val storedSession = SessionInfo(
            sessionId = UUID.randomUUID().toString(),
            hmacSecret = storedSecret,
            createdAt = now,
            expiresAt = safeExpiry(now),
            lastSeenAt = now
        )

        sessionLock.write {
            removeExpiredSessionsLocked(now)

            sessions.remove(clientId)?.hmacSecret?.fill(0)
            if (sessions.size >= MAX_SESSIONS) {
                evictLeastRecentlyUsedSessionLocked()
            }

            sessions[clientId] = storedSession
            updateSessionCountLocked()
        }

        rotatePairingCodeLocked()
        storedSession.copy(hmacSecret = storedSecret.copyOf())
    }

    fun verifyRequest(
        clientId: String?,
        timestamp: String?,
        nonce: String?,
        authorization: String?,
        method: String,
        path: String,
        bodyHash: String
    ): Boolean {
        if (
            clientId.isNullOrBlank() ||
            timestamp.isNullOrBlank() ||
            nonce.isNullOrBlank() ||
            nonce.length > MAX_NONCE_LENGTH ||
            authorization.isNullOrBlank() ||
            !authorization.startsWith("HMAC ")
        ) {
            return false
        }

        val now = nowProvider()
        val parsedTimestamp = timestamp.toLongOrNull() ?: return false
        val earliestAllowed = if (now < Long.MIN_VALUE + MAX_TIMESTAMP_DRIFT_MS)
            Long.MIN_VALUE else now - MAX_TIMESTAMP_DRIFT_MS
        val latestAllowed = if (now > Long.MAX_VALUE - MAX_TIMESTAMP_DRIFT_MS)
            Long.MAX_VALUE else now + MAX_TIMESTAMP_DRIFT_MS
        if (parsedTimestamp !in earliestAllowed..latestAllowed) {
            return false
        }

        val session = getLiveSessionSnapshot(clientId, now) ?: return false
        val provided = runCatching {
            Base64.getDecoder().decode(authorization.removePrefix("HMAC "))
        }.getOrNull() ?: run {
            session.hmacSecret.fill(0)
            return false
        }

        val normalizedPath = path.substringBefore('?')
        val signatureInput = "$method\n$normalizedPath\n$timestamp\n$nonce\n$bodyHash"
        val computed = computeHmacBytes(session.hmacSecret, signatureInput)
        val signatureIsValid = try {
            MessageDigest.isEqual(computed, provided)
        } finally {
            computed.fill(0)
            provided.fill(0)
            session.hmacSecret.fill(0)
        }
        if (!signatureIsValid) {
            return false
        }

        val nonceKey = "$clientId:$nonce"
        val nonceIsFresh = synchronized(nonceLock) {
            val absoluteBucket = now / NONCE_WINDOW_MS
            val currentIndex = Math.floorMod(absoluteBucket, 2L).toInt()
            if (absoluteBucket != activeBucketIndex) {
                nonceBuckets[currentIndex].clear()
                activeBucketIndex = absoluteBucket
            }
            if (nonceBuckets.any { nonceKey in it }) {
                false
            } else {
                nonceBuckets[currentIndex].add(nonceKey)
                true
            }
        }
        if (!nonceIsFresh) {
            return false
        }

        return sessionLock.write {
            removeExpiredSessionsLocked(now)
            val current = sessions[clientId]
            if (current == null || current.sessionId != session.sessionId) {
                false
            } else {
                sessions[clientId] = current.copy(lastSeenAt = now)
                true
            }
        }
    }

    fun getActiveSessionCount(): Int = sessionLock.write {
        removeExpiredSessionsLocked(nowProvider())
        sessions.size
    }

    fun refreshSessionCount(): Int = getActiveSessionCount()

    fun revokeSession(clientId: String): Boolean = sessionLock.write {
        val removed = sessions.remove(clientId) ?: return@write false
        removed.hmacSecret.fill(0)
        updateSessionCountLocked()
        true
    }

    fun revokeAllSessions(): Int {
        val revoked = sessionLock.write {
            val count = sessions.size
            sessions.values.forEach { it.hmacSecret.fill(0) }
            sessions.clear()
            updateSessionCountLocked()
            count
        }
        synchronized(nonceLock) {
            nonceBuckets.forEach { it.clear() }
            activeBucketIndex = -1L
        }
        return revoked
    }

    fun encodeSecret(secret: ByteArray): String =
        Base64.getEncoder().encodeToString(secret)

    /** Returns a defensive copy of the most recently active live session secret. */
    fun currentSecretOrNull(): ByteArray? = sessionLock.write {
        removeExpiredSessionsLocked(nowProvider())
        sessions.values
            .maxWithOrNull(compareBy<SessionInfo> { it.lastSeenAt }.thenBy { it.createdAt })
            ?.hmacSecret
            ?.copyOf()
    }

    /** Replaces an existing live session secret with a defensive copy. */
    fun replaceSecret(clientId: String, newSecret: ByteArray) {
        require(newSecret.isNotEmpty()) { "Session secret cannot be empty" }
        val now = nowProvider()
        sessionLock.write {
            removeExpiredSessionsLocked(now)
            val existing = sessions[clientId] ?: return@write
            val replacement = newSecret.copyOf()
            existing.hmacSecret.fill(0)
            sessions[clientId] = existing.copy(hmacSecret = replacement)
        }
    }

    private fun getLiveSessionSnapshot(clientId: String, now: Long): SessionInfo? = sessionLock.write {
        removeExpiredSessionsLocked(now)
        sessions[clientId]?.let { it.copy(hmacSecret = it.hmacSecret.copyOf()) }
    }

    private fun removeExpiredSessionsLocked(now: Long) {
        var removedAny = false
        val iterator = sessions.entries.iterator()
        while (iterator.hasNext()) {
            val entry = iterator.next()
            if (entry.value.expiresAt <= now) {
                entry.value.hmacSecret.fill(0)
                iterator.remove()
                removedAny = true
            }
        }
        if (removedAny) {
            updateSessionCountLocked()
        }
    }

    private fun evictLeastRecentlyUsedSessionLocked() {
        val leastRecentlyUsed = sessions.entries.minWithOrNull(
            compareBy<Map.Entry<String, SessionInfo>> { it.value.lastSeenAt }
                .thenBy { it.value.createdAt }
                .thenBy { it.key }
        ) ?: return
        leastRecentlyUsed.value.hmacSecret.fill(0)
        sessions.remove(leastRecentlyUsed.key)
    }

    private fun updateSessionCountLocked() {
        _sessionCount.value = sessions.size
    }

    private fun rotatePairingCodeLocked(): String {
        currentPairingCode = generatePairingCode()
        return currentPairingCode
    }

    private fun generatePairingCode(): String =
        buildString(PAIRING_CODE_LENGTH) {
            repeat(PAIRING_CODE_LENGTH) {
                append(random.nextInt(10))
            }
        }

    private fun safeExpiry(createdAt: Long): Long =
        if (createdAt > Long.MAX_VALUE - SESSION_TTL_MS) Long.MAX_VALUE
        else createdAt + SESSION_TTL_MS

    private fun computeHmacBytes(secret: ByteArray, input: String): ByteArray {
        val mac = Mac.getInstance("HmacSHA256")
        mac.init(SecretKeySpec(secret, "HmacSHA256"))
        return mac.doFinal(input.toByteArray(Charsets.UTF_8))
    }
}
