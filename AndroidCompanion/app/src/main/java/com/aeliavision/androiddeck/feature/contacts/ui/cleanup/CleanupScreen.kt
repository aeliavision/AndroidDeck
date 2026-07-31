package com.aeliavision.androiddeck.feature.contacts.ui.cleanup

import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.automirrored.filled.ArrowBack
import androidx.compose.material.icons.filled.CleaningServices
import androidx.compose.material.icons.filled.Merge
import androidx.compose.material3.Button
import androidx.compose.material3.Card
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Scaffold
import androidx.compose.material3.SnackbarHost
import androidx.compose.material3.SnackbarHostState
import androidx.compose.material3.Text
import androidx.compose.material3.TopAppBar
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.remember
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.unit.dp
import androidx.hilt.navigation.compose.hiltViewModel
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import com.aeliavision.androiddeck.core.ui.components.EmptyStateView
import com.aeliavision.androiddeck.core.ui.components.LoadingIndicator
import com.aeliavision.androiddeck.feature.contacts.model.DuplicateGroup
import com.aeliavision.androiddeck.feature.contacts.viewmodel.CleanupViewModel

@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun CleanupScreen(
    onBack: () -> Unit,
    viewModel: CleanupViewModel = hiltViewModel()
) {
    val uiState by viewModel.uiState.collectAsStateWithLifecycle()
    val snackbarHostState = remember { SnackbarHostState() }

    LaunchedEffect(Unit) {
        viewModel.loadDuplicates()
    }

    Scaffold(
        topBar = {
            TopAppBar(
                title = { Text("Contact Cleanup") },
                navigationIcon = {
                    IconButton(onClick = onBack) {
                        Icon(Icons.AutoMirrored.Filled.ArrowBack, contentDescription = "Back")
                    }
                }
            )
        },
        snackbarHost = { SnackbarHost(snackbarHostState) }
    ) { padding ->
        Box(modifier = Modifier.fillMaxSize().padding(padding)) {
            if (uiState.isLoading) {
                LoadingIndicator()
            } else if (uiState.duplicateGroups.isEmpty()) {
                EmptyStateView(
                    icon = Icons.Default.CleaningServices,
                    title = "Address book is clean",
                    subtitle = "No duplicate contacts found."
                )
            } else {
                LazyColumn(modifier = Modifier.fillMaxSize().padding(horizontal = 16.dp)) {
                    item {
                        Text(
                            text = "Duplicates Found",
                            style = MaterialTheme.typography.titleMedium,
                            modifier = Modifier.padding(vertical = 16.dp)
                        )
                    }
                    items(uiState.duplicateGroups) { group ->
                        DuplicateGroupItem(
                            group = group,
                            onMerge = { viewModel.mergeGroup(group) }
                        )
                        Spacer(modifier = Modifier.size(8.dp))
                    }
                }
            }
        }
    }
}

@Composable
fun DuplicateGroupItem(
    group: DuplicateGroup,
    onMerge: () -> Unit
) {
    Card(modifier = Modifier.fillMaxWidth()) {
        Column(modifier = Modifier.padding(16.dp)) {
            Text(
                text = "Group: ${group.value}",
                style = MaterialTheme.typography.labelLarge,
                color = MaterialTheme.colorScheme.primary
            )
            Text(
                text = "${group.contacts.size} entries",
                style = MaterialTheme.typography.bodySmall
            )
            
            group.contacts.forEach { contact ->
                Row(
                    modifier = Modifier.padding(vertical = 4.dp),
                    verticalAlignment = Alignment.CenterVertically
                ) {
                    Text(
                        text = contact.fullName,
                        style = MaterialTheme.typography.bodyMedium,
                        modifier = Modifier.weight(1f)
                    )
                    if (contact.phones?.isNotEmpty() == true) {
                        Text(
                            text = contact.phones!!.first().value,
                            style = MaterialTheme.typography.bodySmall,
                            color = MaterialTheme.colorScheme.onSurfaceVariant
                        )
                    }
                }
            }

            Spacer(modifier = Modifier.size(8.dp))
            Button(
                onClick = onMerge,
                modifier = Modifier.align(Alignment.End)
            ) {
                Icon(Icons.Default.Merge, contentDescription = null, modifier = Modifier.size(18.dp))
                Spacer(modifier = Modifier.width(8.dp))
                Text("Quick Merge")
            }
        }
    }
}
