package com.aeliavision.androiddeck.feature.contacts.ui.detail

import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.verticalScroll
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.automirrored.filled.ArrowBack
import androidx.compose.material.icons.filled.Call
import androidx.compose.material.icons.filled.Delete
import androidx.compose.material.icons.filled.Edit
import androidx.compose.material.icons.filled.Email
import androidx.compose.material3.AssistChip
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.FilledTonalButton
import androidx.compose.material3.HorizontalDivider
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Scaffold
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.material3.TopAppBar
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Brush
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.core.content.ContextCompat
import androidx.hilt.navigation.compose.hiltViewModel
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import android.net.Uri
import android.provider.ContactsContract
import com.aeliavision.androiddeck.core.ui.components.ContactAvatar
import com.aeliavision.androiddeck.core.ui.components.LoadingIndicator
import com.aeliavision.androiddeck.core.ui.components.AppCard
import com.aeliavision.androiddeck.core.util.dialPhone
import com.aeliavision.androiddeck.core.util.sendEmail
import com.aeliavision.androiddeck.feature.contacts.viewmodel.EditContactViewModel
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.semantics
import com.aeliavision.androiddeck.feature.contacts.ui.shared.emailTypeLabel
import com.aeliavision.androiddeck.feature.contacts.ui.shared.phoneTypeLabel

@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun ContactDetailScreen(
    contactId: String,
    onEdit: () -> Unit,
    onBack: () -> Unit,
    viewModel: EditContactViewModel = hiltViewModel()
) {
    val uiState by viewModel.uiState.collectAsStateWithLifecycle()
    val context = LocalContext.current
    var showDeleteConfirm by remember { mutableStateOf(false) }

    LaunchedEffect(contactId) { viewModel.loadContact(contactId) }

    Scaffold(
        topBar = {
            TopAppBar(
                title = { Text(uiState.contact.fullName.ifBlank { "Contact" }) },
                navigationIcon = {
                    IconButton(onClick = onBack) {
                        Icon(Icons.AutoMirrored.Filled.ArrowBack, contentDescription = "Back")
                    }
                },
                actions = {
                    if (!uiState.contact.readOnly) {
                        IconButton(onClick = onEdit) {
                            Icon(Icons.Default.Edit, contentDescription = "Edit contact")
                        }
                        IconButton(onClick = { showDeleteConfirm = true }) {
                            Icon(Icons.Default.Delete, contentDescription = "Delete contact")
                        }
                    }
                }
            )
        }
    ) { padding ->
        if (uiState.isLoading) {
            LoadingIndicator()
            return@Scaffold
        }

        val contact = uiState.contact

        if (showDeleteConfirm) {
            androidx.compose.material3.AlertDialog(
                onDismissRequest = { showDeleteConfirm = false },
                title = { Text("Delete contact?") },
                text = { Text("This action can't be undone.") },
                confirmButton = {
                    androidx.compose.material3.TextButton(onClick = {
                        showDeleteConfirm = false
                        contact.id?.let { viewModel.deleteContact(it) }
                        onBack()
                    }) {
                        Text("Delete")
                    }
                },
                dismissButton = {
                    androidx.compose.material3.TextButton(onClick = { showDeleteConfirm = false }) {
                        Text("Cancel")
                    }
                }
            )
        }

        Column(
            modifier = Modifier
                .fillMaxSize()
                .padding(padding)
                .verticalScroll(rememberScrollState())
        ) {
            Box(
                modifier = Modifier
                    .fillMaxWidth()
                    .background(
                        brush = Brush.verticalGradient(
                            colors = listOf(
                                MaterialTheme.colorScheme.primary.copy(alpha = 0.05f),
                                Color.Transparent
                            )
                        )
                    )
                    .padding(vertical = 40.dp),
                contentAlignment = Alignment.Center
            ) {
                val photoUri = contact.id?.let { id ->
                    Uri.withAppendedPath(
                        Uri.withAppendedPath(ContactsContract.Contacts.CONTENT_URI, id),
                        ContactsContract.Contacts.Photo.CONTENT_DIRECTORY
                    )
                }
                ContactAvatar(name = contact.fullName, size = 120.dp, photoUri = photoUri)
            }

            Column(
                modifier = Modifier
                    .fillMaxWidth()
                    .padding(horizontal = 24.dp),
                horizontalAlignment = Alignment.CenterHorizontally
            ) {
                Text(
                    text = contact.fullName.ifBlank { "Unknown" },
                    style = MaterialTheme.typography.headlineLarge,
                    fontWeight = FontWeight.Bold,
                    textAlign = TextAlign.Center
                )
            }

            if (!contact.organization.isNullOrBlank()) {
                Spacer(Modifier.height(4.dp))
                Text(
                    text = buildString {
                        append(contact.organization)
                        if (!contact.title.isNullOrBlank()) append(" • ${contact.title}")
                    },
                    style = MaterialTheme.typography.titleMedium,
                    color = MaterialTheme.colorScheme.primary,
                    fontWeight = FontWeight.Medium,
                    textAlign = TextAlign.Center
                )
            }

            if (contact.readOnly) {
                Spacer(Modifier.height(8.dp))
                Surface(
                    color = MaterialTheme.colorScheme.surfaceVariant,
                    shape = CircleShape
                ) {
                    Text(
                        "READ ONLY",
                        modifier = Modifier.padding(horizontal = 12.dp, vertical = 4.dp),
                        style = MaterialTheme.typography.labelSmall,
                        fontWeight = FontWeight.Bold,
                        color = MaterialTheme.colorScheme.onSurfaceVariant
                    )
                }
            }

            Spacer(Modifier.height(32.dp))
            HorizontalDivider(color = MaterialTheme.colorScheme.outlineVariant)
            Spacer(Modifier.height(24.dp))
            SectionHeader("Contact Methods")
            contact.phones?.forEach { phone ->
                DetailRow(
                    value = phone.value.orEmpty(),
                    label = phoneTypeLabel(type = phone.type, label = phone.label),
                    icon = Icons.Default.Call,
                    onAction = { context.dialPhone(it) },
                    actionLabel = "Call"
                )
            }

            // Emails
            contact.emails?.forEach { email ->
                DetailRow(
                    value = email.value.orEmpty(),
                    label = emailTypeLabel(type = email.type),
                    icon = Icons.Default.Email,
                    onAction = { context.sendEmail(it) },
                    actionLabel = "Email"
                )
            }

            Spacer(Modifier.height(40.dp))
        }
    }
}

