package com.aeliavision.androiddeck.feature.contacts.viewmodel

import android.content.ContentResolver
import android.net.Uri
import androidx.lifecycle.ViewModel
import androidx.lifecycle.viewModelScope
import com.aeliavision.androiddeck.feature.contacts.data.ContactsRepository
import com.aeliavision.androiddeck.feature.contacts.model.ContactDto
import com.aeliavision.androiddeck.feature.contacts.model.EmailDto
import com.aeliavision.androiddeck.feature.contacts.model.PhoneDto
import dagger.hilt.android.lifecycle.HiltViewModel
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.flow.MutableSharedFlow
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.SharedFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asSharedFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext
import javax.inject.Inject

data class EditContactUiState(
    val contact: ContactDto = ContactDto(),
    val pendingPhotoBytes: ByteArray? = null,
    val isLoading: Boolean = false,
    val isSaving: Boolean = false,
    val validationErrors: Map<String, String> = emptyMap()
)

@HiltViewModel
class EditContactViewModel @Inject constructor(
    private val repository: ContactsRepository
) : ViewModel() {

    private val _uiState = MutableStateFlow(EditContactUiState())
    val uiState: StateFlow<EditContactUiState> = _uiState.asStateFlow()

    private val _saveResult = MutableSharedFlow<Result<Unit>>()
    val saveResult: SharedFlow<Result<Unit>> = _saveResult.asSharedFlow()

    fun loadContact(id: String?) {
        if (id == null) {
            _uiState.value = EditContactUiState(contact = ContactDto())
            return
        }
        viewModelScope.launch {
            _uiState.value = _uiState.value.copy(isLoading = true)
            val contact = repository.getContactDetail(id)
            if (contact != null) {
                _uiState.value = _uiState.value.copy(contact = contact, isLoading = false)
            } else {
                _uiState.value = _uiState.value.copy(isLoading = false)
            }
        }
    }

    fun loadPendingPhotoFromUri(cr: ContentResolver, uri: Uri) {
        viewModelScope.launch {
            val bytes = withContext(Dispatchers.IO) {
                cr.openInputStream(uri)?.use { it.readBytes() }
            }
            _uiState.value = _uiState.value.copy(pendingPhotoBytes = bytes)
        }
    }

    fun saveContact() {
        viewModelScope.launch {
            _uiState.value = _uiState.value.copy(isSaving = true)
            try {
                val currentContact = _uiState.value.contact
                val savedContact = if (currentContact.id == null) {
                    repository.createContact(currentContact)
                } else {
                    repository.updateContact(currentContact)
                }
                val photoBytes = _uiState.value.pendingPhotoBytes
                if (photoBytes != null && savedContact.id != null) {
                    repository.setContactPhoto(savedContact.id, photoBytes)
                }
                
                _saveResult.emit(Result.success(Unit))
            } catch (e: Exception) {
                _saveResult.emit(Result.failure(e))
            } finally {
                _uiState.value = _uiState.value.copy(isSaving = false)
            }
        }
    }

    fun deleteContact(id: String) {
        viewModelScope.launch {
            try {
                repository.deleteContact(id)
            } catch (e: Exception) {
                _saveResult.emit(Result.failure(e))
            }
        }
    }

    fun updateField(block: (ContactDto) -> ContactDto) {
        _uiState.value = _uiState.value.copy(
            contact = block(_uiState.value.contact)
        )
    }

    fun addPhone() {
        val current = _uiState.value.contact.phones ?: emptyList()
        updateField { it.copy(phones = current + PhoneDto("", "2")) } // Default to MOBILE (2)
    }

    fun updatePhone(index: Int, phone: PhoneDto) {
        val current = _uiState.value.contact.phones?.toMutableList() ?: return
        if (index in current.indices) {
            current[index] = phone
            updateField { it.copy(phones = current) }
        }
    }

    fun removePhone(index: Int) {
        val current = _uiState.value.contact.phones?.toMutableList() ?: return
        if (index in current.indices) {
            current.removeAt(index)
            updateField { it.copy(phones = current) }
        }
    }

    fun addEmail() {
        val current = _uiState.value.contact.emails ?: emptyList()
        updateField { it.copy(emails = current + EmailDto("", "1")) } // Default to HOME (1)
    }

    fun updateEmail(index: Int, email: EmailDto) {
        val current = _uiState.value.contact.emails?.toMutableList() ?: return
        if (index in current.indices) {
            current[index] = email
            updateField { it.copy(emails = current) }
        }
    }

    fun removeEmail(index: Int) {
        val current = _uiState.value.contact.emails?.toMutableList() ?: return
        if (index in current.indices) {
            current.removeAt(index)
            updateField { it.copy(emails = current) }
        }
    }
}
