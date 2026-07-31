package com.aeliavision.androiddeck.feature.dashboard.ui

import androidx.compose.foundation.background
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.verticalScroll
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.outlined.CheckCircle
import androidx.compose.material.icons.outlined.ErrorOutline
import androidx.compose.material.icons.outlined.Person
import androidx.compose.material.icons.outlined.Sync
import androidx.compose.material3.*
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.hilt.navigation.compose.hiltViewModel
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import com.aeliavision.androiddeck.core.ui.components.LoadingIndicator
import com.aeliavision.androiddeck.core.ui.components.AppCard
import com.aeliavision.androiddeck.feature.dashboard.viewmodel.ActivityUiItem
import com.aeliavision.androiddeck.feature.dashboard.viewmodel.DashboardViewModel

@OptIn(ExperimentalMaterial3Api::class)
@Composable
public fun DashboardScreen(
    viewModel: DashboardViewModel = hiltViewModel()
) {
    val uiState by viewModel.uiState.collectAsStateWithLifecycle()

    Scaffold(
        topBar = {
            TopAppBar(
                title = {
                    Text(
                        text = "Main Dashboard Summary",
                        style = MaterialTheme.typography.titleLarge,
                        color = MaterialTheme.colorScheme.primary,
                        modifier = Modifier.fillMaxWidth(),
                        textAlign = TextAlign.Center
                    )
                },
                colors = TopAppBarDefaults.topAppBarColors(
                    containerColor = MaterialTheme.colorScheme.surface
                )
            )
        },
        containerColor = MaterialTheme.colorScheme.surface
    ) { padding ->
        if (uiState.isLoading) {
            LoadingIndicator()
        } else if (uiState.errorMessage != null) {
            Column(
                modifier = Modifier
                    .fillMaxSize()
                    .padding(padding)
                    .padding(24.dp),
                horizontalAlignment = Alignment.CenterHorizontally,
                verticalArrangement = Arrangement.Center
            ) {
                Icon(
                    imageVector = Icons.Outlined.ErrorOutline,
                    contentDescription = null,
                    modifier = Modifier.size(48.dp),
                    tint = MaterialTheme.colorScheme.error
                )
                Spacer(Modifier.height(16.dp))
                Text(
                    text = uiState.errorMessage ?: "An error occurred",
                    style = MaterialTheme.typography.bodyLarge,
                    color = MaterialTheme.colorScheme.error,
                    textAlign = TextAlign.Center
                )
                Spacer(Modifier.height(16.dp))
                Button(onClick = { viewModel.refreshMetrics() }) {
                    Text("Retry")
                }
            }
        } else {
            Column(
                modifier = Modifier
                    .fillMaxSize()
                    .padding(padding)
                    .verticalScroll(rememberScrollState())
                    .padding(16.dp),
                verticalArrangement = Arrangement.spacedBy(24.dp)
            ) {
                // --- Dashboard Metrics Layout (Adaptive: 1 column compact, 2 medium, 3 expanded) ---
                BoxWithConstraints(modifier = Modifier.fillMaxWidth()) {
                    val maxWidthDp = maxWidth
                    if (maxWidthDp < 400.dp) {
                        Column(verticalArrangement = Arrangement.spacedBy(8.dp)) {
                            DashboardMetricCard(
                                modifier = Modifier.fillMaxWidth(),
                                title = "Contacts",
                                value = uiState.metrics.contactCount.toString(),
                                icon = Icons.Outlined.Person
                            )
                            StorageHealthCard(
                                modifier = Modifier.fillMaxWidth(),
                                usedGb = uiState.metrics.storageUsedGb,
                                totalGb = uiState.metrics.storageTotalGb
                            )
                            DashboardMetricCard(
                                modifier = Modifier.fillMaxWidth(),
                                title = "Last Sync:",
                                value = uiState.metrics.lastSyncTime,
                                icon = Icons.Outlined.Sync,
                                showCheck = true
                            )
                        }
                    } else if (maxWidthDp < 600.dp) {
                        Column(verticalArrangement = Arrangement.spacedBy(8.dp)) {
                            Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                                DashboardMetricCard(
                                    modifier = Modifier.weight(1f),
                                    title = "Contacts",
                                    value = uiState.metrics.contactCount.toString(),
                                    icon = Icons.Outlined.Person
                                )
                                StorageHealthCard(
                                    modifier = Modifier.weight(1f),
                                    usedGb = uiState.metrics.storageUsedGb,
                                    totalGb = uiState.metrics.storageTotalGb
                                )
                            }
                            DashboardMetricCard(
                                modifier = Modifier.fillMaxWidth(),
                                title = "Last Sync:",
                                value = uiState.metrics.lastSyncTime,
                                icon = Icons.Outlined.Sync,
                                showCheck = true
                            )
                        }
                    } else {
                        Row(
                            modifier = Modifier.fillMaxWidth(),
                            horizontalArrangement = Arrangement.spacedBy(8.dp)
                        ) {
                            DashboardMetricCard(
                                modifier = Modifier.weight(1f),
                                title = "Contacts",
                                value = uiState.metrics.contactCount.toString(),
                                icon = Icons.Outlined.Person
                            )

                            StorageHealthCard(
                                modifier = Modifier.weight(1.2f),
                                usedGb = uiState.metrics.storageUsedGb,
                                totalGb = uiState.metrics.storageTotalGb
                            )

                            DashboardMetricCard(
                                modifier = Modifier.weight(1f),
                                title = "Last Sync:",
                                value = uiState.metrics.lastSyncTime,
                                icon = Icons.Outlined.Sync,
                                showCheck = true
                            )
                        }
                    }
                }

                // --- Recent Activity Section ---------------------------------
                Column(verticalArrangement = Arrangement.spacedBy(16.dp)) {
                    Text(
                        "Recent Activity",
                        style = MaterialTheme.typography.headlineSmall,
                        color = MaterialTheme.colorScheme.primary,
                        fontWeight = FontWeight.Bold
                    )

                    AppCard(
                        containerColor = MaterialTheme.colorScheme.surfaceVariant
                    ) {
                        if (uiState.activities.isEmpty()) {
                            Text(
                                text = "No recent activity logged",
                                style = MaterialTheme.typography.bodyMedium,
                                color = MaterialTheme.colorScheme.onSurfaceVariant.copy(alpha = 0.7f),
                                modifier = Modifier
                                    .fillMaxWidth()
                                    .padding(16.dp),
                                textAlign = TextAlign.Center
                            )
                        } else {
                            Column(verticalArrangement = Arrangement.spacedBy(12.dp)) {
                                uiState.activities.forEachIndexed { index, activity ->
                                    ActivityRow(activity)
                                    if (index < uiState.activities.size - 1) {
                                        HorizontalDivider(color = MaterialTheme.colorScheme.onSurface.copy(alpha = 0.1f))
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }
    }
}

@Composable
private fun ActivityRow(activity: ActivityUiItem) {
    Row(
        modifier = Modifier
            .fillMaxWidth()
            .padding(vertical = 4.dp),
        horizontalArrangement = Arrangement.SpaceBetween,
        verticalAlignment = Alignment.CenterVertically
    ) {
        Column {
            Text(
                text = activity.title,
                style = MaterialTheme.typography.bodyLarge,
                color = MaterialTheme.colorScheme.onSurface,
                fontWeight = FontWeight.Medium
            )
            Text(
                text = activity.relativeTime,
                style = MaterialTheme.typography.bodySmall,
                color = MaterialTheme.colorScheme.onSurface.copy(alpha = 0.5f)
            )
        }
        if (activity.isComplete) {
            Icon(
                imageVector = Icons.Outlined.CheckCircle,
                contentDescription = null,
                tint = MaterialTheme.colorScheme.primary,
                modifier = Modifier.size(20.dp)
            )
        }
    }
}

@Composable
private fun DashboardMetricCard(
    modifier: Modifier = Modifier,
    title: String,
    value: String,
    icon: androidx.compose.ui.graphics.vector.ImageVector,
    showCheck: Boolean = false
) {
    AppCard(
        modifier = modifier.height(115.dp),
        containerColor = MaterialTheme.colorScheme.surfaceVariant,
        contentPadding = PaddingValues(12.dp)
    ) {
        Column(
            modifier = Modifier.fillMaxSize(),
            horizontalAlignment = Alignment.CenterHorizontally,
            verticalArrangement = Arrangement.Center
        ) {
            Row(verticalAlignment = Alignment.CenterVertically) {
                Icon(icon, null, Modifier.size(16.dp), tint = MaterialTheme.colorScheme.primary)
                if (showCheck) {
                    Spacer(Modifier.width(4.dp))
                    Icon(Icons.Outlined.CheckCircle, null, Modifier.size(14.dp), tint = MaterialTheme.colorScheme.primary)
                }
            }
            Spacer(Modifier.height(8.dp))
            Text(
                text = value,
                style = MaterialTheme.typography.bodyLarge,
                fontWeight = FontWeight.Bold,
                color = MaterialTheme.colorScheme.onSurface,
                maxLines = 1,
                overflow = androidx.compose.ui.text.style.TextOverflow.Ellipsis
            )
            Text(
                text = title,
                style = MaterialTheme.typography.labelSmall,
                color = MaterialTheme.colorScheme.onSurface.copy(alpha = 0.6f)
            )
        }
    }
}

@Composable
private fun StorageHealthCard(
    modifier: Modifier = Modifier,
    usedGb: Double,
    totalGb: Double
) {
    val progress = if (totalGb > 0) (usedGb / totalGb).toFloat().coerceIn(0f, 1f) else 0f

    AppCard(
        modifier = modifier.height(115.dp),
        containerColor = MaterialTheme.colorScheme.surfaceVariant,
        contentPadding = PaddingValues(12.dp)
    ) {
        Column(
            modifier = Modifier.fillMaxSize(),
            horizontalAlignment = Alignment.CenterHorizontally,
            verticalArrangement = Arrangement.Center
        ) {
            Text(
                text = buildString {
                    append("%.0fGB".format(usedGb))
                    append(" of %.0fGB used".format(totalGb))
                },
                style = MaterialTheme.typography.labelSmall,
                fontWeight = FontWeight.Bold,
                color = MaterialTheme.colorScheme.onSurface,
                fontSize = 10.sp,
                maxLines = 2,
                textAlign = TextAlign.Center
            )

            Spacer(Modifier.height(10.dp))
            Box(
                modifier = Modifier
                    .fillMaxWidth()
                    .height(6.dp)
                    .background(MaterialTheme.colorScheme.surface, CircleShape)
                    .padding(1.dp)
            ) {
                if (progress > 0f) {
                    Box(
                        modifier = Modifier
                            .fillMaxWidth(progress)
                            .fillMaxHeight()
                            .background(MaterialTheme.colorScheme.primary, CircleShape)
                    )
                }
            }

            Spacer(Modifier.height(10.dp))

            Text(
                text = "Storage Health",
                style = MaterialTheme.typography.labelSmall,
                color = MaterialTheme.colorScheme.onSurface.copy(alpha = 0.6f)
            )
        }
    }
}

