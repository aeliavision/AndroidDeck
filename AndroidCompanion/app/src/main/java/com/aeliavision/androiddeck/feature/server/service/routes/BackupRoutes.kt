package com.aeliavision.androiddeck.feature.server.service.routes

import com.aeliavision.androiddeck.feature.backup.data.BackupManager
import com.aeliavision.androiddeck.feature.backup.data.BackupKeyStore
import com.aeliavision.androiddeck.feature.backup.data.RestoreManager
import com.aeliavision.androiddeck.feature.backup.model.BackupCreateRequest
import com.aeliavision.androiddeck.feature.backup.model.BackupCreateResponse
import com.aeliavision.androiddeck.feature.backup.model.BackupHistoryEntry
import com.aeliavision.androiddeck.feature.backup.model.BackupHistoryResponse
import com.aeliavision.androiddeck.feature.backup.model.BackupStatusResponse
import com.aeliavision.androiddeck.feature.backup.model.RestoreStartResponse
import com.aeliavision.androiddeck.feature.backup.model.RestoreStatusResponse
import com.aeliavision.androiddeck.feature.contacts.model.ContactsBatchRequest
import com.aeliavision.androiddeck.feature.contacts.model.ContactDto
import com.aeliavision.androiddeck.feature.contacts.model.ErrorResponse
import io.ktor.http.ContentType
import io.ktor.http.HttpStatusCode
import io.ktor.server.application.ApplicationCall
import io.ktor.server.request.receive
import io.ktor.server.request.receiveMultipart
import io.ktor.server.response.header
import io.ktor.server.response.respond
import io.ktor.server.response.respondOutputStream
import io.ktor.server.routing.Route
import io.ktor.server.routing.get
import io.ktor.server.routing.post
import io.ktor.server.routing.route
import io.ktor.utils.io.jvm.javaio.toInputStream