@Composable
private fun SectionHeader(title: String) {
    Text(
        text = title.uppercase(),
        style = MaterialTheme.typography.labelMedium,
        fontWeight = FontWeight.Bold,
        color = MaterialTheme.colorScheme.onSurfaceVariant.copy(alpha = 0.6f),
        letterSpacing = 2.sp,
        modifier = Modifier.fillMaxWidth().padding(bottom = 16.dp)
    )
}

@Composable
private fun DetailRow(
    value: String,
    label: String,
    icon: androidx.compose.ui.graphics.vector.ImageVector,
    onAction: (String) -> Unit,
    actionLabel: String
) {
    if (value.isBlank()) return

    AppCard(
        modifier = Modifier.padding(vertical = 8.dp),
        containerColor = MaterialTheme.colorScheme.surfaceVariant.copy(alpha = 0.1f)
    ) {
        Row(
            modifier = Modifier.fillMaxWidth(),
            verticalAlignment = Alignment.CenterVertically,
            horizontalArrangement = Arrangement.SpaceBetween
        ) {
            Column(modifier = Modifier.weight(1f)) {
                Text(value, style = MaterialTheme.typography.bodyLarge, fontWeight = FontWeight.Medium)
                Text(label, style = MaterialTheme.typography.labelSmall, color = MaterialTheme.colorScheme.onSurfaceVariant)
            }
            FilledTonalButton(
                onClick = { onAction(value) },
                shape = CircleShape,
                contentPadding = PaddingValues(horizontal = 16.dp, vertical = 8.dp)
            ) {
                Icon(icon, null, Modifier.size(16.dp))
                Spacer(Modifier.width(8.dp))
                Text(actionLabel, style = MaterialTheme.typography.labelMedium)
            }
        }
    }
}
