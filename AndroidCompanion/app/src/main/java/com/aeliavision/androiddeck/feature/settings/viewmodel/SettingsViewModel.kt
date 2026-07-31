package com.aeliavision.androiddeck.feature.settings.viewmodel

import androidx.lifecycle.ViewModel
import androidx.lifecycle.viewModelScope
import com.aeliavision.androiddeck.data.AuthPreferencesStore
import com.aeliavision.androiddeck.feature.server.service.AuthManager
import com.aeliavision.androiddeck.feature.server.service.AuthManager.SessionSummary
import dagger.hilt.android.lifecycle.HiltViewModel
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.SharingStarted
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.combine
import kotlinx.coroutines.flow.stateIn
import kotlinx.coroutines.launch
import javax.inject.Inject

public data class SettingsUiState(
    val darkMode: String = "system",
    val serverPort: Int = 8732,
    val activeSessions: Int = 0,
    val portError: String? = null,
    val sessionsList: List<SessionSummary> = emptyList()
)

@HiltViewModel
public class SettingsViewModel @Inject constructor(
    private val preferencesStore: AuthPreferencesStore,
    private val authManager: AuthManager
) : ViewModel() {

    private val _portError = MutableStateFlow<String?>(null)
    private val _sessionsList = MutableStateFlow<List<SessionSummary>>(emptyList())

    public val uiState: StateFlow<SettingsUiState> = combine(
        preferencesStore.darkMode,
        preferencesStore.serverPort,
        authManager.sessionCount,
        _portError,
        _sessionsList
    ) { darkMode, serverPort, activeSessions, portError, sessions ->
        SettingsUiState(
            darkMode = darkMode,
            serverPort = serverPort,
            activeSessions = activeSessions,
            portError = portError,
            sessionsList = sessions
        )
    }.stateIn(
        scope = viewModelScope,
        started = SharingStarted.WhileSubscribed(5000),
        initialValue = SettingsUiState()
    )

    init {
        authManager.refreshSessionCount()
        refreshSessionsList()
    }

    public fun setDarkMode(mode: String) {
        viewModelScope.launch {
            preferencesStore.setDarkMode(mode)
        }
    }

    public fun setServerPort(portInput: String): Boolean {
        val port = portInput.toIntOrNull()
        if (port == null || port !in 1024..65535) {
            _portError.value = "Port must be a valid number between 1024 and 65535"
            return false
        }
        _portError.value = null
        viewModelScope.launch {
            preferencesStore.setServerPort(port)
        }
        return true
    }

    public fun clearPortError() {
        _portError.value = null
    }

    public fun refreshSessionsList() {
        _sessionsList.value = authManager.getSessionsList()
    }

    public fun revokeSession(clientId: String) {
        authManager.revokeSession(clientId)
        refreshSessionsList()
    }

    public fun clearSessions() {
        authManager.revokeAllSessions()
        viewModelScope.launch {
            preferencesStore.setSessionId(null)
        }
        refreshSessionsList()
    }
}


