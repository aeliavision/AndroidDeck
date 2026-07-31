package com.aeliavision.androiddeck.feature.gallery.viewmodel

import androidx.lifecycle.ViewModel
import androidx.lifecycle.viewModelScope
import com.aeliavision.androiddeck.feature.gallery.data.GalleryRepository
import com.aeliavision.androiddeck.feature.gallery.model.AlbumEntry
import com.aeliavision.androiddeck.feature.gallery.model.MediaEntry
import dagger.hilt.android.lifecycle.HiltViewModel
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.launch
import javax.inject.Inject

@androidx.compose.runtime.Immutable
data class GalleryUiState(
    val albums: List<AlbumEntry> = emptyList(),
    val media: List<MediaEntry> = emptyList(),
    val selectedAlbumId: String? = null,
    val selectedAlbumName: String = "All Media",
    val selectedIds: Set<String> = emptySet(),
    val isLoading: Boolean = false,
    val isLoadingMore: Boolean = false,
    val error: String? = null
)

@HiltViewModel
class GalleryViewModel @Inject constructor(
    private val repository: GalleryRepository
) : ViewModel() {

    private val _uiState = MutableStateFlow(GalleryUiState())
    val uiState: StateFlow<GalleryUiState> = _uiState.asStateFlow()

    private var currentPage = 1
    private var hasMore = true
    private val pageSize = 50

    fun toggleSelection(id: String) {
        val current = _uiState.value.selectedIds
        _uiState.value = _uiState.value.copy(
            selectedIds = if (current.contains(id)) current - id else current + id
        )
    }

    fun loadAlbums() {
        viewModelScope.launch {
            try {
                val albums = repository.getAlbums()
                _uiState.value = _uiState.value.copy(albums = albums)
            } catch (e: Exception) {
                _uiState.value = _uiState.value.copy(error = e.message)
            }
        }
    }

    fun selectAlbum(albumId: String?) {
        val name = _uiState.value.albums.firstOrNull { it.id == albumId }?.name ?: "All Media"
        _uiState.value = _uiState.value.copy(
            selectedAlbumId = albumId,
            selectedAlbumName = name
        )
        refreshMedia()
    }

    fun refreshMedia() {
        loadMedia(reset = true)
    }

    fun loadNextPage() {
        if (!hasMore || _uiState.value.isLoading || _uiState.value.isLoadingMore) return
        loadMedia(reset = false)
    }

    private fun loadMedia(reset: Boolean) {
        viewModelScope.launch {
            if (reset) {
                currentPage = 1
                hasMore = true
                _uiState.value = _uiState.value.copy(isLoading = true, media = emptyList(), error = null)
            } else {
                _uiState.value = _uiState.value.copy(isLoadingMore = true)
            }

            try {
                val page = if (reset) 1 else currentPage + 1
                val (items, more) = repository.getMedia(
                    albumId = _uiState.value.selectedAlbumId,
                    page = page,
                    pageSize = pageSize
                )
                
                hasMore = more
                currentPage = page
                
                val newList = if (reset) items else (_uiState.value.media + items)
                val distinctList = newList.distinctBy { it.id }

                _uiState.value = _uiState.value.copy(
                    media = distinctList,
                    isLoading = false,
                    isLoadingMore = false
                )
            } catch (e: Exception) {
                _uiState.value = _uiState.value.copy(
                    isLoading = false,
                    isLoadingMore = false,
                    error = e.message ?: "Failed to load media"
                )
            }
        }
    }

    fun clearError() {
        _uiState.value = _uiState.value.copy(error = null)
    }
}
