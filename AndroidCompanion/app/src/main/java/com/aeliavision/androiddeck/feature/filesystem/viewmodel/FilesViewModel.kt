package com.aeliavision.androiddeck.feature.filesystem.viewmodel

import androidx.lifecycle.ViewModel
import androidx.lifecycle.viewModelScope
import com.aeliavision.androiddeck.feature.filesystem.data.FileSystemRepository
import com.aeliavision.androiddeck.feature.filesystem.model.FileEntry
import dagger.hilt.android.lifecycle.HiltViewModel
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.launch
import java.io.File
import javax.inject.Inject

@androidx.compose.runtime.Immutable
data class FilesUiState(
    val currentPath: String = "",
    val items: List<FileEntry> = emptyList(),
    val isLoading: Boolean = false,
    val error: String? = null,
    val permissionGranted: Boolean = false
)

@HiltViewModel
class FilesViewModel @Inject constructor(
    private val repository: FileSystemRepository
) : ViewModel() {

    private val _uiState = MutableStateFlow(FilesUiState())
    val uiState: StateFlow<FilesUiState> = _uiState.asStateFlow()

    init {
        _uiState.value = _uiState.value.copy(currentPath = repository.defaultRoot)
    }

    fun onPermissionResult(granted: Boolean) {
        _uiState.value = _uiState.value.copy(permissionGranted = granted)
        if (granted) {
            refresh()
        }
    }

    fun refresh() {
        loadDirectory(_uiState.value.currentPath)
    }

    fun enterDirectory(path: String) {
        loadDirectory(path)
    }

    fun navigateUp() {
        val current = _uiState.value.currentPath
        val parent = File(current).parent ?: return
        loadDirectory(parent)
    }

    private fun loadDirectory(path: String) {
        viewModelScope.launch {
            _uiState.value = _uiState.value.copy(isLoading = true, error = null)
            try {
                val file = File(path)
                if (!file.exists()) {
                    val defaultRoot = repository.defaultRoot
                    if (path != defaultRoot) {
                        loadDirectory(defaultRoot)
                        return@launch
                    }
                }

                val items = repository.listDirectory(path)
                val distinctItems = items.distinctBy { it.path }
                
                _uiState.value = _uiState.value.copy(
                    currentPath = path,
                    items = distinctItems,
                    isLoading = false
                )
            } catch (e: Exception) {
                _uiState.value = _uiState.value.copy(
                    isLoading = false,
                    error = e.message ?: "Failed to load directory"
                )
            }
        }
    }

    fun deleteFile(path: String) {
        viewModelScope.launch {
            try {
                repository.deleteRecursive(path)
                refresh()
            } catch (e: Exception) {
                _uiState.value = _uiState.value.copy(error = e.message)
            }
        }
    }

    fun renameFile(path: String, newName: String) {
        viewModelScope.launch {
            try {
                repository.rename(path, newName)
                refresh()
            } catch (e: Exception) {
                _uiState.value = _uiState.value.copy(error = e.message)
            }
        }
    }

    fun createDirectory(name: String) {
        viewModelScope.launch {
            try {
                val path = File(_uiState.value.currentPath, name).absolutePath
                repository.mkdir(path)
                refresh()
            } catch (e: Exception) {
                _uiState.value = _uiState.value.copy(error = e.message)
            }
        }
    }

    fun clearError() {
        _uiState.value = _uiState.value.copy(error = null)
    }
}
