package com.aeliavision.androiddeck.feature.filesystem.ui

import android.content.Intent
import android.os.Build
import android.os.Environment
import android.provider.Settings
import android.webkit.MimeTypeMap
import androidx.activity.compose.BackHandler
import androidx.activity.compose.rememberLauncherForActivityResult
import androidx.activity.result.contract.ActivityResultContracts
import androidx.core.net.toUri
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.outlined.*
import androidx.compose.material.icons.automirrored.outlined.ArrowBack
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.Button
import androidx.compose.material3.DropdownMenu
import androidx.compose.material3.DropdownMenuItem
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.FloatingActionButton
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Scaffold
import androidx.compose.material3.SnackbarHost
import androidx.compose.material3.SnackbarHostState
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.material3.TopAppBar
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.setValue
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.unit.dp
import androidx.core.content.FileProvider
import androidx.hilt.lifecycle.viewmodel.compose.hiltViewModel
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import com.aeliavision.androiddeck.core.ui.components.EmptyStateView
import com.aeliavision.androiddeck.core.ui.components.LoadingIndicator
import com.aeliavision.androiddeck.feature.filesystem.model.FileEntry
import com.aeliavision.androiddeck.feature.filesystem.ui.components.FileItem
import com.aeliavision.androiddeck.feature.filesystem.viewmodel.FilesViewModel
import kotlinx.coroutines.launch
import java.io.File

@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun FilesScreen(
    viewModel: FilesViewModel = hiltViewModel()
) {
    val uiState by viewModel.uiState.collectAsStateWithLifecycle()
    val context = LocalContext.current
    val scope = rememberCoroutineScope()
    val snackbarHostState = remember { SnackbarHostState() }

    val manageStorageLauncher = rememberLauncherForActivityResult(
        contract = ActivityResultContracts.StartActivityForResult()
    ) {
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.R) {
            viewModel.onPermissionResult(Environment.isExternalStorageManager())
        }
    }

    val legacyStorageLauncher = rememberLauncherForActivityResult(
        contract = ActivityResultContracts.RequestMultiplePermissions()
    ) { permissions ->
        val granted = permissions.values.all { it }
        viewModel.onPermissionResult(granted)
    }

    fun requestPermission() {
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.R) {
            val intent = Intent(
                Settings.ACTION_MANAGE_APP_ALL_FILES_ACCESS_PERMISSION,
                "package:${context.packageName}".toUri()
            )
            manageStorageLauncher.launch(intent)
        } else {
            legacyStorageLauncher.launch(
                arrayOf(
                    android.Manifest.permission.READ_EXTERNAL_STORAGE,
                    android.Manifest.permission.WRITE_EXTERNAL_STORAGE
                )
            )
        }
    }

    LaunchedEffect(Unit) {
        val granted = if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.R) {
            Environment.isExternalStorageManager()
        } else {
            val readGranted = context.checkSelfPermission(android.Manifest.permission.READ_EXTERNAL_STORAGE) ==
                    android.content.pm.PackageManager.PERMISSION_GRANTED
            val writeGranted = true
            
            readGranted && writeGranted
        }
        viewModel.onPermissionResult(granted)
    }

    LaunchedEffect(uiState.error) {
        uiState.error?.let {
            scope.launch { snackbarHostState.showSnackbar(it) }
            viewModel.clearError()
        }
    }

    val isAtRoot = uiState.currentPath == "/storage/emulated/0"
    BackHandler(enabled = !isAtRoot) {
        viewModel.navigateUp()
    }

    var showMkdirDialog by remember { mutableStateOf(false) }
    var showRenameDialog by remember { mutableStateOf<FileEntry?>(null) }
    var showDeleteConfirm by remember { mutableStateOf<FileEntry?>(null) }

    Scaffold(
        topBar = {
            TopAppBar(
                title = {
                    Column {
                        Text("Files", style = MaterialTheme.typography.titleMedium)
                        Text(
                            text = uiState.currentPath,
                            style = MaterialTheme.typography.bodySmall,
                            maxLines = 1
                        )
                    }
                },
                navigationIcon = {
                    if (!isAtRoot) {
                        IconButton(onClick = viewModel::navigateUp) {
                            Icon(Icons.AutoMirrored.Outlined.ArrowBack, contentDescription = "Up")
                        }
                    }
                },
                actions = {
                    IconButton(onClick = viewModel::refresh) {
                        Icon(Icons.Outlined.Refresh, contentDescription = "Refresh")
                    }
                }
            )
        },
        floatingActionButton = {
            if (uiState.permissionGranted) {
                FloatingActionButton(onClick = { showMkdirDialog = true }) {
                    Icon(Icons.Outlined.CreateNewFolder, contentDescription = "New folder")
                }
            }
        },
        snackbarHost = { SnackbarHost(snackbarHostState) }
    ) { padding ->
        Box(
            modifier = Modifier
                .fillMaxSize()
                .padding(padding)
        ) {
            when {
                !uiState.permissionGranted -> {
                    PermissionDeniedView(
                        onRequestPermission = { requestPermission() }
                    )
                }
                uiState.isLoading -> LoadingIndicator()
                uiState.items.isEmpty() -> EmptyStateView(
                    icon = Icons.Outlined.Folder,
                    title = "Empty folder",
                    subtitle = "No files or folders found here"
                )
                else -> {
                    LazyColumn(modifier = Modifier.fillMaxSize()) {
                        items(uiState.items, key = { it.path }) { item ->
                            var showItemMenu by remember { mutableStateOf(false) }

                            FileItem(
                                item = item,
                                onClick = {
                                    if (item.isDirectory) {
                                        viewModel.enterDirectory(item.path)
                                    } else {
                                        openFile(context, item) { error ->
                                            scope.launch { snackbarHostState.showSnackbar(error) }
                                        }
                                    }
                                },
                                onMoreClick = { showItemMenu = true }
                            )

                            Box {
                                DropdownMenu(
                                    expanded = showItemMenu,
                                    onDismissRequest = { showItemMenu = false }
                                ) {
                                    DropdownMenuItem(
                                        text = { Text("Rename") },
                                        leadingIcon = { Icon(Icons.Outlined.DriveFileRenameOutline, null) },
                                        onClick = {
                                            showItemMenu = false
                                            showRenameDialog = item
                                        }
                                    )
                                    DropdownMenuItem(
                                        text = { Text("Delete") },
                                        leadingIcon = { Icon(Icons.Outlined.Delete, null) },
                                        onClick = {
                                            showItemMenu = false
                                            showDeleteConfirm = item
                                        }
                                    )
                                }
                            }
                        }
                    }
                }
            }
        }
    }

    if (showMkdirDialog) {
        NameInputDialog(
            title = "New Folder",
            onDismiss = { showMkdirDialog = false },
            onConfirm = { name ->
                viewModel.createDirectory(name)
                showMkdirDialog = false
            }
        )
    }

    showRenameDialog?.let { item ->
        NameInputDialog(
            title = "Rename",
            initialValue = item.name,
            onDismiss = { showRenameDialog = null },
            onConfirm = { newName ->
                viewModel.renameFile(item.path, newName)
                showRenameDialog = null
            }
        )
    }

    showDeleteConfirm?.let { item ->
        AlertDialog(
            onDismissRequest = { showDeleteConfirm = null },
            title = { Text("Delete") },
            text = { Text("Are you sure you want to delete '${item.name}'? This cannot be undone.") },
            confirmButton = {
                TextButton(
                    onClick = {
                        viewModel.deleteFile(item.path)
                        showDeleteConfirm = null
                    },
                    colors = androidx.compose.material3.ButtonDefaults.textButtonColors(
                        contentColor = MaterialTheme.colorScheme.error
                    )
                ) {
                    Text("Delete")
                }
            },
            dismissButton = {
                TextButton(onClick = { showDeleteConfirm = null }) {
                    Text("Cancel")
                }
            }
        )
    }
}

