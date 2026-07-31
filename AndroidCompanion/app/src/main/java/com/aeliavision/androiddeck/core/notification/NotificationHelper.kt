package com.aeliavision.androiddeck.core.notification

import android.app.NotificationChannel
import android.app.NotificationManager
import android.app.PendingIntent
import android.content.Context
import android.content.Intent
import android.os.Build
import androidx.core.app.NotificationCompat
import androidx.core.app.NotificationManagerCompat
import com.aeliavision.androiddeck.MainActivity
import com.aeliavision.androiddeck.R
import javax.inject.Inject
import javax.inject.Singleton

/**
 * Centralised notification helper for all app-level notifications.
 *
 * Channels:
 *  - [CHANNEL_SERVER]  — persistent low-priority server running notification
 *  - [CHANNEL_IMPORT]  — one-shot import/export completion notifications
 */
@Singleton
class NotificationHelper @Inject constructor() {

    companion object {
        const val CHANNEL_SERVER = "vcfeditor_server"
        const val CHANNEL_IMPORT = "vcfeditor_import"

        const val NOTIF_ID_SERVER  = 1001
        const val NOTIF_ID_IMPORT  = 1002
        const val NOTIF_ID_EXPORT  = 1003
    }

    fun createChannels(context: Context) {
        val manager = context.getSystemService(NotificationManager::class.java)

        // Server status channel — low importance, no sound, no badge
        manager.createNotificationChannel(
            NotificationChannel(
                CHANNEL_SERVER,
                context.getString(R.string.notification_channel_server),
                NotificationManager.IMPORTANCE_LOW
            ).apply {
                description = context.getString(R.string.notification_channel_server_desc)
                setShowBadge(false)
            }
        )

        // Import/export results channel — default importance so it heads-up
        manager.createNotificationChannel(
            NotificationChannel(
                CHANNEL_IMPORT,
                context.getString(R.string.notification_channel_import),
                NotificationManager.IMPORTANCE_DEFAULT
            ).apply {
                description = context.getString(R.string.notification_channel_import_desc)
                setShowBadge(true)
            }
        )
    }

    /**
     * Post an import-completed notification.
     * @param total   total contacts in the VCF file
     * @param imported  successfully imported count
     * @param failed   count that failed
     */
    // AND-L06 FIX: Added skipped parameter so duplicate-detection results are
    fun notifyImportComplete(context: Context, total: Int, imported: Int, failed: Int, skipped: Int = 0) {
        val notificationManager = NotificationManagerCompat.from(context)
        if (!notificationManager.areNotificationsEnabled()) return

        val tapIntent = PendingIntent.getActivity(
            context, 0,
            Intent(context, MainActivity::class.java),
            PendingIntent.FLAG_UPDATE_CURRENT or PendingIntent.FLAG_IMMUTABLE
        )

        val title = context.getString(R.string.notif_import_title)
        val res = context.resources
        val text = when {
            failed == 0 && skipped == 0 -> res.getQuantityString(R.plurals.notif_import_success, imported, imported)
            imported == 0 -> res.getQuantityString(R.plurals.notif_import_all_failed, total, total)
            else -> buildString {
                append(res.getQuantityString(R.plurals.notif_import_partial, imported, imported, total, failed))
                if (skipped > 0) append(" ($skipped duplicate(s) skipped)")
            }
        }

        val notification = NotificationCompat.Builder(context, CHANNEL_IMPORT)
            .setSmallIcon(R.drawable.ic_notification)
            .setContentTitle(title)
            .setContentText(text)
            .setStyle(NotificationCompat.BigTextStyle().bigText(text))
            .setContentIntent(tapIntent)
            .setAutoCancel(true)
            .setPriority(NotificationCompat.PRIORITY_DEFAULT)
            .build()

        @Suppress("MissingPermission")
        notificationManager.notify(NOTIF_ID_IMPORT, notification)
    }

    /**
     * Post an export-completed notification.
     */
    fun notifyExportComplete(context: Context, count: Int, filePath: String) {
        val notificationManager = NotificationManagerCompat.from(context)
        if (!notificationManager.areNotificationsEnabled()) return

        val tapIntent = PendingIntent.getActivity(
            context, 0,
            Intent(context, MainActivity::class.java),
            PendingIntent.FLAG_UPDATE_CURRENT or PendingIntent.FLAG_IMMUTABLE
        )

        val res = context.resources
        val text = res.getQuantityString(R.plurals.notif_export_success, count, count, filePath)

        val notification = NotificationCompat.Builder(context, CHANNEL_IMPORT)
            .setSmallIcon(R.drawable.ic_notification)
            .setContentTitle(context.getString(R.string.notif_export_title))
            .setContentText(text)
            .setStyle(NotificationCompat.BigTextStyle().bigText(text))
            .setContentIntent(tapIntent)
            .setAutoCancel(true)
            .setPriority(NotificationCompat.PRIORITY_DEFAULT)
            .build()

        @Suppress("MissingPermission")
        notificationManager.notify(NOTIF_ID_EXPORT, notification)
    }
}
