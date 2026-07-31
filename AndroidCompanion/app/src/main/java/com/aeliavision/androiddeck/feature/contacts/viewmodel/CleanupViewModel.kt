package com.aeliavision.androiddeck.feature.contacts.viewmodel

import androidx.lifecycle.ViewModel
import androidx.lifecycle.viewModelScope
import com.aeliavision.androiddeck.feature.contacts.data.ContactsRepository
import com.aeliavision.androiddeck.feature.contacts.model.DuplicateGroup
import dagger.hilt.android.lifecycle.HiltViewModel
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.launch
import javax.inject.Inject

data class CleanupUiState(
    val duplicateGroups: List<DuplicateGroup> = emptyList(),
    val isLoading: Boolean = false,
    val error: String? = null
)

@HiltViewModel
class CleanupViewModel @Inject constructor(
    private val repository: ContactsRepository
) : ViewModel() {

    private val _uiState = MutableStateFlow(CleanupUiState())
    val uiState: StateFlow<CleanupUiState> = _uiState.asStateFlow()

    fun loadDuplicates() {
        viewModelScope.launch {
            _uiState.value = _uiState.value.copy(isLoading = true, error = null)
            try {
                val groups = repository.getDuplicateGroups()
                val resolvedGroups = groups.map { group ->
                    val contacts = repository.getContactDetails(group.contactIds)
                    group.copy(contacts = contacts)
                }
                _uiState.value = _uiState.value.copy(duplicateGroups = resolvedGroups, isLoading = false)
            } catch (e: Exception) {
                _uiState.value = _uiState.value.copy(error = e.message, isLoading = false)
            }
        }
    }

    fun mergeGroup(group: DuplicateGroup) {
        viewModelScope.launch {
            _uiState.value = _uiState.value.copy(isLoading = true)
            try {
                val targetId = group.contactIds.first()
                val sourceIds = group.contactIds.drop(1)
                repository.mergeContacts(targetId, sourceIds)
                loadDuplicates() // Reload
            } catch (e: Exception) {
                _uiState.value = _uiState.value.copy(error = e.message, isLoading = false)
            }
        }
    }

    fun clearError() {
        _uiState.value = _uiState.value.copy(error = null)
    }
}
