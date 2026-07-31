package com.aeliavision.androiddeck.feature.server.data

import android.content.ComponentName
import android.content.Context
import android.content.Intent
import android.content.ServiceConnection
import android.net.ConnectivityManager
import android.net.LinkProperties
import android.net.NetworkCapabilities
import android.os.Build
import android.os.IBinder
import android.util.Log
import com.aeliavision.androiddeck.feature.dashboard.data.ActivityLogRepository
import com.aeliavision.androiddeck.feature.dashboard.model.ActivityType
import com.aeliavision.androiddeck.feature.server.service.ContactsServerService
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.launch
import java.net.Inet4Address
import javax.inject.Inject
import javax.inject.Singleton

data class ServerUiState(
    val isRunning: Boolean = false,
    val ipAddress: String = "--",
    val port: Int = ContactsServerService.DEFAULT_PORT,
    val pairingCode: String = "------",
    val clientCount: Int = 0
)


@Singleton
class ServerManager @Inject constructor(
    private val activityLogRepository: ActivityLogRepository
) {

    private val scope = CoroutineScope(Dispatchers.IO)

    companion object {
        private const val TAG = "ServerManager"
    }

    private val _uiState = MutableStateFlow(ServerUiState())
    val uiState: StateFlow<ServerUiState> = _uiState.asStateFlow()

    private var serverService: ContactsServerService? = null
    private var bound = false

    private val connection = object : ServiceConnection {
        override fun onServiceConnected(name: ComponentName?, binder: IBinder?) {
            val localBinder = binder as? ContactsServerService.LocalBinder ?: return
            serverService = localBinder.getService()
            bound = true
            Log.d(TAG, "Service connected")
            refresh()
        }

        override fun onServiceDisconnected(name: ComponentName?) {
            serverService = null
            bound = false
            Log.d(TAG, "Service disconnected")
            _uiState.value = ServerUiState()
        }
    }

    fun bind(context: Context) {
        val intent = Intent(context, ContactsServerService::class.java)
        context.bindService(intent, connection, Context.BIND_AUTO_CREATE)
    }

    fun unbind(context: Context) {
        if (bound) {
            context.unbindService(connection)
            bound = false
        }
    }

    fun startServer(context: Context) {
        val ip = getWifiIpAddress(context)
        val intent = Intent(context, ContactsServerService::class.java).apply {
            putExtra(ContactsServerService.EXTRA_PORT, ContactsServerService.DEFAULT_PORT)
            putExtra(ContactsServerService.EXTRA_IP, ip)
        }
        context.startForegroundService(intent)
        bind(context)
        scope.launch { activityLogRepository.logActivity("Sync server started", ActivityType.SYNC_START) }
    }

    fun stopServer(context: Context) {
        val intent = Intent(context, ContactsServerService::class.java)
        context.stopService(intent)
        unbind(context)
        _uiState.value = ServerUiState()
        scope.launch { activityLogRepository.logActivity("Sync server stopped", ActivityType.SYNC_STOP) }
    }

    fun regeneratePairingCode() {
        serverService?.getAuthManager()?.regeneratePairingCode()
        refresh()
    }

    /** Pull current state from the bound service and push to [uiState]. */
    fun refresh() {

        val service = serverService ?: return
        val running = service.isServerRunning()
        _uiState.value = _uiState.value.copy(
            isRunning = running,
            pairingCode = service.getAuthManager().currentPairingCode,
            clientCount = service.getAuthManager().getActiveSessionCount()
        )
    }

    fun updateIpAddress(context: Context) {
        _uiState.value = _uiState.value.copy(ipAddress = getWifiIpAddress(context))
    }

    @Suppress("DEPRECATION")
    private fun getWifiIpAddress(context: Context): String {
        // Android 12+: use ConnectivityManager.getLinkProperties
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.S) {
            val cm = context.getSystemService(Context.CONNECTIVITY_SERVICE) as ConnectivityManager
            val network = cm.activeNetwork ?: return "--"
            val caps = cm.getNetworkCapabilities(network) ?: return "--"
            if (!caps.hasTransport(NetworkCapabilities.TRANSPORT_WIFI)) return "--"
            val lp: LinkProperties = cm.getLinkProperties(network) ?: return "--"
            val addr = lp.linkAddresses
                .map { it.address }
                .filterIsInstance<Inet4Address>()
                .firstOrNull { !it.isLoopbackAddress }
            return addr?.hostAddress ?: "--"
        }

        // Legacy path for Android 10/11
        val wm = context.applicationContext
            .getSystemService(Context.WIFI_SERVICE) as android.net.wifi.WifiManager
        val ip = wm.connectionInfo.ipAddress
        if (ip == 0) return "--"
        return "${ip and 0xFF}.${ip shr 8 and 0xFF}.${ip shr 16 and 0xFF}.${ip shr 24 and 0xFF}"
    }
}
