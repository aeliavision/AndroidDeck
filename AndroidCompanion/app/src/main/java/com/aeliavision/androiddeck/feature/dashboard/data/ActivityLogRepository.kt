package com.aeliavision.androiddeck.feature.dashboard.data

import android.content.Context
import androidx.datastore.core.DataStore
import androidx.datastore.preferences.core.Preferences
import androidx.datastore.preferences.core.edit
import androidx.datastore.preferences.core.stringPreferencesKey
import androidx.datastore.preferences.preferencesDataStore
import com.aeliavision.androiddeck.feature.dashboard.model.ActivityItem
import com.aeliavision.androiddeck.feature.dashboard.model.ActivityType
import dagger.hilt.android.qualifiers.ApplicationContext
import kotlinx.coroutines.flow.Flow
import kotlinx.coroutines.flow.map
import kotlinx.serialization.json.Json
import kotlinx.serialization.encodeToString
import java.util.UUID
import javax.inject.Inject
import javax.inject.Singleton

private val Context.dataStore: DataStore<Preferences> by preferencesDataStore(name = "activity_logs")

@Singleton
public class ActivityLogRepository @Inject constructor(
    @ApplicationContext private val context: Context
) {
    private val json = Json { ignoreUnknownKeys = true }
    private val activitiesKey = stringPreferencesKey("activities_json")

    public val activities: Flow<List<ActivityItem>> = context.dataStore.data.map { preferences ->
        val jsonString = preferences[activitiesKey] ?: "[]"
        try {
            json.decodeFromString<List<ActivityItem>>(jsonString)
        } catch (e: Exception) {
            emptyList()
        }
    }

    public suspend fun logActivity(title: String, type: ActivityType) {
        context.dataStore.edit { preferences ->
            val currentJson = preferences[activitiesKey] ?: "[]"
            val currentList = try {
                json.decodeFromString<List<ActivityItem>>(currentJson).toMutableList()
            } catch (e: Exception) {
                mutableListOf()
            }

            val newItem = ActivityItem(
                id = UUID.randomUUID().toString(),
                title = title,
                timestamp = System.currentTimeMillis(),
                type = type
            )

            currentList.add(0, newItem)
            val trimmedList = if (currentList.size > 20) currentList.take(20) else currentList
            
            preferences[activitiesKey] = json.encodeToString(trimmedList)
        }
    }
}
