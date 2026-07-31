package com.aeliavision.androiddeck.feature.contacts.ui.list

import android.content.Intent
import android.net.Uri
import androidx.activity.compose.rememberLauncherForActivityResult
import androidx.activity.result.contract.ActivityResultContracts
import androidx.compose.animation.*
import androidx.compose.animation.core.*
import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.itemsIndexed
import androidx.compose.foundation.lazy.rememberLazyListState
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.snapshotFlow
import kotlinx.coroutines.flow.distinctUntilChanged
import kotlinx.coroutines.flow.filter
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.outlined.*
import androidx.compose.material.icons.automirrored.outlined.ArrowBack
import androidx.compose.material3.*
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.core.content.FileProvider
import androidx.hilt.navigation.compose.hiltViewModel
import androidx.lifecycle.Lifecycle
import androidx.lifecycle.compose.LocalLifecycleOwner
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import androidx.lifecycle.repeatOnLifecycle
import com.aeliavision.androiddeck.core.ui.components.EmptyStateView
import com.aeliavision.androiddeck.core.ui.components.LoadingIndicator
import com.aeliavision.androiddeck.feature.contacts.viewmodel.ContactsViewModel
import kotlinx.coroutines.launch
import java.io.File

@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun ContactListScreen(
    onContactClick: (String) -> Unit,
    onAddContact: () -> Unit,
    onCleanupClick: () -> Unit,
    onGroupsClick: () -> Unit,
    filterGroupId: String? = null,
    onBack: (() -> Unit)? = null,
    viewModel: ContactsViewModel = hiltViewModel()
) {
    val uiState by viewModel.uiState.collectAsStateWithLifecycle()
    val searchQuery by viewModel.searchQuery.collectAsStateWithLifecycle()

    val listState = rememberLazyListState()
    LaunchedEffect(listState) {
        snapshotFlow {
            val info = listState.layoutInfo
            val lastVisible = info.visibleItemsInfo.lastOrNull()?.index ?: 0
            val total = info.totalItemsCount
            total > 0 && lastVisible >= total - 5
        }
            .distinctUntilChanged()
            .filter { it }
            .collect { viewModel.loadNextPage() }
    }

    val lifecycleOwner = LocalLifecycleOwner.current
    LaunchedEffect(filterGroupId, lifecycleOwner.lifecycle) {
        lifecycleOwner.lifecycle.repeatOnLifecycle(Lifecycle.State.RESUMED) {
            // Full refresh on every Resume causes flickering and redundant work.
            if (filterGroupId != null) {
                viewModel.loadContactsByGroup(filterGroupId)
            } else if (uiState.contacts.isEmpty()) {
                viewModel.refresh()
            }
        }
    }
    val snackbarHostState = remember { SnackbarHostState() }
    val scope = rememberCoroutineScope()
    var searchActive by remember { mutableStateOf(false) }

    LaunchedEffect(uiState.error) {
        uiState.error?.let {
            scope.launch { snackbarHostState.showSnackbar(it) }
            viewModel.clearError()
        }
    }

    val context = LocalContext.current
    var showMenu by remember { mutableStateOf(false) }

    val importLauncher = rememberLauncherForActivityResult(
        ActivityResultContracts.GetContent()
    ) { uri: Uri? ->
        if (uri != null) viewModel.importVcf(uri)
    }
    LaunchedEffect(Unit) {
        viewModel.exportResult.collect { vcf ->
            val file = File(context.cacheDir, "contacts_export.vcf")
            file.writeText(vcf)
            val shareUri = FileProvider.getUriForFile(context, "${context.packageName}.fileprovider", file)
            val intent = Intent(Intent.ACTION_SEND).apply {
                type = "text/x-vcard"
                putExtra(Intent.EXTRA_STREAM, shareUri)
                addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION)
            }
            context.startActivity(Intent.createChooser(intent, "Share VCF"))
        }
    }

    LaunchedEffect(Unit) {
        viewModel.importResult.collect { result ->
            snackbarHostState.showSnackbar("Imported ${result.imported}/${result.total} contacts")
        }
    }

    Scaffold(
        topBar = {
            CenterAlignedTopAppBar(
                title = {
                    Text(
                        "Compact Modern Contacts",
                        style = MaterialTheme.typography.titleMedium,
                        color = MaterialTheme.colorScheme.primary,
                        fontWeight = FontWeight.Bold
                    )
                },
                navigationIcon = {
                    IconButton(onClick = { searchActive = true }) {
                        Icon(Icons.Outlined.Search, "Search", tint = MaterialTheme.colorScheme.primary)
                    }
                },
                actions = {
                    Box {
                        IconButton(onClick = { showMenu = true }) {
                            Icon(Icons.Outlined.Tune, "Options", tint = MaterialTheme.colorScheme.primary)
                        }
                        DropdownMenu(
                            expanded = showMenu,
                            onDismissRequest = { showMenu = false }
                        ) {
                            DropdownMenuItem(
                                text = { Text("Groups") },
                                leadingIcon = { Icon(Icons.Outlined.Group, null) },
                                onClick = {
                                    showMenu = false
                                    onGroupsClick()
                                }
                            )
                            HorizontalDivider()
                            DropdownMenuItem(
                                text = { Text("Import VCF") },
                                leadingIcon = { Icon(Icons.Outlined.FileDownload, null) },
                                onClick = {
                                    showMenu = false
                                    importLauncher.launch("text/x-vcard")
                                }
                            )
                            DropdownMenuItem(
                                text = { Text("Export All") },
                                leadingIcon = { Icon(Icons.Outlined.FileUpload, null) },
                                onClick = {
                                    showMenu = false
                                    viewModel.exportVcf()
                                }
                            )
                            DropdownMenuItem(
                                text = { Text("Cleanup Duplicates") },
                                leadingIcon = { Icon(Icons.Outlined.CleaningServices, null) },
                                onClick = {
                                    showMenu = false
                                    onCleanupClick()
                                }
                            )
                        }
                    }
                },
                colors = TopAppBarDefaults.topAppBarColors(containerColor = MaterialTheme.colorScheme.surface)
            )
        },
        floatingActionButton = {
            FloatingActionButton(
                onClick = onAddContact,
                containerColor = MaterialTheme.colorScheme.primary,
                contentColor = MaterialTheme.colorScheme.onPrimary
            ) {
                Icon(Icons.Outlined.Add, "Add")
            }
        },
        snackbarHost = { SnackbarHost(snackbarHostState) },
        containerColor = MaterialTheme.colorScheme.surface
    ) { padding ->
        Column(
            modifier = Modifier
                .fillMaxSize()
                .padding(padding)
        ) {
            if (searchActive) {
                SearchBar(
                    inputField = {
                        SearchBarDefaults.InputField(
                            query = searchQuery,
                            onQueryChange = viewModel::search,
                            onSearch = { searchActive = false },
                            expanded = searchActive,
                            onExpandedChange = { searchActive = it },
                            placeholder = { Text("Search contacts...") },
                            leadingIcon = { Icon(Icons.Outlined.Search, null) }
                        )
                    },
                    expanded = searchActive,
                    onExpandedChange = { searchActive = it },
                    modifier = Modifier.fillMaxWidth().padding(horizontal = 16.dp)
                ) {}
            }

            Box(modifier = Modifier.fillMaxSize()) {
                when {
                    uiState.isLoading -> LoadingIndicator()
                    uiState.contacts.isEmpty() -> EmptyStateView(
                        icon = Icons.Outlined.Contacts,
                        title = "No contacts",
                        subtitle = "Your contacts will appear here"
                    )
                    else -> LazyColumn(
                        modifier = Modifier.fillMaxSize(),
                        state = listState,
                        contentPadding = PaddingValues(bottom = 80.dp)
                    ) {
                        itemsIndexed(
                            items = uiState.contacts,
                            key = { _, it -> it.id ?: it.fullName }
                        ) { _, contact ->
                            CompactContactRow(
                                contact = contact,
                                onClick = { contact.id?.let(onContactClick) }
                            )
                        }
                    }
                }
            }
        }
    }
}

