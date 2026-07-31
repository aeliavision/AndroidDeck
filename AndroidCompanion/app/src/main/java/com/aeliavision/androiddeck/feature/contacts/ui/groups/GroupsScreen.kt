package com.aeliavision.androiddeck.feature.contacts.ui.groups

import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.automirrored.outlined.ArrowBack
import androidx.compose.material.icons.filled.Group
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.hilt.navigation.compose.hiltViewModel
import androidx.lifecycle.Lifecycle
import androidx.lifecycle.compose.LocalLifecycleOwner
import androidx.lifecycle.repeatOnLifecycle
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import com.aeliavision.androiddeck.core.ui.components.EmptyStateView
import com.aeliavision.androiddeck.core.ui.components.LoadingIndicator
import com.aeliavision.androiddeck.core.ui.components.AppCard
import com.aeliavision.androiddeck.feature.contacts.viewmodel.ContactsViewModel

@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun GroupsScreen(
    onGroupSelected: (groupId: String, groupTitle: String) -> Unit,
    onBack: () -> Unit, // New parameter
    viewModel: ContactsViewModel = hiltViewModel()
) {
    val uiState by viewModel.uiState.collectAsStateWithLifecycle()
    // (including navigate-back). LaunchedEffect(Unit) only fires once on first composition.
    val lifecycleOwner = LocalLifecycleOwner.current
    LaunchedEffect(lifecycleOwner.lifecycle) {
        lifecycleOwner.lifecycle.repeatOnLifecycle(Lifecycle.State.RESUMED) {
            viewModel.loadGroups()
        }
    }

    Scaffold(
        topBar = {
            TopAppBar(
                title = { Text("Groups") },
                navigationIcon = {
                    IconButton(onClick = onBack) {
                        Icon(Icons.AutoMirrored.Outlined.ArrowBack, contentDescription = "Back")
                    }
                }
            )
        }
    ) { padding ->
        when {
            uiState.isLoading -> LoadingIndicator()
            uiState.groups.isEmpty() -> EmptyStateView(
                icon = Icons.Default.Group,
                title = "No groups found",
                subtitle = "Contact groups from your accounts will appear here",
                modifier = Modifier.padding(padding)
            )
            else -> LazyColumn(
                modifier = Modifier
                    .fillMaxSize()
                    .padding(padding),
                contentPadding = PaddingValues(16.dp),
                verticalArrangement = Arrangement.spacedBy(12.dp)
            ) {
                items(uiState.groups, key = { it.id }) { group ->
                    AppCard(
                        modifier = Modifier.clickable {
                            onGroupSelected(group.id, group.title)
                        }
                    ) {
                        Row(
                            verticalAlignment = Alignment.CenterVertically,
                            modifier = Modifier.fillMaxWidth()
                        ) {
                            Icon(
                                Icons.Default.Group,
                                contentDescription = null,
                                tint = MaterialTheme.colorScheme.primary,
                                modifier = Modifier.size(32.dp)
                            )
                            Spacer(Modifier.width(16.dp))
                            Column {
                                Text(
                                    text = group.title,
                                    style = MaterialTheme.typography.titleMedium,
                                    fontWeight = FontWeight.Bold
                                )
                                Text(
                                    text = "${group.memberCount} contact${if (group.memberCount != 1) "s" else ""}",
                                    style = MaterialTheme.typography.bodySmall,
                                    color = MaterialTheme.colorScheme.onSurfaceVariant
                                )
                            }
                        }
                    }
                }
            }
        }
    }
}