@Composable
private fun PermissionDeniedView(onRequestPermission: () -> Unit) {
    EmptyStateView(
        icon = Icons.Outlined.Storage,
        title = "Permission Required",
        subtitle = "Storage permission is needed to browse files.",
        action = {
            Button(onClick = onRequestPermission, modifier = Modifier.padding(top = 16.dp)) {
                Text("Grant Permission")
            }
        }
    )
}

@Composable
private fun NameInputDialog(
    title: String,
    initialValue: String = "",
    onDismiss: () -> Unit,
    onConfirm: (String) -> Unit
) {
    var name by remember { mutableStateOf(initialValue) }
    AlertDialog(
        onDismissRequest = onDismiss,
        title = { Text(title) },
        text = {
            OutlinedTextField(
                value = name,
                onValueChange = { name = it },
                label = { Text("Name") },
                singleLine = true,
                modifier = Modifier.fillMaxWidth()
            )
        },
        confirmButton = {
            TextButton(
                onClick = { if (name.isNotBlank()) onConfirm(name) },
                enabled = name.isNotBlank()
            ) {
                Text("Confirm")
            }
        },
        dismissButton = {
            TextButton(onClick = onDismiss) {
                Text("Cancel")
            }
        }
    )
}

private fun openFile(
    context: android.content.Context,
    item: FileEntry,
    onError: (String) -> Unit
) {
    try {
        val file = File(item.path)
        val uri = FileProvider.getUriForFile(
            context,
            "${context.packageName}.fileprovider",
            file
        )
        val extension = MimeTypeMap.getFileExtensionFromUrl(uri.toString())
        val mimeType = MimeTypeMap.getSingleton().getMimeTypeFromExtension(extension) ?: "*/*"

        val intent = Intent(Intent.ACTION_VIEW).apply {
            setDataAndType(uri, mimeType)
            addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION)
        }
        context.startActivity(Intent.createChooser(intent, "Open file with"))
    } catch (e: Exception) {
        onError(e.message ?: "Could not open file")
    }
}
