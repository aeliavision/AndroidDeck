package com.aeliavision.androiddeck.feature.server.ui

import android.Manifest
import android.content.pm.PackageManager
import android.os.Build
import androidx.activity.compose.rememberLauncherForActivityResult
import androidx.activity.result.contract.ActivityResultContracts
import androidx.compose.animation.*
import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.verticalScroll
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.outlined.*
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Brush
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.platform.LocalClipboardManager
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.text.AnnotatedString
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.core.content.ContextCompat
import androidx.hilt.lifecycle.viewmodel.compose.hiltViewModel
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import com.aeliavision.androiddeck.feature.server.viewmodel.ServerViewModel
import kotlinx.coroutines.launch
import android.os.Environment
import com.aeliavision.androiddeck.core.ui.components.AppCard
import com.aeliavision.androiddeck.core.ui.components.SettingToggle

@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun ServerScreen(
    viewModel: ServerViewModel = hiltViewModel()
) {

    val context = LocalContext.current
    val uiState by viewModel.uiState.collectAsStateWithLifecycle()
    val snackbarHostState = remember { SnackbarHostState() }
    val scope = rememberCoroutineScope()
    val mediaPermLauncher = rememberLauncherForActivityResult(
        ActivityResultContracts.RequestMultiplePermissions()
    ) { perms ->
        if (perms.values.any { !it }) {
            scope.launch { snackbarHostState.showSnackbar("Gallery permission denied") }
        }
        viewModel.startServer(context)
    }

    val contactsPermLauncher = rememberLauncherForActivityResult(
        ActivityResultContracts.RequestMultiplePermissions()
    ) { perms ->
        if (!perms.values.all { it }) {
            scope.launch { snackbarHostState.showSnackbar("Contacts permission required") }
            return@rememberLauncherForActivityResult
        }
        val mediaPerms = if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.TIRAMISU) {
            listOf(Manifest.permission.READ_MEDIA_IMAGES, Manifest.permission.READ_MEDIA_VIDEO)
        } else {
            listOf(Manifest.permission.READ_EXTERNAL_STORAGE)
        }.filter { ContextCompat.checkSelfPermission(context, it) != PackageManager.PERMISSION_GRANTED }
        if (mediaPerms.isEmpty()) viewModel.startServer(context)
        else mediaPermLauncher.launch(mediaPerms.toTypedArray())
    }

    val notifPermLauncher = rememberLauncherForActivityResult(
        ActivityResultContracts.RequestPermission()
    ) {
        val needed = listOf(Manifest.permission.READ_CONTACTS, Manifest.permission.WRITE_CONTACTS)
            .filter { ContextCompat.checkSelfPermission(context, it) != PackageManager.PERMISSION_GRANTED }
        if (needed.isEmpty()) {
            val mediaPerms = if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.TIRAMISU) {
                listOf(Manifest.permission.READ_MEDIA_IMAGES, Manifest.permission.READ_MEDIA_VIDEO)
            } else {
                listOf(Manifest.permission.READ_EXTERNAL_STORAGE)
            }.filter { ContextCompat.checkSelfPermission(context, it) != PackageManager.PERMISSION_GRANTED }
            if (mediaPerms.isEmpty()) viewModel.startServer(context)
            else mediaPermLauncher.launch(mediaPerms.toTypedArray())
        } else {
            contactsPermLauncher.launch(needed.toTypedArray())
        }
    }

    DisposableEffect(Unit) {
        viewModel.bind(context)
        onDispose { viewModel.unbind(context) }
    }


    Scaffold(
        topBar = {
            TopAppBar(
                title = {},
                navigationIcon = {
                    IconButton(onClick = {}) {
                        Icon(Icons.Outlined.AccountCircle, "User", tint = MaterialTheme.colorScheme.primary, modifier = Modifier.size(32.dp))
                    }
                },
                actions = {
                    IconButton(onClick = {}) {
                        Icon(Icons.Outlined.Settings, "Settings", tint = MaterialTheme.colorScheme.primary, modifier = Modifier.size(28.dp))
                    }
                },
                colors = TopAppBarDefaults.topAppBarColors(containerColor = Color.Transparent)
            )
        },
        snackbarHost = { SnackbarHost(snackbarHostState) },
        containerColor = MaterialTheme.colorScheme.surface
    ) { padding ->
        Column(
            modifier = Modifier
                .fillMaxSize()
                .padding(padding)
                .verticalScroll(rememberScrollState())
                .padding(horizontal = 24.dp),
            verticalArrangement = Arrangement.spacedBy(24.dp)
        ) {
            Text(
                text = "Sync Dashboard",
                style = MaterialTheme.typography.displaySmall,
                fontWeight = FontWeight.Bold,
                color = MaterialTheme.colorScheme.onSurface
            )
            val notificationsGranted = Build.VERSION.SDK_INT < 33 ||
                ContextCompat.checkSelfPermission(context, Manifest.permission.POST_NOTIFICATIONS) ==
                PackageManager.PERMISSION_GRANTED

            val contactsGranted = listOf(
                Manifest.permission.READ_CONTACTS,
                Manifest.permission.WRITE_CONTACTS
            ).all { ContextCompat.checkSelfPermission(context, it) == PackageManager.PERMISSION_GRANTED }

            val mediaGranted = if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.TIRAMISU) {
                listOf(Manifest.permission.READ_MEDIA_IMAGES, Manifest.permission.READ_MEDIA_VIDEO)
            } else {
                listOf(Manifest.permission.READ_EXTERNAL_STORAGE)
            }.all { ContextCompat.checkSelfPermission(context, it) == PackageManager.PERMISSION_GRANTED }

            val filesGranted = if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.R) {
                Environment.isExternalStorageManager()
            } else {
                ContextCompat.checkSelfPermission(context, Manifest.permission.READ_EXTERNAL_STORAGE) ==
                    PackageManager.PERMISSION_GRANTED
            }

            AppCard(
                containerColor = MaterialTheme.colorScheme.primaryContainer.copy(alpha = 0.6f),
                modifier = Modifier.fillMaxWidth()
            ) {
                Column(verticalArrangement = Arrangement.spacedBy(16.dp)) {
                    Text(
                        "Permissions & Compliance",
                        style = MaterialTheme.typography.titleMedium,
                        fontWeight = FontWeight.Bold,
                        color = MaterialTheme.colorScheme.onPrimaryContainer
                    )

                    PermissionItem(title = "Service Status", granted = notificationsGranted)
                    PermissionItem(title = "Contacts", granted = contactsGranted)
                    PermissionItem(title = "Gallery Media", granted = mediaGranted)
                    PermissionItem(title = "File Browser", granted = filesGranted)
                }
            }
            Box(
                modifier = Modifier
                    .fillMaxWidth()
                    .clip(MaterialTheme.shapes.extraLarge)
                    .border(
                        width = 2.dp,
                        brush = Brush.linearGradient(
                            colors = listOf(MaterialTheme.colorScheme.primary, MaterialTheme.colorScheme.primary.copy(alpha = 0.3f))
                        ),
                        shape = MaterialTheme.shapes.extraLarge
                    )
                    .background(MaterialTheme.colorScheme.surfaceVariant.copy(alpha = 0.8f))
                    .padding(32.dp),
                contentAlignment = Alignment.Center
            ) {
                Column(
                    horizontalAlignment = Alignment.CenterHorizontally,
                    verticalArrangement = Arrangement.spacedBy(16.dp)
                ) {
                    Text(
                        "Server Status",
                        style = MaterialTheme.typography.titleSmall,
                        color = MaterialTheme.colorScheme.onSurfaceVariant.copy(alpha = 0.8f),
                        fontWeight = FontWeight.SemiBold
                    )

                    SettingToggle(
                        checked = uiState.isRunning,
                        onCheckedChange = { shouldRun ->
                            if (shouldRun) {
                                if (Build.VERSION.SDK_INT >= 33 && ContextCompat.checkSelfPermission(context, Manifest.permission.POST_NOTIFICATIONS) != PackageManager.PERMISSION_GRANTED) {
                                    notifPermLauncher.launch(Manifest.permission.POST_NOTIFICATIONS)
                                } else {
                                    val neededContacts = listOf(Manifest.permission.READ_CONTACTS, Manifest.permission.WRITE_CONTACTS)
                                        .filter { ContextCompat.checkSelfPermission(context, it) != PackageManager.PERMISSION_GRANTED }
                                    if (neededContacts.isNotEmpty()) contactsPermLauncher.launch(neededContacts.toTypedArray())
                                    else viewModel.startServer(context)
                                }
                            } else {
                                viewModel.stopServer(context)
                            }
                        }
                    )

                    Text(
                        text = if (uiState.isRunning) "Server is Online" else "Server is Offline",
                        style = MaterialTheme.typography.headlineSmall,
                        fontWeight = FontWeight.Bold,
                        color = MaterialTheme.colorScheme.onSurface
                    )

                    Text(
                        text = if (uiState.isRunning) "Ready to synchronize with desktop at ${uiState.ipAddress}:${uiState.port}"
                               else "Ready to connect to your desktop",
                        style = MaterialTheme.typography.bodyMedium,
                        color = MaterialTheme.colorScheme.onSurface.copy(alpha = 0.6f),
                        textAlign = TextAlign.Center
                    )
                    AnimatedVisibility(
                        visible = uiState.isRunning,
                        enter = expandVertically() + fadeIn(),
                        exit = shrinkVertically() + fadeOut()
                    ) {
                        Column(
                            horizontalAlignment = Alignment.CenterHorizontally,
                            verticalArrangement = Arrangement.spacedBy(12.dp),
                            modifier = Modifier.padding(top = 16.dp)
                        ) {
                            Text(
                                "PASS KEY / PAIRING CODE",
                                style = MaterialTheme.typography.labelMedium,
                                color = MaterialTheme.colorScheme.primary,
                                fontWeight = FontWeight.Bold,
                                letterSpacing = 1.sp
                            )

                            Row(
                                verticalAlignment = Alignment.CenterVertically,
                                horizontalArrangement = Arrangement.spacedBy(8.dp),
                                modifier = Modifier
                                    .clip(RoundedCornerShape(16.dp))
                                    .background(MaterialTheme.colorScheme.onSurface.copy(alpha = 0.05f))
                                    .border(1.dp, MaterialTheme.colorScheme.primary.copy(alpha = 0.2f), RoundedCornerShape(16.dp))
                                    .padding(horizontal = 20.dp, vertical = 12.dp)
                            ) {
                                Text(
                                    text = uiState.pairingCode.chunked(3).joinToString(" "),
                                    style = MaterialTheme.typography.headlineMedium.copy(
                                        letterSpacing = 4.sp,
                                        fontFamily = androidx.compose.ui.text.font.FontFamily.Monospace
                                    ),
                                    fontWeight = FontWeight.ExtraBold,
                                    color = MaterialTheme.colorScheme.onSurface
                                )

                                val clipboardManager = LocalClipboardManager.current
                                IconButton(onClick = {
                                    clipboardManager.setText(AnnotatedString(uiState.pairingCode))
                                }) {
                                    Icon(
                                        Icons.Outlined.ContentCopy,
                                        "Copy",
                                        tint = MaterialTheme.colorScheme.primary.copy(alpha = 0.7f),
                                        modifier = Modifier.size(20.dp)
                                    )
                                }

                                IconButton(onClick = { viewModel.regeneratePairingCode() }) {
                                    Icon(
                                        Icons.Outlined.Refresh,
                                        "Regenerate",
                                        tint = MaterialTheme.colorScheme.primary.copy(alpha = 0.7f),
                                        modifier = Modifier.size(20.dp)
                                    )
                                }
                            }

                            Text(
                                "Enter this code on your desktop to pair",
                                style = MaterialTheme.typography.bodySmall,
                                color = MaterialTheme.colorScheme.onSurface.copy(alpha = 0.4f)
                            )
                        }
                    }
                }
            }
        }
    }
}

@Composable
private fun PermissionItem(title: String, granted: Boolean) {
    Row(
        modifier = Modifier.fillMaxWidth(),
        verticalAlignment = Alignment.CenterVertically
    ) {
        Box(
            modifier = Modifier
                .size(24.dp)
                .border(1.dp, MaterialTheme.colorScheme.primary.copy(alpha = 0.5f), CircleShape),
            contentAlignment = Alignment.Center
        ) {
            if (granted) {
                Icon(
                    Icons.Outlined.Check,
                    null,
                    modifier = Modifier.size(16.dp),
                    tint = MaterialTheme.colorScheme.primary
                )
            }
        }
        Spacer(Modifier.width(16.dp))
        Column {
            Text(title, color = MaterialTheme.colorScheme.onSurface, style = MaterialTheme.typography.bodyLarge, fontWeight = FontWeight.Medium)
            Text(if (granted) "Granted" else "Required", color = MaterialTheme.colorScheme.onSurface.copy(alpha = 0.5f), style = MaterialTheme.typography.bodySmall)
        }
    }
}
