package com.aeliavision.androiddeck.feature.settings.ui

import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.foundation.verticalScroll
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.outlined.Brightness6
import androidx.compose.material.icons.outlined.Delete
import androidx.compose.material.icons.outlined.Info
import androidx.compose.material.icons.outlined.LinkOff
import androidx.compose.material.icons.outlined.Numbers
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.vector.ImageVector
import androidx.compose.ui.text.input.KeyboardType
import androidx.compose.ui.unit.dp
import androidx.hilt.lifecycle.viewmodel.compose.hiltViewModel
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import com.aeliavision.androiddeck.BuildConfig
import com.aeliavision.androiddeck.feature.settings.viewmodel.SettingsViewModel

@OptIn(ExperimentalMaterial3Api::class)
@Composable
public fun SettingsScreen(
    viewModel: SettingsViewModel = hiltViewModel()
) {
    val uiState by viewModel.uiState.collectAsStateWithLifecycle()
    var showThemeDialog by remember { mutableStateOf(false) }
    var showPortDialog by remember { mutableStateOf(false) }
    var showSessionDialog by remember { mutableStateOf(false) }

    Scaffold(
        topBar = {
            TopAppBar(title = { Text("Settings") })
        }
    ) { padding ->
        Column(
            modifier = Modifier
                .fillMaxSize()
                .padding(padding)
                .verticalScroll(rememberScrollState())
        ) {
            SettingsCategory(title = "Appearance")

            SettingsItem(
                icon = Icons.Outlined.Brightness6,
                title = "Theme",
                subtitle = when(uiState.darkMode) {
                    "light" -> "Light"
                    "dark" -> "Dark"
                    else -> "System default"
                },
                onClick = { showThemeDialog = true }
            )

            HorizontalDivider()
            SettingsCategory(title = "Connection")

            SettingsItem(
                icon = Icons.Outlined.Numbers,
                title = "Server Port",
                subtitle = "${uiState.serverPort} (tap to edit)",
                onClick = { showPortDialog = true }
            )

            val sessionSubtitle = if (uiState.activeSessions == 1) {
                "1 active desktop session"
            } else {
                "${uiState.activeSessions} active desktop sessions"
            }

            SettingsItem(
                icon = Icons.Outlined.LinkOff,
                title = "Sessions",
                subtitle = sessionSubtitle,
                onClick = {
                    viewModel.refreshSessionsList()
                    showSessionDialog = true
                }
            )

            HorizontalDivider()
            SettingsCategory(title = "About")

            SettingsItem(
                icon = Icons.Outlined.Info,
                title = "Version",
                subtitle = BuildConfig.VERSION_NAME,
                onClick = null
            )
        }
    }

    if (showThemeDialog) {
        ThemeSelectionDialog(
            currentMode = uiState.darkMode,
            onDismiss = { showThemeDialog = false },
            onSelect = {
                viewModel.setDarkMode(it)
                showThemeDialog = false
            }
        )
    }

    if (showPortDialog) {
        ServerPortDialog(
            currentPort = uiState.serverPort,
            portError = uiState.portError,
            onDismiss = {
                viewModel.clearPortError()
                showPortDialog = false
            },
            onSave = { portInput ->
                if (viewModel.setServerPort(portInput)) {
                    showPortDialog = false
                }
            }
        )
    }

    if (showSessionDialog) {
        SessionsDialog(
            sessions = uiState.sessionsList,
            onDismiss = { showSessionDialog = false },
            onRevokeSession = { clientId -> viewModel.revokeSession(clientId) },
            onRevokeAll = {
                viewModel.clearSessions()
                showSessionDialog = false
            }
        )
    }
}

@Composable
public fun SettingsCategory(title: String) {
    Text(
        text = title,
        style = MaterialTheme.typography.labelLarge,
        color = MaterialTheme.colorScheme.primary,
        modifier = Modifier.padding(start = 16.dp, top = 24.dp, bottom = 8.dp)
    )
}

@Composable
public fun SettingsItem(
    icon: ImageVector,
    title: String,
    subtitle: String,
    onClick: (() -> Unit)? = null
) {
    ListItem(
        headlineContent = { Text(title) },
        supportingContent = { Text(subtitle) },
        leadingContent = { Icon(icon, contentDescription = null) },
        modifier = if (onClick != null) Modifier.clickable(onClick = onClick) else Modifier
    )
}

