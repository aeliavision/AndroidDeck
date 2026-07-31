package com.aeliavision.androiddeck.feature.gallery.model

import androidx.annotation.Keep

enum class MediaType { IMAGE, VIDEO, AUDIO }

@Keep
data class AlbumEntry(
    val id: String,
    val name: String,
    val coverMediaId: Long,
    val coverMediaType: MediaType,
    var count: Int
)

@Keep
data class MediaEntry(
    val id: String,
    val name: String,
    val uri: String,
    val mimeType: String,
    val size: Long,
    val dateTaken: Long,
    val width: Int,
    val height: Int,
    val mediaType: MediaType
)

@Keep
data class AlbumsResponse(val albums: List<AlbumDto>)

@Keep
data class AlbumDto(
    val id: String,
    val name: String,
    val coverMediaId: String,
    val coverMediaType: String,
    val count: Int
)

@Keep
data class MediaPageResponse(
    val items: List<MediaDto>,
    val nextPage: Int?,
    val page: Int,
    val pageSize: Int
)

@Keep
data class MediaDto(
    val id: String,
    val name: String,
    val mimeType: String,
    val size: Long,
    val dateTaken: Long,
    val width: Int,
    val height: Int,
    val mediaType: String
)

@Keep
data class GalleryActionResult(
    val success: Boolean,
    val error: String? = null,
    val message: String? = null,
    val updatedName: String? = null,
    val updatedPath: String? = null
)

@Keep
data class GalleryDeleteRequest(
    val ids: List<String>,
    val mediaType: String = "image"
)

@Keep
data class GalleryRenameRequest(
    val id: String,
    val newName: String,
    val mediaType: String = "image"
)

@Keep
data class GalleryMoveRequest(
    val ids: List<String>,
    val targetRelativePath: String,
    val mediaType: String = "image"
)

@Keep
data class GalleryMetadataRequest(
    val id: String,
    val mediaType: String = "image",
    val favorite: Boolean? = null,
    val description: String? = null
)
