package com.aeliavision.androiddeck.feature.server.service.routes

import com.aeliavision.androiddeck.feature.contacts.model.ErrorResponse
import com.aeliavision.androiddeck.feature.filesystem.data.FileSystemRepository
import com.aeliavision.androiddeck.feature.filesystem.data.StreamTransferManager
import com.aeliavision.androiddeck.feature.filesystem.model.ChunkAck
import com.aeliavision.androiddeck.feature.filesystem.model.DeleteResult
import com.aeliavision.androiddeck.feature.filesystem.model.DirectoryListing
import com.aeliavision.androiddeck.feature.filesystem.model.MkdirResult
import com.aeliavision.androiddeck.feature.filesystem.model.StreamCompleteResponse
import com.aeliavision.androiddeck.feature.filesystem.model.StreamInitRequest
import com.aeliavision.androiddeck.feature.filesystem.model.StreamInitResponse
import com.aeliavision.androiddeck.feature.filesystem.model.StreamStatusResponse
import com.aeliavision.androiddeck.feature.filesystem.model.UploadResult
import io.ktor.http.ContentType
import io.ktor.http.HttpHeaders
import io.ktor.http.HttpStatusCode
import io.ktor.server.application.ApplicationCall
import io.ktor.server.response.header
import io.ktor.server.response.respond
import io.ktor.server.response.respondFile
import io.ktor.server.response.respondOutputStream
import io.ktor.server.request.receive
import io.ktor.server.request.receiveMultipart
import io.ktor.server.routing.Route
import io.ktor.server.routing.delete
import io.ktor.server.routing.get
import io.ktor.server.routing.post
import io.ktor.server.routing.put
import io.ktor.server.routing.route
import io.ktor.utils.io.jvm.javaio.toInputStream
import java.io.File
import java.io.RandomAccessFile

