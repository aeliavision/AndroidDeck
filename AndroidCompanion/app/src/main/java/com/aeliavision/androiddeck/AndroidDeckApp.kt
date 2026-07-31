package com.aeliavision.androiddeck

import android.app.Application
import androidx.hilt.work.HiltWorkerFactory
import androidx.work.Configuration
import com.aeliavision.androiddeck.core.notification.NotificationHelper
import dagger.hilt.android.HiltAndroidApp
import javax.inject.Inject

/**
 * Application class required by Hilt for dependency injection.
 * Creates all notification channels on startup so they are always registered
 * before any service or ViewModel posts a notification.
 *
 * AND-L09 FIX: Implements Configuration.Provider so WorkManager uses
 * HiltWorkerFactory — required for @HiltWorker injection to work.
 */
@HiltAndroidApp
class AndroidDeckApp : Application(), Configuration.Provider {

    @Inject lateinit var notificationHelper: NotificationHelper
    @Inject lateinit var workerFactory: HiltWorkerFactory

    override fun onCreate() {
        super.onCreate()
        // Create notification channels early — must be done before any notification
        // is posted. Safe to call multiple times (idempotent on API 26+).
        notificationHelper.createChannels(this)
    }

    // AND-L09 FIX: Provide custom WorkManager config using HiltWorkerFactory.
    // Without this, WorkManager cannot inject dependencies into @HiltWorker classes.
    override val workManagerConfiguration: Configuration
        get() = Configuration.Builder()
            .setWorkerFactory(workerFactory)
            .build()
}
