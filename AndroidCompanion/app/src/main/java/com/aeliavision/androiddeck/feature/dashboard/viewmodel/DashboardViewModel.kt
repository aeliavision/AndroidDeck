package com.aeliavision.androiddeck.feature.dashboard.viewmodel

import android.os.Environment
import android.os.StatFs
import android.text.format.DateUtils
import androidx.lifecycle.ViewModel
import androidx.lifecycle.viewModelScope
import com.aeliavision.androiddeck.feature.contacts.data.ContactsRepository
import com.aeliavision.androiddeck.feature.dashboard.data.ActivityLogRepository
import com.aeliavision.androiddeck.feature.dashboard.model.ActivityType
import dagger.hilt.android.lifecycle.HiltViewModel
import kotlinx.coroutines.CoroutineDispatcher
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.flow.collectLatest
import kotlinx.coroutines.flow.update
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext
import javax.inject.Inject


@androidx.compose.runtime.Immutable
public data class DashboardMetrics(
    val contactCount: Int = 0,
    val storageUsedGb: Double = 0.0,
    val storageTotalGb: Double = 0.0,
    val lastSyncTime: String = "Never"
)

@androidx.compose.runtime.Immutable
public data class ActivityUiItem(
    val title: String,
    val relativeTime: String,
    val type: ActivityType,
    val isComplete: Boolean = true
)

@androidx.compose.runtime.Immutable
public data class DashboardUiState(
    val metrics: DashboardMetrics = DashboardMetrics(),
    val activities: List<ActivityUiItem> = emptyList(),
    val isLoading: Boolean = true,
    val errorMessage: String? = null
)

@HiltViewModel
public class DashboardViewModel @Inject constructor(
    private val contactsRepository: ContactsRepository,
    private val activityLogRepository: ActivityLogRepository
) : ViewModel() {

    internal var ioDispatcher: CoroutineDispatcher = Dispatchers.IO

    private val _uiState = MutableStateFlow(DashboardUiState())
    public val uiState: StateFlow<DashboardUiState> = _uiState.asStateFlow()

    init {
        observeActivities()
        refreshMetrics()
    }


    private fun observeActivities() {
        viewModelScope.launch {
            activityLogRepository.activities.collectLatest { activities ->
                val uiItems = activities.map { item ->
                    ActivityUiItem(
                        title = item.title,
                        relativeTime = formatRelativeTime(item.timestamp),
                        type = item.type,
                        isComplete = item.isComplete
                    )
                }
                
                val lastSyncLog = activities.firstOrNull { it.type == ActivityType.SYNC_START || it.type == ActivityType.SYNC_STOP }
                val lastSyncTime = lastSyncLog?.let { formatRelativeTime(it.timestamp) } ?: "Never"

                _uiState.update { current ->
                    current.copy(
                        activities = uiItems,
                        metrics = current.metrics.copy(lastSyncTime = lastSyncTime)
                    )
                }
            }
        }
    }

    public fun refreshMetrics() {
        viewModelScope.launch {
            _uiState.update { it.copy(isLoading = true, errorMessage = null) }
            try {
                val (contacts, used, total) = withContext(ioDispatcher) {
                    val contactCount = contactsRepository.getAllContactIds().size
                    val (usedGb, totalGb) = try {
                        val path = Environment.getDataDirectory()
                        val stat = StatFs(path.path)
                        val blockSize = stat.blockSizeLong
                        val totalBlocks = stat.blockCountLong
                        val availableBlocks = stat.availableBlocksLong
                        
                        val total = (totalBlocks * blockSize).toDouble() / (1024 * 1024 * 1024)
                        val used = ((totalBlocks - availableBlocks) * blockSize).toDouble() / (1024 * 1024 * 1024)
                        Pair(used, total)
                    } catch (e: Throwable) {
                        Pair(0.0, 0.0)
                    }
                    Triple(contactCount, usedGb, totalGb)
                }


                _uiState.update { current ->
                    current.copy(
                        metrics = current.metrics.copy(
                            contactCount = contacts,
                            storageUsedGb = used,
                            storageTotalGb = total
                        ),
                        isLoading = false
                    )
                }
            } catch (e: Exception) {
                _uiState.update { current ->
                    current.copy(
                        isLoading = false,
                        errorMessage = e.localizedMessage ?: "Failed to load dashboard metrics"
                    )
                }
            }
        }
    }

    private fun formatRelativeTime(timestamp: Long): String {
        return DateUtils.getRelativeTimeSpanString(
            timestamp,
            System.currentTimeMillis(),
            DateUtils.MINUTE_IN_MILLIS,
            DateUtils.FORMAT_ABBREV_RELATIVE
        ).toString()
    }
}