@Composable
public fun ServerPortDialog(
    currentPort: Int,
    portError: String?,
    onDismiss: () -> Unit,
    onSave: (String) -> Unit
) {
    var portText by remember { mutableStateOf(currentPort.toString()) }

    AlertDialog(
        onDismissRequest = onDismiss,
        title = { Text("Server Port") },
        text = {
            Column {
                OutlinedTextField(
                    value = portText,
                    onValueChange = { portText = it },
                    label = { Text("Port number (1024-65535)") },
                    singleLine = true,
                    isError = portError != null,
                    supportingText = portError?.let { { Text(it, color = MaterialTheme.colorScheme.error) } },
                    keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Number)
                )
                Spacer(Modifier.height(8.dp))
                Text(
                    text = "Note: Changing the server port requires restarting the server service.",
                    style = MaterialTheme.typography.bodySmall,
                    color = MaterialTheme.colorScheme.onSurfaceVariant
                )
            }
        },
        confirmButton = {
            TextButton(onClick = { onSave(portText) }) {
                Text("Save")
            }
        },
        dismissButton = {
            TextButton(onClick = onDismiss) {
                Text("Cancel")
            }
        }
    )
}

@Composable
public fun SessionsDialog(
    sessions: List<com.aeliavision.androiddeck.feature.server.service.AuthManager.SessionSummary>,
    onDismiss: () -> Unit,
    onRevokeSession: (String) -> Unit,
    onRevokeAll: () -> Unit
) {
    AlertDialog(
        onDismissRequest = onDismiss,
        title = { Text("Active Desktop Sessions") },
        text = {
            Column(
                modifier = Modifier.fillMaxWidth(),
                verticalArrangement = Arrangement.spacedBy(8.dp)
            ) {
                if (sessions.isEmpty()) {
                    Text(
                        text = "No active paired desktop sessions.",
                        style = MaterialTheme.typography.bodyMedium
                    )
                } else {
                    sessions.forEach { session ->
                        Row(
                            modifier = Modifier.fillMaxWidth(),
                            horizontalArrangement = Arrangement.SpaceBetween,
                            verticalAlignment = Alignment.CenterVertically
                        ) {
                            Column(modifier = Modifier.weight(1f)) {
                                Text(
                                    text = session.clientId,
                                    style = MaterialTheme.typography.bodyLarge
                                )
                                Text(
                                    text = "ID: ${session.sessionId.take(8)}...",
                                    style = MaterialTheme.typography.bodySmall,
                                    color = MaterialTheme.colorScheme.onSurfaceVariant
                                )
                            }
                            IconButton(onClick = { onRevokeSession(session.clientId) }) {
                                Icon(
                                    imageVector = Icons.Outlined.Delete,
                                    contentDescription = "Revoke session",
                                    tint = MaterialTheme.colorScheme.error
                                )
                            }
                        }
                    }
                }
            }
        },
        confirmButton = {
            if (sessions.isNotEmpty()) {
                TextButton(onClick = onRevokeAll) {
                    Text("Revoke All", color = MaterialTheme.colorScheme.error)
                }
            }
        },
        dismissButton = {
            TextButton(onClick = onDismiss) {
                Text("Close")
            }
        }
    )
}

@Composable
public fun ThemeSelectionDialog(
    currentMode: String,
    onDismiss: () -> Unit,
    onSelect: (String) -> Unit
) {
    AlertDialog(
        onDismissRequest = onDismiss,
        title = { Text("Choose Theme") },
        text = {
            Column {
                ThemeOption("system", "System default", currentMode, onSelect)
                ThemeOption("light", "Light", currentMode, onSelect)
                ThemeOption("dark", "Dark", currentMode, onSelect)
            }
        },
        confirmButton = {
            TextButton(onClick = onDismiss) {
                Text("Cancel")
            }
        }
    )
}

@Composable
public fun ThemeOption(
    mode: String,
    label: String,
    currentMode: String,
    onSelect: (String) -> Unit
) {
    Row(
        modifier = Modifier
            .fillMaxWidth()
            .clickable { onSelect(mode) }
            .padding(vertical = 8.dp),
        verticalAlignment = Alignment.CenterVertically
    ) {
        RadioButton(selected = mode == currentMode, onClick = { onSelect(mode) })
        Spacer(modifier = Modifier.width(8.dp))
        Text(label)
    }
}


