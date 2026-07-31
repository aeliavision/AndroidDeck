package com.aeliavision.androiddeck.feature.server.service.routes

import com.aeliavision.androiddeck.feature.contacts.model.ErrorResponse
import com.aeliavision.androiddeck.feature.gallery.data.GalleryRepository
import com.aeliavision.androiddeck.feature.gallery.model.AlbumDto
import com.aeliavision.androiddeck.feature.gallery.model.AlbumsResponse
import com.aeliavision.androiddeck.feature.gallery.model.MediaDto
import com.aeliavision.androiddeck.feature.gallery.model.MediaPageResponse
import com.aeliavision.androiddeck.feature.gallery.model.MediaType
import io.ktor.http.ContentType
import io.ktor.http.HttpStatusCode
import io.ktor.server.application.ApplicationCall
import io.ktor.server.request.receive
import io.ktor.server.response.header
import io.ktor.server.response.respond
import io.ktor.server.response.respondBytes
import io.ktor.server.response.respondOutputStream
import io.ktor.server.routing.Route
import io.ktor.server.routing.get
import io.ktor.server.routing.post
import io.ktor.server.routing.route

internal fun Route.installGalleryRoutes(
    withAuth: WithAuth,
    galleryRepo: suspend (ApplicationCall) -> GalleryRepository?
) {
    route("/gallery") {
        get("/albums") {
            withAuth(call) {
                val repo = galleryRepo(call) ?: return@withAuth
                val albums = repo.getAlbums()
                call.respond(
                    AlbumsResponse(
                        albums = albums.map { a ->
                            AlbumDto(
                                id = a.id,
                                name = a.name,
                                coverMediaId = a.coverMediaId.toString(),
                                coverMediaType = a.coverMediaType.name.lowercase(),
                                count = a.count
                            )
                        }
                    )
                )
            }
        }

        get("/media") {
            withAuth(call) {
                val repo = galleryRepo(call) ?: return@withAuth
                val albumId = call.request.queryParameters["albumId"]
                val page = call.request.queryParameters["page"]?.toIntOrNull() ?: 1
                val pageSize = call.request.queryParameters["pageSize"]?.toIntOrNull() ?: 50
                val typesParam = call.request.queryParameters["types"]
                val mediaTypes = typesParam?.split(",")?.mapNotNull {
                    runCatching { MediaType.valueOf(it.uppercase()) }.getOrNull()
                }?.toSet() ?: setOf(MediaType.IMAGE, MediaType.VIDEO)

                val (items, hasMore) = repo.getMedia(albumId, mediaTypes, page, pageSize)
                call.respond(
                    MediaPageResponse(
                        items = items.map { m ->
                            MediaDto(
                                id = m.id,
                                name = m.name,
                                mimeType = m.mimeType,
                                size = m.size,
                                dateTaken = m.dateTaken,
                                width = m.width,
                                height = m.height,
                                mediaType = m.mediaType.name.lowercase()
                            )
                        },
                        nextPage = if (hasMore) page + 1 else null,
                        page = page,
                        pageSize = pageSize
                    )
                )
            }
        }

        get("/thumbnail/{mediaId}") {
            withAuth(call) {
                val repo = galleryRepo(call) ?: return@withAuth
                val mediaId = call.parameters["mediaId"]
                    ?: return@withAuth call.respond(
                        HttpStatusCode.BadRequest,
                        ErrorResponse(error = "missing_param", message = "missing mediaId")
                    )
                val typeStr = call.request.queryParameters["type"] ?: "image"
                val mediaType = runCatching { MediaType.valueOf(typeStr.uppercase()) }.getOrDefault(MediaType.IMAGE)
                val maxDim = call.request.queryParameters["maxDim"]?.toIntOrNull() ?: 256
                val jpegBytes = repo.getThumbnail(mediaId, mediaType, maxDim)
                call.respondBytes(jpegBytes, ContentType.Image.JPEG)
            }
        }

        get("/download/{mediaId}") {
            withAuth(call) {
                val repo = galleryRepo(call) ?: return@withAuth
                val mediaId = call.parameters["mediaId"]
                    ?: return@withAuth call.respond(
                        HttpStatusCode.BadRequest,
                        ErrorResponse(error = "missing_param", message = "missing mediaId")
                    )
                val typeStr = call.request.queryParameters["type"] ?: "image"
                val mediaType = runCatching { MediaType.valueOf(typeStr.uppercase()) }.getOrDefault(MediaType.IMAGE)
                val (stream, size) = repo.openMediaStream(mediaId, mediaType)
                call.response.header("Content-Length", size.toString())
                call.respondOutputStream(
                    contentType = ContentType.Application.OctetStream,
                    status = HttpStatusCode.OK
                ) {
                    stream.use { it.copyTo(this) }
                }
            }
        }

        post("/delete") {
            withAuth(call) {
                val repo = galleryRepo(call) ?: return@withAuth
                val req = call.receive<com.aeliavision.androiddeck.feature.gallery.model.GalleryDeleteRequest>()
                val mediaType = runCatching { MediaType.valueOf(req.mediaType.uppercase()) }.getOrDefault(MediaType.IMAGE)
                val (count, err) = repo.deleteMedia(req.ids, mediaType)
                call.respond(
                    com.aeliavision.androiddeck.feature.gallery.model.GalleryActionResult(
                        success = err == null,
                        error = err,
                        message = if (err == null) "deleted=$count" else "deleted=$count"
                    )
                )
            }
        }

        post("/rename") {
            withAuth(call) {
                val repo = galleryRepo(call) ?: return@withAuth
                val req = call.receive<com.aeliavision.androiddeck.feature.gallery.model.GalleryRenameRequest>()
                val mediaType = runCatching { MediaType.valueOf(req.mediaType.uppercase()) }.getOrDefault(MediaType.IMAGE)
                val (ok, err) = repo.renameMedia(req.id, mediaType, req.newName)
                call.respond(
                    com.aeliavision.androiddeck.feature.gallery.model.GalleryActionResult(
                        success = ok,
                        error = err,
                        updatedName = if (ok) req.newName else null
                    )
                )
            }
        }

        post("/move") {
            withAuth(call) {
                val repo = galleryRepo(call) ?: return@withAuth
                val req = call.receive<com.aeliavision.androiddeck.feature.gallery.model.GalleryMoveRequest>()
                val mediaType = runCatching { MediaType.valueOf(req.mediaType.uppercase()) }.getOrDefault(MediaType.IMAGE)
                val (count, err) = repo.moveMedia(req.ids, mediaType, req.targetRelativePath)
                call.respond(
                    com.aeliavision.androiddeck.feature.gallery.model.GalleryActionResult(
                        success = err == null,
                        error = err,
                        message = if (err == null) "moved=$count" else "moved=$count"
                    )
                )
            }
        }

        post("/metadata") {
            withAuth(call) {
                val repo = galleryRepo(call) ?: return@withAuth
                val req = call.receive<com.aeliavision.androiddeck.feature.gallery.model.GalleryMetadataRequest>()
                val mediaType = runCatching { MediaType.valueOf(req.mediaType.uppercase()) }.getOrDefault(MediaType.IMAGE)
                val (ok, err) = repo.updateMetadata(req.id, mediaType, req.favorite, req.description)
                call.respond(
                    com.aeliavision.androiddeck.feature.gallery.model.GalleryActionResult(
                        success = ok,
                        error = err
                    )
                )
            }
        }
    }
}
