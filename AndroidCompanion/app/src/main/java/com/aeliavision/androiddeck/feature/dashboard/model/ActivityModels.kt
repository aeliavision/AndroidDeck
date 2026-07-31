package com.aeliavision.androiddeck.feature.dashboard.model

import kotlinx.serialization.Serializable

@Serializable
public enum class ActivityType {
    IMPORT,
    BACKUP,
    SYNC_START,
    SYNC_STOP
}

@Serializable
public data class ActivityItem(
    val id: String,
    val title: String,
    val timestamp: Long,
    val type: ActivityType,
    val isComplete: Boolean = true
)
