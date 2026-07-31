package com.aeliavision.androiddeck.feature.server.service

import android.util.Log
import com.aeliavision.androiddeck.feature.contacts.data.ContactsRepository
import com.aeliavision.androiddeck.feature.contacts.data.ReadOnlyAccountException
import com.aeliavision.androiddeck.feature.contacts.model.ContactDto
import com.aeliavision.androiddeck.feature.contacts.model.ErrorResponse
import com.aeliavision.androiddeck.feature.contacts.model.PairRequestV3
import com.aeliavision.androiddeck.feature.contacts.model.PairResponseV3
import com.aeliavision.androiddeck.feature.contacts.model.StatusResponse
import com.aeliavision.androiddeck.feature.contacts.model.ValidationResult
import com.aeliavision.androiddeck.feature.contacts.model.validate
import com.aeliavision.androiddeck.feature.filesystem.data.FileSystemRepository
import com.aeliavision.androiddeck.feature.filesystem.data.StreamTransferManager
import com.aeliavision.androiddeck.feature.gallery.data.GalleryRepository
import com.aeliavision.androiddeck.feature.backup.data.BackupManager
import com.aeliavision.androiddeck.feature.backup.data.BackupKeyStore
import com.aeliavision.androiddeck.feature.backup.data.RestoreManager
import io.ktor.http.*
import io.ktor.server.application.*
import io.ktor.server.engine.*
import io.ktor.server.netty.*
import io.ktor.server.plugins.compression.*
import io.ktor.server.plugins.contentnegotiation.*
import io.ktor.server.plugins.doublereceive.*
import io.ktor.server.plugins.statuspages.*
import io.ktor.server.request.*
import io.ktor.server.response.*
import io.ktor.server.routing.*
import io.ktor.serialization.gson.*
import io.ktor.utils.io.readRemaining
import android.os.Environment
import java.security.KeyStore
import java.security.MessageDigest
import java.util.Base64
import java.util.UUID
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.SupervisorJob
import kotlin.coroutines.CoroutineContext
import com.aeliavision.androiddeck.feature.server.service.routes.installBackupRoutes
import com.aeliavision.androiddeck.feature.server.service.routes.installFileSystemRoutes
import com.aeliavision.androiddeck.feature.server.service.routes.installGalleryRoutes
import kotlinx.io.readByteArray

/**
 * Coroutine-native - no blocking thread-per-request model, no ANR risk.
 */
