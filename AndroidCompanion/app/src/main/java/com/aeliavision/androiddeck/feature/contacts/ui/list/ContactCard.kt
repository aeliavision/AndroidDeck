package com.aeliavision.androiddeck.feature.contacts.ui.list

import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.width
import androidx.compose.material3.HorizontalDivider
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.remember
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import android.net.Uri
import android.provider.ContactsContract
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.automirrored.filled.ArrowForward
import androidx.compose.material3.Icon
import com.aeliavision.androiddeck.core.ui.components.ContactAvatar
import com.aeliavision.androiddeck.core.ui.components.AppCard
import com.aeliavision.androiddeck.feature.contacts.model.ContactDto
import androidx.compose.foundation.layout.size

@Composable
fun ContactCard(
    contact: ContactDto,
    onClick: () -> Unit,
    modifier: Modifier = Modifier
) {
    val photoUri = remember(contact.id) {
        contact.id?.let { id ->
            Uri.withAppendedPath(
                Uri.withAppendedPath(ContactsContract.Contacts.CONTENT_URI, id),
                ContactsContract.Contacts.Photo.CONTENT_DIRECTORY
            )
        }
    }

    AppCard(
        modifier = modifier
            .padding(horizontal = 16.dp, vertical = 6.dp)
            .clickable(onClick = onClick),
        containerColor = MaterialTheme.colorScheme.surfaceVariant.copy(alpha = 0.2f)
    ) {
        Row(
            modifier = Modifier.fillMaxWidth(),
            verticalAlignment = Alignment.CenterVertically
        ) {
            ContactAvatar(name = contact.fullName, size = 56.dp, photoUri = photoUri)
            Spacer(Modifier.width(16.dp))
            Column(modifier = Modifier.weight(1f)) {
                Text(
                    text = contact.fullName.ifBlank { "Unknown" },
                    style = MaterialTheme.typography.titleMedium,
                    fontWeight = FontWeight.Bold,
                    color = MaterialTheme.colorScheme.onSurface,
                    maxLines = 1,
                    overflow = TextOverflow.Ellipsis
                )

                val org = contact.organization
                if (!org.isNullOrBlank()) {
                    Text(
                        text = org,
                        style = MaterialTheme.typography.labelSmall,
                        color = MaterialTheme.colorScheme.primary,
                        fontWeight = FontWeight.SemiBold,
                        maxLines = 1,
                        overflow = TextOverflow.Ellipsis
                    )
                }

                val phone = contact.phones?.firstOrNull()?.value
                if (phone != null) {
                    Text(
                        text = phone,
                        style = MaterialTheme.typography.bodySmall,
                        color = MaterialTheme.colorScheme.onSurfaceVariant.copy(alpha = 0.8f),
                        maxLines = 1,
                        overflow = TextOverflow.Ellipsis
                    )
                }
            }
            Icon(
                imageVector = Icons.AutoMirrored.Filled.ArrowForward,
                contentDescription = null,
                tint = MaterialTheme.colorScheme.primary.copy(alpha = 0.5f),
                modifier = Modifier.size(20.dp)
            )
        }
    }
}
