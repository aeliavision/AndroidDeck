package com.aeliavision.androiddeck.feature.server.service

import android.util.Log
import com.aeliavision.androiddeck.feature.contacts.model.ErrorResponse
import io.ktor.http.HttpHeaders
import io.ktor.http.HttpStatusCode
import io.ktor.server.application.ApplicationCallPipeline
import io.ktor.server.application.BaseApplicationPlugin
import io.ktor.server.application.call
import io.ktor.server.request.path
import io.ktor.server.response.header
import io.ktor.server.response.respond
import io.ktor.util.AttributeKey
import java.util.concurrent.ConcurrentHashMap
import java.util.concurrent.atomic.AtomicInteger

/** Request and concurrent-transfer limits for the local companion server. */
class RateLimiter(
    private val config: Config,
    private val nowProvider: () -> Long = System::currentTimeMillis
) {
    companion object Plugin : BaseApplicationPlugin<ApplicationCallPipeline, Config, RateLimiter> {
        private const val TAG = "RateLimiter"
        override val key = AttributeKey<RateLimiter>("RateLimiter")

        override fun install(pipeline: ApplicationCallPipeline, configure: Config.() -> Unit): RateLimiter {
            val config = Config().apply(configure)
            val plugin = RateLimiter(config)

            pipeline.intercept(ApplicationCallPipeline.Plugins) {
                val path = call.request.path()
                val normalizedClientId = call.request.headers["X-Client-Id"]
                    ?.trim()?.lowercase()?.takeIf { it.matches(Regex("[a-z0-9._:-]{1,128}")) }
                    ?: "anonymous"
                val remoteAddress = call.request.local.remoteHost
                val identity = "$remoteAddress|$normalizedClientId"
                val isExempt = path in config.exemptPaths
                val isPairing = path in config.pairingPaths

                if (!isExempt && !plugin.allowRequest(identity)) {
                    call.response.header(HttpHeaders.RetryAfter, plugin.retryAfterSeconds())
                    call.respond(HttpStatusCode.TooManyRequests,
                        ErrorResponse("rate_limit_exceeded", "Too many requests. Retry later."))
                    finish(); return@intercept
                }
                if (isPairing && !plugin.allowPairingRequest(identity)) {
                    Log.w(TAG, "Pairing rate limit exceeded")
                    call.response.header(HttpHeaders.RetryAfter, plugin.retryAfterSeconds())
                    call.respond(HttpStatusCode.TooManyRequests,
                        ErrorResponse("pairing_rate_limit_exceeded", "Too many pairing attempts. Retry later."))
                    finish(); return@intercept
                }

                val isTransfer = config.transferPathPrefixes.any { path.startsWith(it) }
                if (isTransfer && !plugin.acquireTransferSlot(identity)) {
                    call.response.header(HttpHeaders.RetryAfter, "1")
                    call.respond(HttpStatusCode.TooManyRequests,
                        ErrorResponse("concurrent_transfer_limit", "Too many concurrent transfers."))
                    finish(); return@intercept
                }
                try { proceed() } finally { if (isTransfer) plugin.releaseTransferSlot(identity) }
            }
            return plugin
        }
    }

    class Config {
        var maxRequestsPerWindow: Int = 600
        var windowMs: Long = 60_000L
        var maxConcurrentTransfers: Int = 3
        var pairingMaxRequestsPerWindow: Int = 6
        val pairingPaths: Set<String> = setOf("/api/v2/pair", "/api/v3/pair")
        val exemptPaths: Set<String> = setOf("/api/meta", "/api/v1/status", "/api/v2/status")
        val transferPathPrefixes: Set<String> = setOf(
            "/api/v2/files/", "/api/v2/gallery/", "/api/v2/backup/", "/api/v2/restore/"
        )
    }

    private data class WindowState(val windowStart: Long, val count: AtomicInteger)
    private val windows = ConcurrentHashMap<String, WindowState>()
    private val activeTransfers = ConcurrentHashMap<String, AtomicInteger>()

    fun allowRequest(clientId: String): Boolean = checkWindow("req:$clientId", config.maxRequestsPerWindow)
    fun allowPairingRequest(clientId: String): Boolean = checkWindow("pair:$clientId", config.pairingMaxRequestsPerWindow)
    fun retryAfterSeconds(): String = ((config.windowMs + 999) / 1000).coerceAtLeast(1).toString()

    private fun checkWindow(key: String, limit: Int): Boolean {
        val now = nowProvider()
        cleanupExpiredWindows(now)
        val state = windows.compute(key) { _, existing ->
            if (existing == null || now - existing.windowStart >= config.windowMs)
                WindowState(now, AtomicInteger(1))
            else { existing.count.incrementAndGet(); existing }
        }!!
        return state.count.get() <= limit
    }

    private fun cleanupExpiredWindows(now: Long) {
        windows.entries.removeIf { now - it.value.windowStart >= config.windowMs * 2 }
    }

    fun acquireTransferSlot(clientId: String): Boolean {
        val counter = activeTransfers.computeIfAbsent(clientId) { AtomicInteger() }
        while (true) {
            val current = counter.get()
            if (current >= config.maxConcurrentTransfers) return false
            if (counter.compareAndSet(current, current + 1)) return true
        }
    }

    fun releaseTransferSlot(clientId: String) {
        activeTransfers.computeIfPresent(clientId) { _, counter ->
            val remaining = counter.updateAndGet { (it - 1).coerceAtLeast(0) }
            if (remaining == 0) null else counter
        }
    }

    internal fun trackedWindowCount(): Int = windows.size
    internal fun activeTransferIdentityCount(): Int = activeTransfers.size
}
