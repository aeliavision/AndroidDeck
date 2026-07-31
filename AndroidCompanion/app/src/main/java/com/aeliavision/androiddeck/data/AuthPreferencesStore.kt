package com.aeliavision.androiddeck.data

import android.content.Context
import androidx.datastore.core.DataStore
import androidx.datastore.preferences.core.Preferences
import androidx.datastore.preferences.core.edit
import androidx.datastore.preferences.core.stringPreferencesKey
import androidx.datastore.preferences.preferencesDataStore
import dagger.hilt.android.qualifiers.ApplicationContext
import kotlinx.coroutines.flow.Flow
import kotlinx.coroutines.flow.map
import javax.inject.Inject
import javax.inject.Singleton

/**
 *
 * Benefits over SharedPreferences:
 * - Fully coroutine / Flow based — no blocking I/O on the main thread.
 * - Atomic, transactional writes — no corrupted state on process death.
 * - Type-safe keys — no stringly-typed getString("key", null) calls.
 */
@Singleton
class AuthPreferencesStore @Inject constructor(
    private val dataStore: DataStore<Preferences>
) {
    companion object {
        private val KEY_PINNED_CERT_SHA256 = stringPreferencesKey("pinned_cert_sha256")
        private val KEY_LAST_HOST          = stringPreferencesKey("last_host")
        private val KEY_SESSION_ID         = stringPreferencesKey("session_id")
        private val KEY_DARK_MODE          = stringPreferencesKey("dark_mode")
        private val KEY_SERVER_PORT        = stringPreferencesKey("server_port")
        private val KEY_LAST_PORT          = stringPreferencesKey("last_port")
    }

    // --- Pinned certificate fingerprint ------------------------------------------

    val pinnedCertSha256: Flow<String?> = dataStore.data
        .map { it[KEY_PINNED_CERT_SHA256] }

    suspend fun setPinnedCertSha256(sha256: String?) {
        dataStore.edit { prefs ->
            if (sha256 == null) prefs.remove(KEY_PINNED_CERT_SHA256)
            else prefs[KEY_PINNED_CERT_SHA256] = sha256
        }
    }

    // --- Last connected host / port -----------------------------------------------

    val lastHost: Flow<String?> = dataStore.data
        .map { it[KEY_LAST_HOST] }

    val lastPort: Flow<Int?> = dataStore.data
        .map { it[KEY_LAST_PORT]?.toIntOrNull() }

    suspend fun setLastEndpoint(host: String, port: Int) {
        dataStore.edit { prefs ->
            prefs[KEY_LAST_HOST] = host
            prefs[KEY_LAST_PORT] = port.toString()
        }
    }

    // --- Session persistence (optional — cleared on disconnect) ------------------

    val sessionId: Flow<String?> = dataStore.data
        .map { it[KEY_SESSION_ID] }

    suspend fun setSessionId(id: String?) {
        dataStore.edit { prefs ->
            if (id == null) prefs.remove(KEY_SESSION_ID)
            else prefs[KEY_SESSION_ID] = id
        }
    }

    // --- App Settings ------------------------------------------------------------

    val darkMode: Flow<String> = dataStore.data
        .map { it[KEY_DARK_MODE] ?: "system" }

    suspend fun setDarkMode(mode: String) {
        dataStore.edit { it[KEY_DARK_MODE] = mode }
    }

    val serverPort: Flow<Int> = dataStore.data
        .map { it[KEY_SERVER_PORT]?.toIntOrNull() ?: 8732 }

    suspend fun setServerPort(port: Int) {
        dataStore.edit { it[KEY_SERVER_PORT] = port.toString() }
    }

    suspend fun clearAll() {
        dataStore.edit { it.clear() }
    }
}
