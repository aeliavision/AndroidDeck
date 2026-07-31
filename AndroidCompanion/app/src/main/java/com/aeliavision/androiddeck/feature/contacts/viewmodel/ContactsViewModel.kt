package com.aeliavision.androiddeck.feature.contacts.viewmodel

import android.content.Context
import android.net.Uri
import androidx.lifecycle.ViewModel
import androidx.lifecycle.viewModelScope
import com.aeliavision.androiddeck.feature.contacts.data.ContactsRepository
import com.aeliavision.androiddeck.feature.contacts.model.ContactDto
import com.aeliavision.androiddeck.feature.contacts.model.GroupDto
import com.aeliavision.androiddeck.feature.contacts.data.ImportResult
import dagger.hilt.android.lifecycle.HiltViewModel
import dagger.hilt.android.qualifiers.ApplicationContext
import kotlinx.coroutines.flow.MutableSharedFlow
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.SharedFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asSharedFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.launch
import java.io.InputStream
import javax.inject.Inject

data class ContactsUiState(
    val contacts: List<ContactDto> = emptyList(),
    val groups: List<GroupDto> = emptyList(),
    val isLoading: Boolean = false,
    val error: String? = null
)

@HiltViewModel
class ContactsViewModel @Inject constructor(
    @ApplicationContext private val context: Context,
    private val repository: ContactsRepository
) : ViewModel() {

    private val _uiState = MutableStateFlow(ContactsUiState())
    val uiState: StateFlow<ContactsUiState> = _uiState.asStateFlow()

    private val _importResult = MutableSharedFlow<ImportResult>()
    val importResult: SharedFlow<ImportResult> = _importResult.asSharedFlow()

    private val _exportResult = MutableSharedFlow<String>()
    val exportResult: SharedFlow<String> = _exportResult.asSharedFlow()

    private val _searchQuery = MutableStateFlow("")
    val searchQuery: StateFlow<String> = _searchQuery.asStateFlow()

    private var currentPage = 1
    private var hasMore = true
    private var currentGroupId: String? = null

    init {
        refresh()
        loadGroups()
    }

    fun refresh() {
        currentPage = 1
        hasMore = true
        loadContacts(reset = true)
    }

    fun loadNextPage() {
        if (!hasMore || _uiState.value.isLoading) return
        loadContacts(reset = false)
    }

    fun search(query: String) {
        _searchQuery.value = query
        refresh()
    }

    fun loadContactsByGroup(groupId: String?) {
        currentGroupId = groupId
        refresh()
    }

    fun loadGroups() {
        viewModelScope.launch {
            try {
                val groups = repository.getGroups()
                val distinctGroups = groups.distinctBy { it.id }
                _uiState.value = _uiState.value.copy(groups = distinctGroups)
            } catch (e: Exception) {
                _uiState.value = _uiState.value.copy(error = e.message ?: "Failed to load groups")
            }
        }
    }

    private fun loadContacts(reset: Boolean) {
        viewModelScope.launch {
            if (reset) {
                _uiState.value = _uiState.value.copy(isLoading = true, error = null)
            }
            
            try {
                val page = if (reset) 1 else currentPage + 1
                val query = _searchQuery.value.takeIf { it.isNotBlank() }
                
                val items = if (currentGroupId != null) {
                    repository.getContactsByGroup(currentGroupId!!)
                } else {
                    repository.getContacts(page = page, query = query)
                }

                hasMore = items.size >= 50
                currentPage = page
                val distinctList = kotlinx.coroutines.withContext(kotlinx.coroutines.Dispatchers.Default) {
                    val newList = if (reset) items else _uiState.value.contacts + items
                    newList.distinctBy { it.id ?: it.fullName }
                }

                _uiState.value = _uiState.value.copy(
                    contacts = distinctList,
                    isLoading = false
                )
            } catch (e: Exception) {
                _uiState.value = _uiState.value.copy(
                    isLoading = false,
                    error = e.message ?: "Failed to load contacts"
                )
            }
        }
    }

    fun importVcf(uri: Uri) {
        viewModelScope.launch {
            _uiState.value = _uiState.value.copy(isLoading = true)
            try {
                val inputStream = context.contentResolver.openInputStream(uri)
                if (inputStream != null) {
                    val result = repository.importVcf(inputStream, null, null)
                    _importResult.emit(result)
                    refresh()
                }
            } catch (e: Exception) {
                _uiState.value = _uiState.value.copy(error = e.message ?: "Import failed")
            } finally {
                _uiState.value = _uiState.value.copy(isLoading = false)
            }
        }
    }

    fun exportVcf() {
        viewModelScope.launch {
            _uiState.value = _uiState.value.copy(isLoading = true)
            try {
                val result = repository.exportVcf()
                _exportResult.emit(result)
            } catch (e: Exception) {
                _uiState.value = _uiState.value.copy(error = e.message ?: "Export failed")
            } finally {
                _uiState.value = _uiState.value.copy(isLoading = false)
            }
        }
    }

    fun clearError() {
        _uiState.value = _uiState.value.copy(error = null)
    }
}