internal fun Route.installBackupRoutes(
    withAuth: WithAuth,
    requireBackup: suspend (ApplicationCall) -> BackupManager?,
    requireRestore: suspend (ApplicationCall) -> RestoreManager?,
    backupKeyStore: BackupKeyStore,
    contactsRepository: com.aeliavision.androiddeck.feature.contacts.data.ContactsRepository
) {
    route("/backup") {
        post("/create") {
            withAuth(call) {
                val bm = requireBackup(call) ?: return@withAuth
                val req = call.receive<BackupCreateRequest>()
                val id = bm.startBackup(req.types, req.paths, req.encrypt, req.incremental, req.sinceMs)
                val st = bm.getState(id)
                call.respond(
                    HttpStatusCode.Accepted,
                    BackupCreateResponse(
                        backupId = id,
                        estimatedItemCount = st?.itemCount ?: 0,
                        status = "started"
                    )
                )
            }
        }

        get("/{backupId}/status") {
            withAuth(call) {
                val bm = requireBackup(call) ?: return@withAuth
                val id = call.parameters["backupId"] ?: return@withAuth call.respond(
                    HttpStatusCode.BadRequest,
                    ErrorResponse(error = "missing_param", message = "missing backupId")
                )
                val s = bm.getState(id) ?: return@withAuth call.respond(
                    HttpStatusCode.NotFound,
                    ErrorResponse(error = "not_found", message = "backup not found")
                )
                call.respond(
                    BackupStatusResponse(
                        backupId = id,
                        progress = s.progress,
                        phase = s.phase.name.lowercase(),
                        currentItem = s.currentItem,
                        itemCount = s.itemCount,
                        processedItems = s.processedItems,
                        archiveSize = s.archiveSize,
                        error = s.error
                    )
                )
            }
        }

        get("/{backupId}/download") {
            withAuth(call) {
                val bm = requireBackup(call) ?: return@withAuth
                val id = call.parameters["backupId"] ?: return@withAuth call.respond(
                    HttpStatusCode.BadRequest,
                    ErrorResponse(error = "missing_param", message = "missing backupId")
                )
                val (stream, size) = bm.openArchiveStream(id)
                call.response.header("Content-Length", size.toString())
                call.respondOutputStream(
                    contentType = ContentType.Application.OctetStream,
                    status = HttpStatusCode.OK
                ) {
                    stream.use { it.copyTo(this, bufferSize = 512 * 1024) }
                }
            }
        }

        post("/restore") {
            withAuth(call) {
                val rm = requireRestore(call) ?: return@withAuth

                val multipart = call.receiveMultipart()
                var restoreId: String? = null
                var seedId: String? = null
                var seedBase64: String? = null

                while (true) {
                    val part = multipart.readPart() ?: break
                    try {
                        if (part is io.ktor.http.content.PartData.FormItem) {
                            when (part.name) {
                                "seedId" -> seedId = part.value
                                "seed" -> seedBase64 = part.value
                            }
                        }

                        if (part is io.ktor.http.content.PartData.FileItem && part.name == "archive") {
                            part.provider().toInputStream().use { stream ->
                                restoreId = rm.startRestore(stream, seedId, seedBase64)
                            }
                            break
                        }
                    } finally {
                        part.release()
                    }
                }

                val id = restoreId ?: return@withAuth call.respond(
                    HttpStatusCode.BadRequest,
                    ErrorResponse(error = "missing_file", message = "missing archive file")
                )

                call.respond(HttpStatusCode.Accepted, RestoreStartResponse(restoreId = id))
            }
        }

        get("/seed/{seedId}") {
            withAuth(call) {
                val seedId = call.parameters["seedId"] ?: return@withAuth call.respond(
                    HttpStatusCode.BadRequest,
                    ErrorResponse(error = "missing_param", message = "missing seedId")
                )

                val seed = try {
                    backupKeyStore.getBackupSeedById(seedId)
                } catch (e: Exception) {
                    return@withAuth call.respond(
                        HttpStatusCode.NotFound,
                        ErrorResponse(error = "not_found", message = e.message ?: "seed not found")
                    )
                }

                val seedB64 = android.util.Base64.encodeToString(seed, android.util.Base64.NO_WRAP)
                call.respond(mapOf("seedId" to seedId, "seed" to seedB64))
            }
        }

        get("/restore/{restoreId}/status") {
            withAuth(call) {
                val rm = requireRestore(call) ?: return@withAuth
                val id = call.parameters["restoreId"] ?: return@withAuth call.respond(
                    HttpStatusCode.BadRequest,
                    ErrorResponse(error = "missing_param", message = "missing restoreId")
                )
                val s = rm.getState(id) ?: return@withAuth call.respond(
                    HttpStatusCode.NotFound,
                    ErrorResponse(error = "not_found", message = "restore not found")
                )
                call.respond(
                    RestoreStatusResponse(
                        restoreId = id,
                        progress = s.progress,
                        phase = s.phase.name.lowercase(),
                        restoredItems = s.restoredItems,
                        failedItems = s.failedItems,
                        skippedItems = s.skippedItems,
                        error = s.error
                    )
                )
            }
        }

        get("/history") {
            withAuth(call) {
                val bm = requireBackup(call) ?: return@withAuth
                val backups = bm.getHistory().map { h ->
                    BackupHistoryEntry(
                        backupId = h.backupId,
                        createdAt = h.createdAt,
                        types = h.types,
                        archiveSize = h.archiveSize,
                        itemCount = h.itemCount
                    )
                }
                call.respond(BackupHistoryResponse(backups = backups))
            }
        }
    }

    post("/contacts/batch") {
        withAuth(call) {
            val req = call.receive<ContactsBatchRequest>()
            val ids = req.ids
            if (ids.isEmpty()) {
                call.respond(emptyList<ContactDto>())
                return@withAuth
            }
            val results = contactsRepository.getContactDetails(ids)
            call.respond(results)
        }
    }
}
