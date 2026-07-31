package com.aeliavision.androiddeck.feature.gallery.ui

import androidx.activity.compose.BackHandler
import androidx.compose.animation.*
import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.lazy.LazyRow
import androidx.compose.foundation.lazy.grid.GridCells
import androidx.compose.foundation.lazy.grid.LazyVerticalGrid
import androidx.compose.foundation.lazy.grid.itemsIndexed
import androidx.compose.foundation.lazy.grid.rememberLazyGridState
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.outlined.Close
import androidx.compose.material.icons.outlined.PhotoLibrary
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.layout.ContentScale
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.hilt.lifecycle.viewmodel.compose.hiltViewModel
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import coil3.compose.AsyncImage
import com.aeliavision.androiddeck.core.ui.components.EmptyStateView
import com.aeliavision.androiddeck.core.ui.components.LoadingIndicator
import com.aeliavision.androiddeck.core.ui.components.AppCard
import com.aeliavision.androiddeck.feature.gallery.model.MediaEntry
import com.aeliavision.androiddeck.feature.gallery.ui.components.MediaThumbnail
import com.aeliavision.androiddeck.feature.gallery.viewmodel.GalleryViewModel
import kotlinx.coroutines.flow.distinctUntilChanged
import kotlinx.coroutines.flow.filter
import kotlinx.coroutines.launch

@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun GalleryScreen(viewModel: GalleryViewModel = hiltViewModel()) {
    val uiState by viewModel.uiState.collectAsStateWithLifecycle()
    val scope = rememberCoroutineScope()
    val snackbarHostState = remember { SnackbarHostState() }
    val gridState = rememberLazyGridState()

    var previewMedia by remember { mutableStateOf<MediaEntry?>(null) }

    LaunchedEffect(Unit) {
        viewModel.loadAlbums()
        viewModel.refreshMedia()
    }

    LaunchedEffect(gridState) {
        snapshotFlow {
            val info = gridState.layoutInfo
            val lastVisible = info.visibleItemsInfo.lastOrNull()?.index ?: 0
            val total = info.totalItemsCount
            total > 0 && lastVisible >= total - 10
        }
            .distinctUntilChanged()
            .filter { it }
            .collect { viewModel.loadNextPage() }
    }

    LaunchedEffect(uiState.error) {
        uiState.error?.let {
            scope.launch { snackbarHostState.showSnackbar(it) }
            viewModel.clearError()
        }
    }

    BackHandler(enabled = previewMedia != null) {
        previewMedia = null
    }

    Scaffold(
        topBar = {
            TopAppBar(
                title = {
                    Text(
                        text = "Media Gallery",
                        style = MaterialTheme.typography.headlineMedium,
                        color = MaterialTheme.colorScheme.primary
                    )
                },
                colors = TopAppBarDefaults.topAppBarColors(
                    containerColor = MaterialTheme.colorScheme.surface
                )
            )
        },
        snackbarHost = { SnackbarHost(snackbarHostState) },
        containerColor = MaterialTheme.colorScheme.surface
    ) { padding ->
        Column(
            modifier = Modifier
                .fillMaxSize()
                .padding(padding)
        ) {
            LazyRow(
                modifier = Modifier
                    .fillMaxWidth()
                    .padding(vertical = 12.dp),
                contentPadding = PaddingValues(horizontal = 16.dp),
                horizontalArrangement = Arrangement.spacedBy(8.dp)
            ) {
                item {
                    LuxuryFilterChip(
                        selected = uiState.selectedAlbumId == null,
                        label = "All Media",
                        onClick = { viewModel.selectAlbum(null) }
                    )
                }
                items(uiState.albums, key = { it.id }) { album ->
                    LuxuryFilterChip(
                        selected = uiState.selectedAlbumId == album.id,
                        label = album.name,
                        onClick = { viewModel.selectAlbum(album.id) }
                    )
                }
            }

            Box(modifier = Modifier.fillMaxSize()) {
                if (uiState.isLoading) {
                    LoadingIndicator()
                } else if (uiState.media.isEmpty()) {
                    EmptyStateView(
                        icon = Icons.Outlined.PhotoLibrary,
                        title = "Treasury is empty",
                        subtitle = "No captured moments found"
                    )
                } else {
                    LazyVerticalGrid(
                        columns = GridCells.Fixed(4),
                        state = gridState,
                        contentPadding = PaddingValues(horizontal = 16.dp, vertical = 8.dp),
                        verticalArrangement = Arrangement.spacedBy(8.dp),
                        horizontalArrangement = Arrangement.spacedBy(8.dp),
                        modifier = Modifier.fillMaxSize()
                    ) {
                        itemsIndexed(uiState.media, key = { _, it -> it.id }) { _, item ->
                            val isSelected = uiState.selectedIds.contains(item.id)
                            MediaThumbnail(
                                media = item,
                                isSelected = isSelected,
                                onClick = { previewMedia = item },
                                onLongClick = { viewModel.toggleSelection(item.id) }
                            )
                        }
                    }
                }
            }
        }
    }

    AnimatedVisibility(
        visible = previewMedia != null,
        enter = fadeIn() + scaleIn(initialScale = 0.9f),
        exit = fadeOut() + scaleOut(targetScale = 0.9f)
    ) {
        previewMedia?.let {
            MediaPreviewOverlay(
                media = it,
                onClose = { previewMedia = null }
            )
        }
    }
}