internal fun Route.installFileSystemRoutes(
    withAuth: WithAuth,
    fileRepo: suspend (ApplicationCall) -> FileSystemRepository?,
    stm: suspend (ApplicationCall) -> StreamTransferManager?,
    fsRepo: suspend (ApplicationCall) -> FileSystemRepository?
) {
    route("/files") {
        get {
            withAuth(call) {
                val repo = fileRepo(call) ?: return@withAuth
                val path = call.request.queryParameters["path"] ?: repo.defaultRoot
                val items = repo.listDirectory(path)
                val parent = File(path).parent
                call.respond(DirectoryListing(path = path, parent = parent, items = items))
            }
        }

        get("/download") {
            withAuth(call) {
                val repo = fileRepo(call) ?: return@withAuth
                val path = call.request.queryParameters["path"]
                    ?: return@withAuth call.respond(
                        HttpStatusCode.BadRequest,
                        ErrorResponse(error = "missing_param", message = "missing path")
                    )
                val (file, checksum) = repo.openFileForRead(path)
                val totalSize = file.length()
                call.response.header("X-Checksum-SHA256", checksum)
                call.response.header(HttpHeaders.AcceptRanges, "bytes")

                val range = call.request.headers[HttpHeaders.Range]
                if (range == null || !range.startsWith("bytes=", ignoreCase = true)) {
                    call.response.header(HttpHeaders.ContentLength, totalSize.toString())
                    call.respondFile(file)
                    return@withAuth
                }

                val spec = range.substringAfter("bytes=", "").trim()
                val parts = spec.split('-', limit = 2)
                val start = parts.getOrNull(0)?.trim()?.takeIf { it.isNotEmpty() }?.toLongOrNull()
                val end = parts.getOrNull(1)?.trim()?.takeIf { it.isNotEmpty() }?.toLongOrNull()

                if (start == null || start < 0 || start >= totalSize) {
                    call.response.header(HttpHeaders.ContentRange, "bytes */$totalSize")
                    call.respond(HttpStatusCode.RequestedRangeNotSatisfiable)
                    return@withAuth
                }

                val safeEnd = when {
                    end == null -> totalSize - 1
                    end < start -> start
                    end >= totalSize -> totalSize - 1
                    else -> end
                }
                val length = (safeEnd - start + 1).coerceAtLeast(0)

                call.response.status(HttpStatusCode.PartialContent)
                call.response.header(HttpHeaders.ContentRange, "bytes $start-$safeEnd/$totalSize")
                call.response.header(HttpHeaders.ContentLength, length.toString())
                call.respondOutputStream(contentType = ContentType.Application.OctetStream) {
                    RandomAccessFile(file, "r").use { raf ->
                        raf.seek(start)
                        val buf = ByteArray(256 * 1024)
                        var remaining = length
                        while (remaining > 0) {
                            val toRead = minOf(buf.size.toLong(), remaining).toInt()
                            val read = raf.read(buf, 0, toRead)
                            if (read <= 0) break
                            write(buf, 0, read)
                            remaining -= read
                        }
                    }
                }
            }
        }

        post("/upload") {
            withAuth(call) {
                val repo = fileRepo(call) ?: return@withAuth
                val multipart = call.receiveMultipart()
                var destPath: String? = null
                var checksum: String? = null
                var result: UploadResult? = null

                while (true) {
                    val part = multipart.readPart() ?: break
                    try {
                        when (part) {
                            is io.ktor.http.content.PartData.FormItem -> {
                                when (part.name) {
                                    "destinationPath" -> destPath = part.value
                                    "checksum" -> checksum = part.value
                                }
                            }
                            is io.ktor.http.content.PartData.FileItem -> {
                                val dp = destPath ?: continue
                                val stream = part.provider().toInputStream()
                                val (entry, actualChecksum) = repo.uploadFile(
                                    destPath = dp,
                                    inputStream = stream,
                                    expectedChecksum = checksum
                                )
                                result = UploadResult(
                                    path = entry.path,
                                    size = entry.size,
                                    checksum = actualChecksum
                                )
                            }
                            else -> { }
                        }
                    } finally {
                        part.release()
                    }
                }

                result?.let { call.respond(HttpStatusCode.Created, it) }
                    ?: call.respond(
                        HttpStatusCode.BadRequest,
                        ErrorResponse(error = "missing_file", message = "No file part in multipart body")
                    )
            }
        }

        delete {
            withAuth(call) {
                val repo = fileRepo(call) ?: return@withAuth
                val path = call.request.queryParameters["path"]
                    ?: return@withAuth call.respond(
                        HttpStatusCode.BadRequest,
                        ErrorResponse(error = "missing_param", message = "missing path")
                    )
                val recursive = call.request.queryParameters["recursive"]?.equals("true", ignoreCase = true) ?: false
                val ok = if (recursive) repo.deleteRecursive(path) else repo.delete(path)
                call.respond(DeleteResult(success = ok, path = path))
            }
        }

        post("/mkdir") {
            withAuth(call) {
                val repo = fileRepo(call) ?: return@withAuth
                val body = call.receive<Map<String, String>>()
                val path = body["path"]
                    ?: return@withAuth call.respond(
                        HttpStatusCode.BadRequest,
                        ErrorResponse(error = "missing_field", message = "missing path")
                    )
                val entry = repo.mkdir(path)
                call.respond(MkdirResult(path = entry.path, created = true))
            }
        }

        post("/rename") {
            withAuth(call) {
                val repo = fileRepo(call) ?: return@withAuth
                val body = call.receive<Map<String, Any>>()
                val path = body["path"] as? String
                    ?: return@withAuth call.respond(
                        HttpStatusCode.BadRequest,
                        ErrorResponse(error = "missing_field", message = "missing path")
                    )
                val newName = body["newName"] as? String
                    ?: return@withAuth call.respond(
                        HttpStatusCode.BadRequest,
                        ErrorResponse(error = "missing_field", message = "missing newName")
                    )
                val overwrite = (body["overwrite"] as? Boolean) ?: false
                val entry = repo.rename(path = path, newName = newName, overwrite = overwrite)
                call.respond(entry)
            }
        }

        post("/move") {
            withAuth(call) {
                val repo = fileRepo(call) ?: return@withAuth
                val body = call.receive<Map<String, Any>>()
                val fromPath = body["fromPath"] as? String
                    ?: return@withAuth call.respond(
                        HttpStatusCode.BadRequest,
                        ErrorResponse(error = "missing_field", message = "missing fromPath")
                    )
                val toPath = body["toPath"] as? String
                    ?: return@withAuth call.respond(
                        HttpStatusCode.BadRequest,
                        ErrorResponse(error = "missing_field", message = "missing toPath")
                    )
                val overwrite = (body["overwrite"] as? Boolean) ?: false
                val entry = repo.move(fromPath = fromPath, toPath = toPath, overwrite = overwrite)
                call.respond(entry)
            }
        }
    }

    route("/stream") {
        post("/init") {
            withAuth(call) {
                val s = stm(call) ?: return@withAuth
                val request = call.receive<StreamInitRequest>()
                val session = s.createSession(request)
                call.respond(StreamInitResponse(transferId = session.transferId, chunkSize = session.chunkSize))
            }
        }

        put("/{transferId}/chunk/{chunkIndex}") {
            withAuth(call) {
                val s = stm(call) ?: return@withAuth
                val r = fsRepo(call) ?: return@withAuth
                val transferId = call.parameters["transferId"]
                    ?: return@withAuth call.respond(
                        HttpStatusCode.BadRequest,
                        ErrorResponse(error = "missing_param", message = "missing transferId")
                    )
                val chunkIndex = call.parameters["chunkIndex"]?.toIntOrNull()
                    ?: return@withAuth call.respond(
                        HttpStatusCode.BadRequest,
                        ErrorResponse(error = "missing_param", message = "invalid chunkIndex")
                    )
                s.getSession(transferId) ?: return@withAuth call.respond(
                    HttpStatusCode.NotFound,
                    ErrorResponse(error = "not_found", message = "transfer session not found or expired")
                )
                val bytes = call.receive<ByteArray>()
                r.writeChunk(transferId, chunkIndex, bytes)
                call.respond(ChunkAck(received = true, chunkIndex = chunkIndex))
            }
        }

        post("/{transferId}/complete") {
            withAuth(call) {
                val s = stm(call) ?: return@withAuth
                val r = fsRepo(call) ?: return@withAuth
                val transferId = call.parameters["transferId"]
                    ?: return@withAuth call.respond(
                        HttpStatusCode.BadRequest,
                        ErrorResponse(error = "missing_param", message = "missing transferId")
                    )
                val session = s.getSession(transferId)
                    ?: return@withAuth call.respond(
                        HttpStatusCode.NotFound,
                        ErrorResponse(error = "not_found", message = "transfer session not found or expired")
                    )
                val (entry, _) = r.finalizeChunkedUpload(
                    transferId = transferId,
                    destPath = session.destPath,
                    totalChunks = session.totalChunks,
                    expectedChecksum = session.expectedChecksum
                )
                s.removeSession(transferId)
                call.respond(StreamCompleteResponse(success = true, finalPath = entry.path, verifiedChecksum = true))
            }
        }

        get("/{transferId}/status") {
            withAuth(call) {
                val s = stm(call) ?: return@withAuth
                val r = fsRepo(call) ?: return@withAuth
                val transferId = call.parameters["transferId"]
                    ?: return@withAuth call.respond(
                        HttpStatusCode.BadRequest,
                        ErrorResponse(error = "missing_param", message = "missing transferId")
                    )
                val session = s.getSession(transferId)
                    ?: return@withAuth call.respond(
                        HttpStatusCode.NotFound,
                        ErrorResponse(error = "not_found", message = "transfer session not found or expired")
                    )
                val received = r.getReceivedChunks(transferId)
                call.respond(
                    StreamStatusResponse(
                        transferId = transferId,
                        chunksReceived = received,
                        totalChunks = session.totalChunks,
                        bytesReceived = received.size.toLong() * session.chunkSize
                    )
                )
            }
        }
    }
}
