package com.aeliavision.androiddeck.feature.gallery.data

import android.content.ContentResolver
import android.content.ContentUris
import android.content.ContentValues
import android.content.Context
import android.graphics.Bitmap
import android.os.Build
import android.os.Bundle
import android.provider.MediaStore
import android.util.Size
import com.aeliavision.androiddeck.feature.gallery.model.AlbumEntry
import com.aeliavision.androiddeck.feature.gallery.model.MediaEntry
import com.aeliavision.androiddeck.feature.gallery.model.MediaType
import dagger.hilt.android.qualifiers.ApplicationContext
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext
import java.io.ByteArrayOutputStream
import java.io.InputStream
import javax.inject.Inject
import javax.inject.Singleton

/**
 */
@Singleton
class GalleryRepository @Inject constructor(
    @ApplicationContext private val context: Context
) {
    private val contentResolver: ContentResolver = context.contentResolver

    suspend fun getAlbums(): List<AlbumEntry> = withContext(Dispatchers.IO) {
        val albums = LinkedHashMap<String, AlbumEntry>()

        queryAlbums(
            uri = MediaStore.Images.Media.EXTERNAL_CONTENT_URI,
            bucketIdCol = MediaStore.Images.Media.BUCKET_ID,
            bucketNameCol = MediaStore.Images.Media.BUCKET_DISPLAY_NAME,
            idCol = MediaStore.Images.Media._ID,
            dateCol = MediaStore.Images.Media.DATE_TAKEN,
            mediaType = MediaType.IMAGE,
            albums = albums
        )

        queryAlbums(
            uri = MediaStore.Video.Media.EXTERNAL_CONTENT_URI,
            bucketIdCol = MediaStore.Video.Media.BUCKET_ID,
            bucketNameCol = MediaStore.Video.Media.BUCKET_DISPLAY_NAME,
            idCol = MediaStore.Video.Media._ID,
            dateCol = MediaStore.Video.Media.DATE_TAKEN,
            mediaType = MediaType.VIDEO,
            albums = albums
        )

        albums.values.sortedByDescending { it.count }.toList()
    }

    private fun queryAlbums(
        uri: android.net.Uri,
        bucketIdCol: String,
        bucketNameCol: String,
        idCol: String,
        dateCol: String,
        mediaType: MediaType,
        albums: LinkedHashMap<String, AlbumEntry>
    ) {
        val projection = arrayOf(bucketIdCol, bucketNameCol, idCol)
        val bundle = Bundle().apply {
            putString(ContentResolver.QUERY_ARG_SQL_SORT_ORDER, "$bucketIdCol ASC, $dateCol DESC")
        }

        contentResolver.query(uri, projection, bundle, null)?.use { cursor ->
            val bucketIdIdx = cursor.getColumnIndex(bucketIdCol)
            val bucketNameIdx = cursor.getColumnIndex(bucketNameCol)
            val idIdx = cursor.getColumnIndex(idCol)

            var lastBucketId: String? = null

            while (cursor.moveToNext()) {
                val bucketId = cursor.getString(bucketIdIdx) ?: continue
                
                if (bucketId == lastBucketId) {
                    albums[bucketId]?.let { it.count += 1 }
                    continue
                }

                val bucketName = cursor.getString(bucketNameIdx) ?: "Unknown"
                val mediaId = cursor.getLong(idIdx)

                albums[bucketId] = AlbumEntry(
                    id = bucketId,
                    name = bucketName,
                    coverMediaId = mediaId,
                    coverMediaType = mediaType,
                    count = 1
                )
                lastBucketId = bucketId
            }
        }
    }

    suspend fun getMedia(
        albumId: String? = null,
        mediaTypes: Set<MediaType> = setOf(MediaType.IMAGE, MediaType.VIDEO),
        page: Int = 1,
        pageSize: Int = 50
    ): Pair<List<MediaEntry>, Boolean> = withContext(Dispatchers.IO) {
        val projection = arrayOf(
            MediaStore.Files.FileColumns._ID,
            MediaStore.Files.FileColumns.DISPLAY_NAME,
            MediaStore.Files.FileColumns.SIZE,
            MediaStore.Files.FileColumns.MIME_TYPE,
            MediaStore.Files.FileColumns.DATE_TAKEN,
            MediaStore.Files.FileColumns.WIDTH,
            MediaStore.Files.FileColumns.HEIGHT,
            MediaStore.Files.FileColumns.MEDIA_TYPE
        )

        val typeFilters = mutableListOf<String>()
        if (MediaType.IMAGE in mediaTypes) typeFilters.add(MediaStore.Files.FileColumns.MEDIA_TYPE_IMAGE.toString())
        if (MediaType.VIDEO in mediaTypes) typeFilters.add(MediaStore.Files.FileColumns.MEDIA_TYPE_VIDEO.toString())
        if (MediaType.AUDIO in mediaTypes) typeFilters.add(MediaStore.Files.FileColumns.MEDIA_TYPE_AUDIO.toString())

        if (typeFilters.isEmpty()) return@withContext Pair(emptyList(), false)

        val selectionList = mutableListOf("${MediaStore.Files.FileColumns.MEDIA_TYPE} IN (${typeFilters.joinToString(",")})")
        val selectionArgs = mutableListOf<String>()

        if (albumId != null) {
            selectionList.add("${MediaStore.Files.FileColumns.BUCKET_ID} = ?")
            selectionArgs.add(albumId)
        }

        val queryBundle = Bundle().apply {
            putString(ContentResolver.QUERY_ARG_SQL_SELECTION, selectionList.joinToString(" AND "))
            putStringArray(ContentResolver.QUERY_ARG_SQL_SELECTION_ARGS, selectionArgs.toTypedArray())
            putString(ContentResolver.QUERY_ARG_SQL_SORT_ORDER, "${MediaStore.Files.FileColumns.DATE_TAKEN} DESC")
            putInt(ContentResolver.QUERY_ARG_OFFSET, (page - 1) * pageSize)
            putInt(ContentResolver.QUERY_ARG_LIMIT, pageSize + 1)
        }

        val results = mutableListOf<MediaEntry>()
        val uri = MediaStore.Files.getContentUri("external")

        contentResolver.query(uri, projection, queryBundle, null)?.use { cursor ->
            val idIdx = cursor.getColumnIndex(MediaStore.Files.FileColumns._ID)
            val nameIdx = cursor.getColumnIndex(MediaStore.Files.FileColumns.DISPLAY_NAME)
            val sizeIdx = cursor.getColumnIndex(MediaStore.Files.FileColumns.SIZE)
            val mimeIdx = cursor.getColumnIndex(MediaStore.Files.FileColumns.MIME_TYPE)
            val dateIdx = cursor.getColumnIndex(MediaStore.Files.FileColumns.DATE_TAKEN)
            val widthIdx = cursor.getColumnIndex(MediaStore.Files.FileColumns.WIDTH)
            val heightIdx = cursor.getColumnIndex(MediaStore.Files.FileColumns.HEIGHT)
            val typeIdx = cursor.getColumnIndex(MediaStore.Files.FileColumns.MEDIA_TYPE)

            while (cursor.moveToNext()) {
                val id = cursor.getLong(idIdx)
                val mType = when (cursor.getInt(typeIdx)) {
                    MediaStore.Files.FileColumns.MEDIA_TYPE_VIDEO -> MediaType.VIDEO
                    MediaStore.Files.FileColumns.MEDIA_TYPE_AUDIO -> MediaType.AUDIO
                    else -> MediaType.IMAGE
                }
                
                val baseUri = when (mType) {
                    MediaType.VIDEO -> MediaStore.Video.Media.EXTERNAL_CONTENT_URI
                    else -> MediaStore.Images.Media.EXTERNAL_CONTENT_URI
                }
                val contentUri = ContentUris.withAppendedId(baseUri, id)

                results.add(MediaEntry(
                    id = id.toString(),
                    name = cursor.getString(nameIdx) ?: "Unknown",
                    uri = contentUri.toString(),
                    mimeType = cursor.getString(mimeIdx) ?: "application/octet-stream",
                    size = cursor.getLong(sizeIdx),
                    dateTaken = cursor.getLong(dateIdx),
                    width = cursor.getInt(widthIdx),
                    height = cursor.getInt(heightIdx),
                    mediaType = mType
                ))
            }
        }

        val hasMore = results.size > pageSize
        val finalItems = if (hasMore) results.take(pageSize) else results
        Pair(finalItems, hasMore)
    }

    suspend fun getThumbnail(
        mediaId: String,
        mediaType: MediaType,
        maxDim: Int = 256
    ): ByteArray = withContext(Dispatchers.IO) {
        val id = mediaId.toLong()
        val uri = when (mediaType) {
            MediaType.IMAGE -> ContentUris.withAppendedId(
                MediaStore.Images.Media.EXTERNAL_CONTENT_URI, id)
            MediaType.VIDEO -> ContentUris.withAppendedId(
                MediaStore.Video.Media.EXTERNAL_CONTENT_URI, id)
            MediaType.AUDIO -> ContentUris.withAppendedId(
                MediaStore.Audio.Media.EXTERNAL_CONTENT_URI, id)
        }

        val bitmap = contentResolver.loadThumbnail(uri, Size(maxDim, maxDim), null)

        ByteArrayOutputStream().use { out ->
            bitmap.compress(Bitmap.CompressFormat.JPEG, 80, out)
            out.toByteArray()
        }
    }

    suspend fun openMediaStream(
        mediaId: String,
        mediaType: MediaType
    ): Pair<InputStream, Long> = withContext(Dispatchers.IO) {
        val id = mediaId.toLong()
        val uri = when (mediaType) {
            MediaType.IMAGE -> ContentUris.withAppendedId(
                MediaStore.Images.Media.EXTERNAL_CONTENT_URI, id)
            MediaType.VIDEO -> ContentUris.withAppendedId(
                MediaStore.Video.Media.EXTERNAL_CONTENT_URI, id)
            MediaType.AUDIO -> ContentUris.withAppendedId(
                MediaStore.Audio.Media.EXTERNAL_CONTENT_URI, id)
        }

        val pfd = contentResolver.openFileDescriptor(uri, "r")
            ?: throw IllegalStateException("Could not open media file for mediaId=$mediaId")
        val size = pfd.statSize
        val stream = android.os.ParcelFileDescriptor.AutoCloseInputStream(pfd)
        Pair(stream, size)
    }

    private fun uriFor(id: Long, mediaType: MediaType): android.net.Uri {
        val baseUri = when (mediaType) {
            MediaType.VIDEO -> MediaStore.Video.Media.EXTERNAL_CONTENT_URI
            MediaType.AUDIO -> MediaStore.Audio.Media.EXTERNAL_CONTENT_URI
            else -> MediaStore.Images.Media.EXTERNAL_CONTENT_URI
        }
        return ContentUris.withAppendedId(baseUri, id)
    }

    suspend fun deleteMedia(ids: List<String>, mediaType: MediaType): Pair<Int, String?> =
        withContext(Dispatchers.IO) {
            var deleted = 0
            try {
                for (idStr in ids) {
                    val id = idStr.toLongOrNull() ?: continue
                    val uri = uriFor(id, mediaType)
                    deleted += contentResolver.delete(uri, null, null)
                }
                Pair(deleted, null)
            } catch (se: SecurityException) {
                Pair(deleted, "security_exception")
            } catch (e: Exception) {
                Pair(deleted, e.message ?: "error")
            }
        }

    suspend fun renameMedia(id: String, mediaType: MediaType, newName: String): Pair<Boolean, String?> =
        withContext(Dispatchers.IO) {
            val mediaId = id.toLongOrNull() ?: return@withContext Pair(false, "invalid_id")
            val uri = uriFor(mediaId, mediaType)
            val values = ContentValues().apply {
                put(MediaStore.MediaColumns.DISPLAY_NAME, newName)
            }
            try {
                val updated = contentResolver.update(uri, values, null, null)
                Pair(updated > 0, null)
            } catch (se: SecurityException) {
                Pair(false, "security_exception")
            } catch (e: Exception) {
                Pair(false, e.message ?: "error")
            }
        }

    suspend fun moveMedia(ids: List<String>, mediaType: MediaType, targetRelativePath: String): Pair<Int, String?> =
        withContext(Dispatchers.IO) {
            var moved = 0
            val rel = targetRelativePath.trim().trimStart('/')
            val values = ContentValues().apply {
                put(MediaStore.MediaColumns.RELATIVE_PATH, if (rel.endsWith('/')) rel else "$rel/")
            }
            if (values.size() == 0) return@withContext Pair(0, "not_supported")
            
            try {
                for (idStr in ids) {
                    val id = idStr.toLongOrNull() ?: continue
                    val uri = uriFor(id, mediaType)
                    val updated = contentResolver.update(uri, values, null, null)
                    if (updated > 0) moved++
                }
                Pair(moved, null)
            } catch (se: SecurityException) {
                Pair(moved, "security_exception")
            } catch (e: Exception) {
                Pair(moved, e.message ?: "error")
            }
        }

    suspend fun updateMetadata(
        id: String,
        mediaType: MediaType,
        favorite: Boolean?,
        description: String?
    ): Pair<Boolean, String?> = withContext(Dispatchers.IO) {
        val mediaId = id.toLongOrNull() ?: return@withContext Pair(false, "invalid_id")
        val uri = uriFor(mediaId, mediaType)
        val values = ContentValues()

        if (favorite != null && Build.VERSION.SDK_INT >= Build.VERSION_CODES.R) {
            values.put(MediaStore.MediaColumns.IS_FAVORITE, if (favorite) 1 else 0)
        }

        if (values.size() == 0)
            return@withContext Pair(false, "not_supported")

        try {
            val updated = contentResolver.update(uri, values, null, null)
            Pair(updated > 0, null)
        } catch (se: SecurityException) {
            Pair(false, "security_exception")
        } catch (e: Exception) {
            Pair(false, e.message ?: "error")
        }
    }
}