@OptIn(ExperimentalMaterial3Api::class)
@Composable
private fun LuxuryFilterChip(
    selected: Boolean,
    label: String,
    onClick: () -> Unit
) {
    FilterChip(
        selected = selected,
        onClick = onClick,
        label = { Text(label, style = MaterialTheme.typography.labelLarge) },
        shape = CircleShape,
        colors = FilterChipDefaults.filterChipColors(
            selectedContainerColor = MaterialTheme.colorScheme.primary,
            selectedLabelColor = MaterialTheme.colorScheme.onPrimary,
            containerColor = MaterialTheme.colorScheme.surfaceVariant.copy(alpha = 0.3f),
            labelColor = MaterialTheme.colorScheme.onSurfaceVariant
        ),
        border = FilterChipDefaults.filterChipBorder(
            enabled = true,
            selected = selected,
            borderColor = MaterialTheme.colorScheme.outline.copy(alpha = 0.1f),
            selectedBorderColor = Color.Transparent,
            borderWidth = 1.dp
        )
    )
}

@Composable
private fun MediaPreviewOverlay(
    media: MediaEntry,
    onClose: () -> Unit
) {
    Box(
        modifier = Modifier
            .fillMaxSize()
            .background(Color.Black.copy(alpha = 0.95f))
            .clickable(enabled = false) {}
    ) {
        AsyncImage(
            model = media.uri,
            contentDescription = media.name,
            contentScale = ContentScale.Fit,
            modifier = Modifier.fillMaxSize()
        )

        Column(
            modifier = Modifier
                .align(Alignment.BottomCenter)
                .fillMaxWidth()
                .padding(24.dp)
        ) {
            AppCard(
                containerColor = Color.Black.copy(alpha = 0.6f)
            ) {
                Row(
                    modifier = Modifier.fillMaxWidth(),
                    verticalAlignment = Alignment.CenterVertically,
                    horizontalArrangement = Arrangement.SpaceBetween
                ) {
                    Column(modifier = Modifier.weight(1f)) {
                        Text(
                            text = media.name,
                            color = Color.White,
                            style = MaterialTheme.typography.titleLarge,
                            fontWeight = FontWeight.Bold
                        )
                        Spacer(Modifier.height(4.dp))
                        Text(
                            text = "${media.width}x${media.height} • ${media.mimeType.uppercase()}",
                            color = Color.White.copy(alpha = 0.6f),
                            style = MaterialTheme.typography.labelMedium
                        )
                    }

                    IconButton(
                        onClick = onClose,
                        modifier = Modifier
                            .background(Color.White.copy(alpha = 0.1f), CircleShape)
                    ) {
                        Icon(Icons.Outlined.Close, "Close", tint = Color.White)
                    }
                }
            }
        }
    }
}
