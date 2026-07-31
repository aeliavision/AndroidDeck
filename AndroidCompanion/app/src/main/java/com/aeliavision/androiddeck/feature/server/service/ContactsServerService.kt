package com.aeliavision.androiddeck.feature.server.service

import android.app.Notification
import android.app.NotificationChannel
import android.app.NotificationManager
import android.app.PendingIntent
import android.app.Service
import android.content.Intent
import android.Manifest
import android.content.pm.PackageManager
import android.os.Binder
import android.os.Build
import android.os.Environment
import android.os.IBinder
import androidx.core.content.ContextCompat
import androidx.core.app.NotificationCompat
import com.aeliavision.androiddeck.R
import com.aeliavision.androiddeck.MainActivity
import com.aeliavision.androiddeck.feature.contacts.data.ContactsRepository
import com.aeliavision.androiddeck.feature.filesystem.data.FileSystemRepository
import com.aeliavision.androiddeck.feature.filesystem.data.StreamTransferManager
import com.aeliavision.androiddeck.feature.gallery.data.GalleryRepository
import com.aeliavision.androiddeck.feature.backup.data.BackupManager
import com.aeliavision.androiddeck.feature.backup.data.RestoreManager
import com.aeliavision.androiddeck.feature.backup.data.BackupKeyStore
import dagger.hilt.android.AndroidEntryPoint
import javax.inject.Inject
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.cancel
import kotlinx.coroutines.launch

/**
 * Foreground service that keeps the HTTP server running.
 * Shows a persistent notification so the user knows the server is active.
 */
@AndroidEntryPoint
class ContactsServerService : Service() {

    companion object {
        private const val NOTIFICATION_ID = 1001
        private const val CHANNEL_ID = "vcfeditor_server"

        const val DEFAULT_PORT = 8732

        const val EXTRA_PORT = "port"
        const val EXTRA_IP = "ip"
    }

    private var server: ContactsKtorServer? = null
    @Inject lateinit var injectedAuthManager: AuthManager
    private val binder = LocalBinder()

    @Inject lateinit var repository: ContactsRepository

    @Inject lateinit var fileSystemRepository: FileSystemRepository
    @Inject lateinit var streamTransferManager: StreamTransferManager

    @Inject lateinit var galleryRepository: GalleryRepository

    @Inject lateinit var backupManager: BackupManager
    @Inject lateinit var restoreManager: RestoreManager
    @Inject lateinit var backupKeyStore: BackupKeyStore

    private val serviceScope = CoroutineScope(SupervisorJob() + Dispatchers.Main)

    inner class LocalBinder : Binder() {
        fun getService(): ContactsServerService = this@ContactsServerService
    }

    override fun onBind(intent: Intent?): IBinder = binder

    override fun onCreate() {
        super.onCreate()
        createNotificationChannel()
    }

    override fun onStartCommand(intent: Intent?, flags: Int, startId: Int): Int {
        val port = intent?.getIntExtra(EXTRA_PORT, DEFAULT_PORT) ?: DEFAULT_PORT
        val ip = intent?.getStringExtra(EXTRA_IP) ?: "0.0.0.0"

        startServer(port)

        val notification = buildNotification(ip, port)
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.UPSIDE_DOWN_CAKE) {
            startForeground(NOTIFICATION_ID, notification, 
                android.content.pm.ServiceInfo.FOREGROUND_SERVICE_TYPE_DATA_SYNC or 
                android.content.pm.ServiceInfo.FOREGROUND_SERVICE_TYPE_SPECIAL_USE)
        } else {
            startForeground(NOTIFICATION_ID, notification, android.content.pm.ServiceInfo.FOREGROUND_SERVICE_TYPE_DATA_SYNC)
        }

        return START_STICKY
    }

    override fun onDestroy() {
        stopServer()
        backupManager.shutdown()
        serviceScope.cancel()
        super.onDestroy()
    }

    override fun onTaskRemoved(rootIntent: Intent?) {
        stopServer()
        backupManager.shutdown()
        stopForeground(STOP_FOREGROUND_REMOVE)
        stopSelf()
        super.onTaskRemoved(rootIntent)
    }

    fun getAuthManager(): AuthManager = injectedAuthManager

    fun isServerRunning(): Boolean = server?.isRunning == true

    private fun startServer(port: Int) {
        stopServer()
        val deviceName = "${Build.MANUFACTURER} ${Build.MODEL}"
        serviceScope.launch {
            val keyStore = TlsHelper.loadOrCreateServerKeyStore(this@ContactsServerService)
            val keystorePassword = TlsHelper.getKeystorePasswordAsync(this@ContactsServerService)
            val filesGranted = if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.R) {
                Environment.isExternalStorageManager()
            } else {
                ContextCompat.checkSelfPermission(this@ContactsServerService, Manifest.permission.READ_EXTERNAL_STORAGE) ==
                    PackageManager.PERMISSION_GRANTED
            }

            val mediaGranted = if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.TIRAMISU) {
                ContextCompat.checkSelfPermission(this@ContactsServerService, Manifest.permission.READ_MEDIA_IMAGES) ==
                    PackageManager.PERMISSION_GRANTED &&
                    ContextCompat.checkSelfPermission(this@ContactsServerService, Manifest.permission.READ_MEDIA_VIDEO) ==
                    PackageManager.PERMISSION_GRANTED
            } else {
                ContextCompat.checkSelfPermission(this@ContactsServerService, Manifest.permission.READ_EXTERNAL_STORAGE) ==
                    PackageManager.PERMISSION_GRANTED
            }

            server = ContactsKtorServer(
                port = port,
                repository = repository,
                authManager = injectedAuthManager,
                deviceName = deviceName,
                keyStore = keyStore,
                keyStorePassword = keystorePassword,
                fileSystemRepository = if (filesGranted) fileSystemRepository else null,
                streamTransferManager = if (filesGranted) streamTransferManager else null,
                galleryRepository = if (mediaGranted) galleryRepository else null,
                requiresAllFilesAccess = (Build.VERSION.SDK_INT >= Build.VERSION_CODES.R) && !filesGranted,
                requiresMediaPermissions = !mediaGranted,
                supportsBackup = true,
                backupManager = backupManager,
                restoreManager = restoreManager,
                backupKeyStore = backupKeyStore
            )
            server?.start()
        }
    }

    private fun stopServer() {
        server?.stop()
        server = null
    }

    private fun createNotificationChannel() {
        val channel = NotificationChannel(
            CHANNEL_ID,
            getString(R.string.notification_channel_server),
            NotificationManager.IMPORTANCE_LOW
        ).apply {
            description = getString(R.string.notification_channel_server_desc)
            setShowBadge(false)
        }

        val manager = getSystemService(NotificationManager::class.java)
        manager.createNotificationChannel(channel)
    }

    private fun buildNotification(ip: String, port: Int): Notification {
        val intent = Intent(this, MainActivity::class.java)
        val pendingIntent = PendingIntent.getActivity(
            this, 0, intent,
            PendingIntent.FLAG_UPDATE_CURRENT or PendingIntent.FLAG_IMMUTABLE
        )

        return NotificationCompat.Builder(this, CHANNEL_ID)
            .setContentTitle(getString(R.string.notification_title))
            .setContentText(getString(R.string.notification_text, ip, port))
            .setSmallIcon(R.drawable.ic_notification)
            .setContentIntent(pendingIntent)
            .setOngoing(true)
            .setPriority(NotificationCompat.PRIORITY_LOW)
            .build()
    }
}