@Composable
private fun CompactContactRow(
    contact: com.aeliavision.androiddeck.feature.contacts.model.ContactDto,
    onClick: () -> Unit
) {
    Column(
        modifier = Modifier
            .fillMaxWidth()
            .clickable(onClick = onClick)
            .padding(horizontal = 16.dp, vertical = 12.dp)
    ) {
        Row(
            modifier = Modifier.fillMaxWidth(),
            verticalAlignment = Alignment.CenterVertically
        ) {
            com.aeliavision.androiddeck.core.ui.components.ContactAvatar(
                name = contact.fullName,
                size = 52.dp,
                modifier = Modifier.border(1.dp, MaterialTheme.colorScheme.primary.copy(alpha = 0.3f), androidx.compose.foundation.shape.CircleShape)
            )
            Spacer(Modifier.width(16.dp))
            Column(modifier = Modifier.weight(1f)) {
                Text(
                    text = contact.fullName,
                    style = MaterialTheme.typography.bodyLarge,
                    fontWeight = FontWeight.Bold,
                    color = MaterialTheme.colorScheme.onSurface
                )
                val detail = contact.emails?.firstOrNull()?.value ?: contact.phones?.firstOrNull()?.value.orEmpty()
                if (detail.isNotBlank()) {
                    Text(
                        text = detail,
                        style = MaterialTheme.typography.bodySmall,
                        color = MaterialTheme.colorScheme.onSurface.copy(alpha = 0.5f)
                    )
                }
            }
        }
        Spacer(Modifier.height(12.dp))
        HorizontalDivider(color = MaterialTheme.colorScheme.onSurface.copy(alpha = 0.1f))
    }
}

