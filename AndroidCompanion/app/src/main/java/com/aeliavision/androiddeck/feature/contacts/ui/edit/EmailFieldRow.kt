package com.aeliavision.androiddeck.feature.contacts.ui.edit

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.Remove
import androidx.compose.material3.DropdownMenuItem
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.ExposedDropdownMenuBox
import androidx.compose.material3.ExposedDropdownMenuDefaults
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.MenuAnchorType
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.unit.dp
import com.aeliavision.androiddeck.feature.contacts.model.EmailDto
import com.aeliavision.androiddeck.feature.contacts.ui.shared.ContactTypeLabels
import com.aeliavision.androiddeck.feature.contacts.ui.shared.emailTypeLabel

@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun EmailFieldRow(
    email: EmailDto,
    error: String?,
    onUpdate: (EmailDto) -> Unit,
    onRemove: () -> Unit
) {
    var expanded by remember { mutableStateOf(false) }
    val typeLabel = emailTypeLabel(type = email.type)


    Row(
        modifier = Modifier.fillMaxWidth(),
        horizontalArrangement = Arrangement.spacedBy(8.dp),
        verticalAlignment = Alignment.Top
    ) {
        Column(modifier = Modifier.weight(1f)) {
            OutlinedTextField(
                value = email.value,
                onValueChange = { onUpdate(email.copy(value = it)) },
                label = { Text("Email Address") },
                isError = error != null,
                supportingText = error?.let { { Text(it) } },
                modifier = Modifier.fillMaxWidth()
            )
        }
        ExposedDropdownMenuBox(
            expanded = expanded,
            onExpandedChange = { expanded = it },
            modifier = Modifier.width(110.dp)
        ) {
            OutlinedTextField(
                value = typeLabel,
                onValueChange = {},
                readOnly = true,
                label = { Text("Type") },
                trailingIcon = { ExposedDropdownMenuDefaults.TrailingIcon(expanded) },
                modifier = Modifier.menuAnchor(MenuAnchorType.PrimaryNotEditable)
            )
            ExposedDropdownMenu(expanded = expanded, onDismissRequest = { expanded = false }) {
                ContactTypeLabels.emailTypes.forEach { (typeStr, label) ->
                    DropdownMenuItem(
                        text = { Text(label) },
                        onClick = {
                            onUpdate(email.copy(type = typeStr))
                            expanded = false
                        }
                    )
                }
            }
        }
        IconButton(onClick = onRemove, modifier = Modifier.size(48.dp)) {
            Icon(Icons.Default.Remove, contentDescription = "Remove email")
        }
    }
}
