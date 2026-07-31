package com.aeliavision.androiddeck.feature.server.viewmodel

import android.content.Context
import androidx.lifecycle.ViewModel
import androidx.lifecycle.viewModelScope
import com.aeliavision.androiddeck.feature.server.data.ServerManager
import com.aeliavision.androiddeck.feature.server.data.ServerUiState
import dagger.hilt.android.lifecycle.HiltViewModel
import kotlinx.coroutines.currentCoroutineContext
import kotlinx.coroutines.delay
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.isActive
import kotlinx.coroutines.launch
import javax.inject.Inject
import kotlin.time.Duration.Companion.milliseconds

/**
 * ViewModel for the Server screen.
 */
@HiltViewModel
public class ServerViewModel @Inject constructor(
    private val serverManager: ServerManager
) : ViewModel() {

    public val uiState: StateFlow<ServerUiState> = serverManager.uiState

    init {
        startAutoRefresh()
    }

    private fun startAutoRefresh() {
        viewModelScope.launch {
            while (currentCoroutineContext().isActive) {
                serverManager.refresh()
                val isRunning = serverManager.uiState.value.isRunning
                val pollInterval = if (isRunning) 5_000L else 15_000L
                delay(pollInterval.milliseconds)
            }
        }
    }

    public fun startServer(context: Context) {
        serverManager.startServer(context)
        serverManager.updateIpAddress(context)
    }

    public fun stopServer(context: Context) {
        serverManager.stopServer(context)
    }

    public fun regeneratePairingCode() {
        serverManager.regeneratePairingCode()
    }

    public fun bind(context: Context) {
        serverManager.bind(context)
        serverManager.updateIpAddress(context)
    }

    public fun unbind(context: Context) {
        serverManager.unbind(context)
    }
}


