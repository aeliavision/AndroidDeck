package com.aeliavision.androiddeck.feature.contacts.ui.edit

import android.net.Uri
import android.provider.ContactsContract
import androidx.activity.compose.rememberLauncherForActivityResult
import androidx.activity.result.contract.ActivityResultContracts
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.verticalScroll
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.automirrored.filled.ArrowBack
import androidx.compose.material.icons.filled.Add
import androidx.compose.material.icons.filled.AddAPhoto
import androidx.compose.material.icons.filled.Check
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.unit.dp
import androidx.activity.compose.BackHandler
import androidx.hilt.navigation.compose.hiltViewModel
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import com.aeliavision.androiddeck.core.ui.components.ContactAvatar
import com.aeliavision.androiddeck.feature.contacts.model.EmailDto
import com.aeliavision.androiddeck.feature.contacts.model.PhoneDto
import com.aeliavision.androiddeck.feature.contacts.viewmodel.EditContactViewModel

@OptIn(ExperimentalMaterial3Api::class)
@Composable
public fun EditContactScreen(
    contactId: String?,
    onSaved: () -> Unit,
    onBack: () -> Unit,
    viewModel: EditContactViewModel = hiltViewModel()
) {

    val uiState by viewModel.uiState.collectAsStateWithLifecycle()
    val snackbarHostState = remember { SnackbarHostState() }
    val context = LocalContext.current

    var initialContact by remember(contactId) { mutableStateOf(uiState.contact) }
    var showDiscardDialog by remember { mutableStateOf(false) }

    LaunchedEffect(contactId, uiState.isLoading) {
        if (!uiState.isLoading) {
            initialContact = uiState.contact
        }
    }

    val isDirty = uiState.contact != initialContact || uiState.pendingPhotoBytes != null

    val requestBack: () -> Unit = {
        if (!uiState.isSaving) {
            if (isDirty) showDiscardDialog = true else onBack()
        }
    }

    BackHandler(enabled = !uiState.isSaving) {
        requestBack()
    }

    val photoPickerLauncher = rememberLauncherForActivityResult(
        contract = ActivityResultContracts.GetContent()
    ) { uri: Uri? ->
        if (uri != null) {
            val cr = context.contentResolver
            viewModel.loadPendingPhotoFromUri(cr, uri)
        }
    }

    LaunchedEffect(contactId) { viewModel.loadContact(contactId) }
    LaunchedEffect(Unit) {
        viewModel.saveResult.collect { result ->
            if (result.isSuccess) onSaved()
            else snackbarHostState.showSnackbar(result.exceptionOrNull()?.message ?: "Save failed")
        }
    }

    Scaffold(
        topBar = {
            TopAppBar(
                title = { Text(if (contactId == null) "New Contact" else "Edit Contact") },
                navigationIcon = {
                    IconButton(onClick = requestBack) {
                        Icon(Icons.AutoMirrored.Filled.ArrowBack, "Back")
                    }
                },
                actions = {
                    IconButton(
                        onClick = { viewModel.saveContact() },
                        enabled = !uiState.isSaving && !uiState.contact.readOnly
                    ) {
                        if (uiState.isSaving)
                            CircularProgressIndicator(modifier = Modifier.size(20.dp))
                        else
                            Icon(Icons.Default.Check, "Save")
                    }
                }
            )
        },
        snackbarHost = { SnackbarHost(snackbarHostState) }
    ) { padding ->
        if (showDiscardDialog) {
            AlertDialog(
                onDismissRequest = { showDiscardDialog = false },
                title = { Text("Discard changes?") },
                text = { Text("You have unsaved changes.") },
                confirmButton = {
                    TextButton(onClick = {
                        showDiscardDialog = false
                        onBack()
                    }) { Text("Discard") }
                },
                dismissButton = {
                    TextButton(onClick = { showDiscardDialog = false }) { Text("Keep editing") }
                }
            )
        }

        Column(
            modifier = Modifier
                .fillMaxSize()
                .padding(padding)
                .verticalScroll(rememberScrollState())
                .padding(16.dp),
            verticalArrangement = Arrangement.spacedBy(12.dp)
        ) {
            if (uiState.contact.readOnly) {
                Card(
                    colors = CardDefaults.cardColors(containerColor = MaterialTheme.colorScheme.errorContainer),
                    modifier = Modifier.fillMaxWidth()
                ) {
                    Text(
                        text = "This contact account is read-only (${uiState.contact.accountName ?: "System"}) and cannot be modified.",
                        style = MaterialTheme.typography.bodyMedium,
                        color = MaterialTheme.colorScheme.onErrorContainer,
                        modifier = Modifier.padding(12.dp)
                    )
                }
            }
            if (uiState.isSaving) {
                LinearProgressIndicator(modifier = Modifier.fillMaxWidth())
            }

            val contact = uiState.contact
            val errors = uiState.validationErrors
            Box(
                modifier = Modifier.fillMaxWidth(),
                contentAlignment = Alignment.Center
            ) {
                val photoUri = contact.id?.let { id ->
                    Uri.withAppendedPath(
                        Uri.withAppendedPath(ContactsContract.Contacts.CONTENT_URI, id),
                        ContactsContract.Contacts.Photo.CONTENT_DIRECTORY
                    )
                }
                ContactAvatar(
                    name = contact.fullName.ifBlank {
                        listOfNotNull(contact.firstName, contact.lastName).joinToString(" ")
                    },
                    size = 88.dp,
                    photoUri = if (uiState.pendingPhotoBytes == null) photoUri else null,
                    photoBytes = uiState.pendingPhotoBytes,
                    modifier = Modifier.clickable {
                        photoPickerLauncher.launch("image/*")
                    }
                )
            }
            TextButton(
                onClick = { photoPickerLauncher.launch("image/*") },
                modifier = Modifier.fillMaxWidth()
            ) {
                Icon(Icons.Default.AddAPhoto, null, modifier = Modifier.size(18.dp))
                Spacer(Modifier.width(4.dp))
                Text(if (uiState.pendingPhotoBytes != null) "Change Photo" else "Add Photo")
            }

            HorizontalDivider(modifier = Modifier.padding(vertical = 4.dp))

            SectionLabel("Name")
            OutlinedTextField(
                value = contact.firstName ?: "",
                onValueChange = { viewModel.updateField { c -> c.copy(firstName = it) } },
                label = { Text("First Name") },
                isError = errors.containsKey("name"),
                modifier = Modifier.fillMaxWidth()
            )
            OutlinedTextField(
                value = contact.middleName ?: "",
                onValueChange = { viewModel.updateField { c -> c.copy(middleName = it) } },
                label = { Text("Middle Name") },
                modifier = Modifier.fillMaxWidth()
            )
            OutlinedTextField(
                value = contact.lastName ?: "",
                onValueChange = { viewModel.updateField { c -> c.copy(lastName = it) } },
                label = { Text("Last Name") },
                isError = errors.containsKey("name"),
                supportingText = errors["name"]?.let { { Text(it) } },
                modifier = Modifier.fillMaxWidth()
            )
            Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                OutlinedTextField(
                    value = contact.prefix ?: "",
                    onValueChange = { viewModel.updateField { c -> c.copy(prefix = it) } },
                    label = { Text("Prefix") },
                    modifier = Modifier.weight(1f)
                )
                OutlinedTextField(
                    value = contact.suffix ?: "",
                    onValueChange = { viewModel.updateField { c -> c.copy(suffix = it) } },
                    label = { Text("Suffix") },
                    modifier = Modifier.weight(1f)
                )
            }

            HorizontalDivider(modifier = Modifier.padding(vertical = 4.dp))

            SectionLabel("Organisation")
            OutlinedTextField(
                value = contact.organization ?: "",
                onValueChange = { viewModel.updateField { c -> c.copy(organization = it) } },
                label = { Text("Company") },
                modifier = Modifier.fillMaxWidth()
            )
            OutlinedTextField(
                value = contact.title ?: "",
                onValueChange = { viewModel.updateField { c -> c.copy(title = it) } },
                label = { Text("Job Title") },
                modifier = Modifier.fillMaxWidth()
            )

            HorizontalDivider(modifier = Modifier.padding(vertical = 4.dp))

            SectionLabel("Phone Numbers")
            contact.phones?.forEachIndexed { index, phone ->
                PhoneFieldRow(
                    phone = phone,
                    error = errors["phone_$index"],
                    onUpdate = { viewModel.updatePhone(index, it) },
                    onRemove = { viewModel.removePhone(index) }
                )
            }
            TextButton(
                onClick = { viewModel.addPhone() },
                modifier = Modifier.fillMaxWidth()
            ) {
                Icon(Icons.Default.Add, null, modifier = Modifier.size(18.dp))
                Spacer(Modifier.width(4.dp))
                Text("Add Phone Number")
            }

            HorizontalDivider(modifier = Modifier.padding(vertical = 4.dp))

            SectionLabel("Email Addresses")
            contact.emails?.forEachIndexed { index, email ->
                EmailFieldRow(
                    email = email,
                    error = errors["email_$index"],
                    onUpdate = { viewModel.updateEmail(index, it) },
                    onRemove = { viewModel.removeEmail(index) }
                )
            }
            TextButton(
                onClick = { viewModel.addEmail() },
                modifier = Modifier.fillMaxWidth()
            ) {
                Icon(Icons.Default.Add, null, modifier = Modifier.size(18.dp))
                Spacer(Modifier.width(4.dp))
                Text("Add Email Address")
            }
        }
    }
}

@Composable
private fun SectionLabel(text: String) {
    Text(
        text = text,
        style = MaterialTheme.typography.labelLarge,
        color = MaterialTheme.colorScheme.primary
    )
}