class ContactsKtorServer(
    private val port: Int,
    private val repository: ContactsRepository,
    private val authManager: AuthManager,
    private val deviceName: String,
    private val keyStore: KeyStore,
    private val keyStorePassword: CharArray,
    private val fileSystemRepository: FileSystemRepository? = null,
    private val streamTransferManager: StreamTransferManager? = null,
    private val galleryRepository: GalleryRepository? = null,
    private val requiresAllFilesAccess: Boolean = false,
    private val requiresMediaPermissions: Boolean = false,
    private val supportsBackup: Boolean = false,
    private val backupManager: BackupManager? = null,
    private val restoreManager: RestoreManager? = null,
    private val backupKeyStore: BackupKeyStore,
    private val allowLegacyPairing: Boolean = false
) : CoroutineScope {

    companion object {
        private const val TAG = "ContactsKtorServer"
    }

    @Volatile
    private var job = SupervisorJob()

    private val lifecycleLock = Any()

    override val coroutineContext: CoroutineContext
        get() = job + Dispatchers.IO

    private var engine: EmbeddedServer<NettyApplicationEngine, NettyApplicationEngine.Configuration>? = null

    val isRunning: Boolean get() = engine != null

    fun start() {
        synchronized(lifecycleLock) {
            if (!job.isActive) job = SupervisorJob()
        }

        engine = embeddedServer(
            factory = Netty,
            host = "0.0.0.0",
            port = this@ContactsKtorServer.port
        ) {
            install(ContentNegotiation) {
                gson { setPrettyPrinting() }
            }
            install(Compression) {
                gzip {
                    priority = 1.0
                    minimumSize(1024)
                }
            }
            install(DoubleReceive)
            install(RateLimiter) {
                maxRequestsPerWindow = 600
                windowMs = 60_000
                pairingMaxRequestsPerWindow = 6
                maxConcurrentTransfers = 3
            }
            install(StatusPages) {
                exception<Throwable> { call, cause ->
                    val correlationId = UUID.randomUUID().toString()
                    Log.e(TAG, "Unhandled server error correlationId=$correlationId", cause)
                    call.respond(
                        HttpStatusCode.InternalServerError,
                        ErrorResponse(error = "internal_error", message = "Request failed. Reference: $correlationId")
                    )
                }
            }
            routing {
                get("/api/meta") {
                    call.respond(mapOf(
                        "pairingProtocolVersion" to 3,
                        "legacyPairingEnabled" to allowLegacyPairing
                    ))
                }

                route("/api/v3") {
                    post("/pair") {
                        val request = runCatching { call.receive<PairRequestV3>() }.getOrNull()
                            ?: return@post call.respond(
                                HttpStatusCode.BadRequest,
                                ErrorResponse("invalid_request", "Invalid pairing request.")
                            )
                        if (request.clientId.isBlank() || request.clientPublicKey.isBlank()) {
                            return@post call.respond(
                                HttpStatusCode.BadRequest,
                                ErrorResponse("missing_field", "clientId and clientPublicKey are required.")
                            )
                        }
                        val session = authManager.pair(request.pairingCode, request.clientId)
                            ?: return@post call.respond(
                                HttpStatusCode.Unauthorized,
                                ErrorResponse("invalid_code", "Invalid or expired pairing code.")
                            )
                        val ecdhResult = try {
                            EcdhHelper.deriveSharedSecret(
                                clientPublicKeyBase64 = request.clientPublicKey,
                                salt = session.sessionId.toByteArray(Charsets.UTF_8),
                                info = "AndroidDeck pairing v3".toByteArray(Charsets.UTF_8)
                            )
                        } catch (e: Exception) {
                            authManager.revokeSession(request.clientId)
                            Log.w(TAG, "Rejected malformed v3 public key", e)
                            return@post call.respond(
                                HttpStatusCode.BadRequest,
                                ErrorResponse("invalid_public_key", "The client public key is invalid.")
                            )
                        }
                        authManager.replaceSecret(request.clientId, ecdhResult.derivedSecret)
                        ecdhResult.derivedSecret.fill(0)
                        val certFingerprint = certificateFingerprint() ?: ""
                        call.respond(
                            PairResponseV3(
                                sessionId = session.sessionId,
                                expiresAt = session.expiresAt,
                                serverPublicKey = ecdhResult.serverPublicKeyBase64,
                                certFingerprint = certFingerprint
                            )
                        )
                    }
                }

                route("/api/v1") {

                    // Status
                    get("/status") {
                        call.withAuth {
                            call.respond(
                                StatusResponse(
                                    deviceName = deviceName,
                                    supportsFiles = fileSystemRepository != null,
                                    supportsGallery = galleryRepository != null,
                                    supportsBackup = supportsBackup,
                                    requiresAllFilesAccess = requiresAllFilesAccess,
                                    requiresMediaPermissions = requiresMediaPermissions,
                                    pairingProtocolVersion = 3,
                                    legacyPairingEnabled = allowLegacyPairing
                                )
                            )
                        }
                    }

                    // Contacts - read
                    get("/contacts") {
                        call.withAuth {
                            val page     = call.request.queryParameters["page"]?.toIntOrNull() ?: 1
                            val pageSize = call.request.queryParameters["pageSize"]?.toIntOrNull() ?: 50
                            val query    = call.request.queryParameters["query"]
                            val items    = repository.getContacts(page, pageSize, query)
                            val nextPage = if (items.size >= pageSize) page + 1 else null
                            call.respond(
                                com.aeliavision.androiddeck.feature.contacts.model.ContactsPage(
                                    items    = items,
                                    nextPage = nextPage
                                )
                            )
                        }
                    }

                    get("/contacts/export.vcf") {
                        call.withAuth {
                            call.respondOutputStream(
                                contentType = ContentType.parse("text/vcard; charset=utf-8")
                            ) {
                                repository.exportVcfTo(this, contactIds = null)
                            }
                        }
                    }

                    get("/contacts/{id}") {
                        call.withAuth {
                            val id = call.parameters["id"] ?: return@withAuth
                            val contact = repository.getContactDetail(id)
                            if (contact == null)
                                call.respond(HttpStatusCode.NotFound, ErrorResponse(error = "not_found"))
                            else
                                call.respond(contact)
                        }
                    }

                    get("/contacts/{id}/photo") {
                        call.withAuth {
                            val id = call.parameters["id"] ?: return@withAuth
                            val photo = repository.getContactPhoto(id)
                            if (photo == null)
                                call.respond(
                                    HttpStatusCode.NotFound,
                                    ErrorResponse(error = "not_found", message = "contact photo not found")
                                )
                            else
                                call.respondBytes(photo, ContentType.Image.JPEG)
                        }
                    }

                    // Contacts - write
                    post("/contacts") {
                        call.withAuth {
                            val dto = call.receive<ContactDto>()
                            val validation = dto.validate()
                            if (validation is ValidationResult.Invalid) {
                                call.respond(HttpStatusCode.BadRequest,
                                    ErrorResponse(error = "validation_error",
                                        message = validation.errors.joinToString("; ")))
                                return@withAuth
                            }
                            try {
                                val created = repository.createContact(dto)
                                call.respond(HttpStatusCode.Created, created)
                            } catch (e: ReadOnlyAccountException) {
                                call.respond(HttpStatusCode.Forbidden,
                                    ErrorResponse(error = "read_only", message = e.message))
                            }
                        }
                    }

                    put("/contacts/{id}") {
                        call.withAuth {
                            val id = call.parameters["id"] ?: return@withAuth
                            val dto = call.receive<ContactDto>().copy(id = id)
                            val validation = dto.validate()
                            if (validation is ValidationResult.Invalid) {
                                call.respond(HttpStatusCode.BadRequest,
                                    ErrorResponse(error = "validation_error",
                                        message = validation.errors.joinToString("; ")))
                                return@withAuth
                            }
                            try {
                                val updated = repository.updateContact(dto)
                                call.respond(updated)
                            } catch (e: ReadOnlyAccountException) {
                                call.respond(HttpStatusCode.Forbidden,
                                    ErrorResponse(error = "read_only", message = e.message))
                            }
                        }
                    }

                    delete("/contacts/{id}") {
                        call.withAuth {
                            val id = call.parameters["id"] ?: return@withAuth
                            try {
                                val deleted = repository.deleteContact(id)
                                if (deleted)
                                    call.respond(HttpStatusCode.NoContent)
                                else
                                    call.respond(HttpStatusCode.NotFound, ErrorResponse(error = "not_found"))
                            } catch (e: ReadOnlyAccountException) {
                                call.respond(HttpStatusCode.Forbidden,
                                    ErrorResponse(error = "read_only", message = e.message))
                            }
                        }
                    }

                    put("/contacts/{id}/photo") {
                        call.withAuth {
                            val id = call.parameters["id"] ?: return@withAuth
                            val contentType = call.request.contentType()
                            val photoBytes: ByteArray = if (contentType.match(ContentType.Application.Json)) {
                                val body = call.receive<Map<String, String>>()
                                val b64 = body["photo"] ?: return@withAuth call.respond(
                                    HttpStatusCode.BadRequest,
                                    ErrorResponse(error = "missing_field", message = "missing photo field")
                                )
                                Base64.getDecoder().decode(b64)
                            } else {
                                call.receiveChannel().readRemaining().readByteArray()
                            }
                            try {
                                val ok = repository.setContactPhoto(id, photoBytes)
                                if (ok)
                                    call.respond(HttpStatusCode.NoContent)
                                else
                                    call.respond(HttpStatusCode.NotFound, ErrorResponse(error = "not_found"))
                            } catch (e: ReadOnlyAccountException) {
                                call.respond(HttpStatusCode.Forbidden,
                                    ErrorResponse(error = "read_only", message = e.message))
                            }
                        }
                    }

                    // Groups
                    get("/groups") {
                        call.withAuth {
                            call.respond(
                                com.aeliavision.androiddeck.feature.contacts.model.GroupsPage(
                                    items = repository.getGroups()
                                )
                            )
                        }
                    }

                    get("/groups/{groupId}/contacts") {
                        call.withAuth {
                            val groupId = call.parameters["groupId"] ?: return@withAuth
                            call.respond(repository.getContactsByGroup(groupId))
                        }
                    }
                }

                // API v2
                route("/api/v2") {

                    // ECDH-enhanced pairing (v2)
                    post("/pair") {
                        if (!allowLegacyPairing) {
                            return@post call.respond(
                                HttpStatusCode.Gone,
                                ErrorResponse("legacy_pairing_disabled", "Use pairing protocol v3.")
                            )
                        }
                        val body = call.receive<Map<String, String>>()
                        val code = body["pairingCode"] ?: return@post call.respond(
                            HttpStatusCode.BadRequest,
                            ErrorResponse(error = "missing_field", message = "missing pairingCode")
                        )
                        val clientId = body["clientId"] ?: return@post call.respond(
                            HttpStatusCode.BadRequest,
                            ErrorResponse(error = "missing_field", message = "missing clientId")
                        )
                        val clientPublicKey = body["clientPublicKey"]

                        val session = authManager.pair(code, clientId) ?: return@post call.respond(
                            HttpStatusCode.Unauthorized,
                            ErrorResponse(error = "invalid_code", message = "invalid or expired pairing code")
                        )

                        val (finalSecret, serverPublicKey) = if (clientPublicKey != null) {
                            try {
                                val ecdhResult = EcdhHelper.deriveSharedSecret(
                                    clientPublicKeyBase64 = clientPublicKey,
                                    salt = session.sessionId.toByteArray(Charsets.UTF_8)
                                )
                                authManager.replaceSecret(clientId, ecdhResult.derivedSecret)
                                Pair(ecdhResult.derivedSecret, ecdhResult.serverPublicKeyBase64)
                            } catch (e: Exception) {
                                Log.w(TAG, "ECDH key exchange failed - falling back to random secret: ${e.message}")
                                Pair(session.hmacSecret, null)
                            }
                        } else {
                            Pair(session.hmacSecret, null)
                        }

                        val certFingerprint = keyStore.getCertificate(TlsHelper.KEY_ALIAS)?.let { cert ->
                            val digest = MessageDigest.getInstance("SHA-256").digest(cert.encoded)
                            digest.joinToString(":") { b -> "%02X".format(b) }
                        }

                        call.respond(
                            com.aeliavision.androiddeck.feature.contacts.model.PairResponseV2(
                                sessionId = session.sessionId,
                                hmacSecret = authManager.encodeSecret(finalSecret),
                                expiresAt = session.expiresAt,
                                serverPublicKey = serverPublicKey,
                                certFingerprint = certFingerprint
                            )
                        )
                    }

                    // Status (v2)
                    get("/status") {
                        call.withAuth {
                            call.respond(
                                StatusResponse(
                                    deviceName = deviceName,
                                    supportsFiles = fileSystemRepository != null,
                                    supportsGallery = galleryRepository != null,
                                    supportsBackup = supportsBackup,
                                    requiresAllFilesAccess = requiresAllFilesAccess,
                                    requiresMediaPermissions = requiresMediaPermissions,
                                    pairingProtocolVersion = 3,
                                    legacyPairingEnabled = allowLegacyPairing
                                )
                            )
                        }
                    }

                    // File System API
                    suspend fun fileRepo(c: ApplicationCall): FileSystemRepository? {
                        if (fileSystemRepository == null) {
                            c.respond(HttpStatusCode.ServiceUnavailable,
                                ErrorResponse(error = "permission_required",
                                    message = "File system access not available. Grant MANAGE_EXTERNAL_STORAGE permission in the companion app."))
                            return null
                        }
                        return fileSystemRepository
                    }

                    suspend fun stm(c: ApplicationCall): StreamTransferManager? {
                        if (streamTransferManager == null) {
                            c.respond(HttpStatusCode.ServiceUnavailable,
                                ErrorResponse(error = "permission_required", message = "Stream transfer not available."))
                            return null
                        }
                        return streamTransferManager
                    }

                    suspend fun galleryRepo(c: ApplicationCall): GalleryRepository? {
                        if (galleryRepository == null) {
                            c.respond(HttpStatusCode.ServiceUnavailable,
                                ErrorResponse(error = "permission_required",
                                    message = "Gallery access not available. Grant READ_MEDIA_IMAGES permission in the companion app."))
                            return null
                        }
                        return galleryRepository
                    }

                    val withAuthFn: com.aeliavision.androiddeck.feature.server.service.routes.WithAuth = { c, block ->
                        c.withAuth { block() }
                    }

                    installFileSystemRoutes(
                        withAuth = withAuthFn,
                        fileRepo = ::fileRepo,
                        stm = ::stm,
                        fsRepo = ::fileRepo
                    )

                    installGalleryRoutes(
                        withAuth = withAuthFn,
                        galleryRepo = ::galleryRepo
                    )

                    // Backup & Restore API
                    suspend fun requireBackup(c: ApplicationCall): BackupManager? {
                        if (backupManager == null) {
                            c.respond(HttpStatusCode.ServiceUnavailable,
                                ErrorResponse(error = "feature_disabled", message = "Backup feature not available."))
                            return null
                        }
                        return backupManager
                    }

                    suspend fun requireRestore(c: ApplicationCall): RestoreManager? {
                        if (restoreManager == null) {
                            c.respond(HttpStatusCode.ServiceUnavailable,
                                ErrorResponse(error = "feature_disabled", message = "Restore feature not available."))
                            return null
                        }
                        return restoreManager
                    }

                    installBackupRoutes(
                        withAuth = withAuthFn,
                        requireBackup = ::requireBackup,
                        requireRestore = ::requireRestore,
                        backupKeyStore = backupKeyStore,
                        contactsRepository = repository
                    )
                }

                // API v2.1
                route("/api/v2.1") {
                    route("/backup") {
                        get("/manifest") {
                            call.withAuth {
                                val defaults = listOf(
                                    Environment.getExternalStoragePublicDirectory(Environment.DIRECTORY_DOWNLOADS).canonicalPath,
                                    Environment.getExternalStoragePublicDirectory(Environment.DIRECTORY_DOCUMENTS).canonicalPath
                                ).distinct()
                                call.respond(
                                    com.aeliavision.androiddeck.feature.backup.model.BackupManifestResponse(
                                        defaultPaths = defaults
                                    )
                                )
                            }
                        }
                    }
                }
            }
        }

        engine!!.start(wait = false)
        Log.i(TAG, "Ktor Netty HTTP server started on port $port")
    }

    fun stop() {
        synchronized(lifecycleLock) {
            engine?.stop(gracePeriodMillis = 500, timeoutMillis = 1000)
            engine = null
            job.cancel()
            job = SupervisorJob()
            Log.i(TAG, "Ktor Netty server stopped")
        }
    }

    private suspend fun ApplicationCall.withAuth(block: suspend () -> Unit) {
        val clientId  = request.headers["X-Client-Id"]
        val timestamp = request.headers["X-Timestamp"]
        val nonce     = request.headers["X-Nonce"]
        val auth      = request.headers["Authorization"]
        val contentLength = request.contentLength() ?: 0L
        val maximum = maximumRequestBytes(request.path(), request.contentType())
        if (contentLength > maximum) {
            respond(HttpStatusCode.PayloadTooLarge, ErrorResponse("request_too_large", "Request body exceeds the allowed size."))
            return
        }
        val declaredDigest = request.headers["X-Content-SHA256"]
        val bodyHash = if (contentLength > 0L) {
            if (declaredDigest.isNullOrBlank()) {
                respond(HttpStatusCode.BadRequest, ErrorResponse("missing_content_digest", "X-Content-SHA256 is required."))
                return
            }
            val computed = digestRequestBody(maximum)
            if (!MessageDigest.isEqual(
                    Base64.getDecoder().decode(computed),
                    runCatching { Base64.getDecoder().decode(declaredDigest) }.getOrNull() ?: byteArrayOf()
                )) {
                respond(HttpStatusCode.BadRequest, ErrorResponse("content_digest_mismatch", "Request body digest does not match."))
                return
            }
            computed
        } else emptyBodyHash

        if (!authManager.verifyRequest(clientId, timestamp, nonce, auth,
                request.httpMethod.value, request.path(), bodyHash)) {
            respond(HttpStatusCode.Unauthorized, ErrorResponse(error = "unauthorized"))
            return
        }
        block()
    }

    private fun maximumRequestBytes(path: String, contentType: ContentType): Long = when {
        path.contains("/backup/") || path.contains("/restore") -> 1_073_741_824L
        path.contains("/files/upload") || path.contains("/stream/") -> 536_870_912L
        path.contains("/contacts/import") || contentType.match(ContentType.Text.Any) -> 33_554_432L
        else -> 2_097_152L
    }

    private suspend fun ApplicationCall.digestRequestBody(maximum: Long): String {
        val digest = MessageDigest.getInstance("SHA-256")
        val channel = receiveChannel()
        val buffer = ByteArray(64 * 1024)
        var total = 0L
        while (!channel.isClosedForRead) {
            val packet = channel.readRemaining(buffer.size.toLong())
            val bytes = packet.readByteArray()
            if (bytes.isEmpty()) break
            total += bytes.size
            if (total > maximum) throw IllegalArgumentException("request_too_large")
            digest.update(bytes)
        }
        return Base64.getEncoder().encodeToString(digest.digest())
    }

    private fun certificateFingerprint(): String? =
        keyStore.getCertificate(TlsHelper.KEY_ALIAS)?.let { cert ->
            MessageDigest.getInstance("SHA-256").digest(cert.encoded)
                .joinToString(":") { b -> "%02X".format(b) }
        }

    private fun sha256Base64(data: ByteArray): String =
        Base64.getEncoder().encodeToString(
            MessageDigest.getInstance("SHA-256").digest(data))

    private val emptyBodyHash: String = sha256Base64(byteArrayOf())
}
