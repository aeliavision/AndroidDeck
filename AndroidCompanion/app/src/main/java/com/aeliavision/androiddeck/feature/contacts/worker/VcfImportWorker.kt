package com.aeliavision.androiddeck.feature.contacts.worker

import android.content.Context
import android.net.Uri
import androidx.hilt.work.HiltWorker
import androidx.work.CoroutineWorker
import androidx.work.Data
import androidx.work.OneTimeWorkRequestBuilder
import androidx.work.WorkManager
import androidx.work.WorkerParameters
import androidx.core.net.toUri
import com.aeliavision.androiddeck.core.notification.NotificationHelper
import com.aeliavision.androiddeck.feature.contacts.data.ContactsRepository
import com.aeliavision.androiddeck.feature.dashboard.data.ActivityLogRepository
import com.aeliavision.androiddeck.feature.dashboard.model.ActivityType
import dagger.assisted.Assisted
import dagger.assisted.AssistedInject

/**
 * AND-L09 FIX: WorkManager-backed VCF import worker.
 */
@HiltWorker
class VcfImportWorker @AssistedInject constructor(
    @Assisted context: Context,
    @Assisted params: WorkerParameters,
    private val repository: ContactsRepository,
    private val activityLogRepository: ActivityLogRepository,
    private val notificationHelper: NotificationHelper
) : CoroutineWorker(context, params) {

    override suspend fun doWork(): Result {
        val uriString = inputData.getString(KEY_URI) ?: return Result.failure()
        val accountName = inputData.getString(KEY_ACCOUNT_NAME)
        val accountType = inputData.getString(KEY_ACCOUNT_TYPE)

        return try {
            val uri = uriString.toUri()
            val stream = applicationContext.contentResolver.openInputStream(uri)
                ?: return Result.failure()

            val importResult = stream.use { repository.importVcf(it, accountName, accountType) }
            activityLogRepository.logActivity(
                title = "Imported ${importResult.imported}/${importResult.total} contacts",
                type = ActivityType.IMPORT
            )

            notificationHelper.notifyImportComplete(
                context = applicationContext,
                imported = importResult.imported,
                total = importResult.total,
                failed = importResult.failed,
                skipped = importResult.skipped
            )

            Result.success(
                Data.Builder()
                    .putInt(KEY_RESULT_IMPORTED, importResult.imported)
                    .putInt(KEY_RESULT_TOTAL, importResult.total)
                    .putInt(KEY_RESULT_FAILED, importResult.failed)
                    .putInt(KEY_RESULT_SKIPPED, importResult.skipped)
                    .build()
            )
        } catch (e: Exception) {
            if (runAttemptCount < 3) Result.retry() else Result.failure()
        }
    }

    companion object {
        const val KEY_URI = "vcf_uri"
        const val KEY_ACCOUNT_NAME = "account_name"
        const val KEY_ACCOUNT_TYPE = "account_type"
        const val KEY_RESULT_IMPORTED = "result_imported"
        const val KEY_RESULT_TOTAL = "result_total"
        const val KEY_RESULT_FAILED = "result_failed"
        const val KEY_RESULT_SKIPPED = "result_skipped"

        fun buildRequest(
            uri: Uri,
            accountName: String? = null,
            accountType: String? = null
        ) = OneTimeWorkRequestBuilder<VcfImportWorker>()
            .setInputData(
                Data.Builder()
                    .putString(KEY_URI, uri.toString())
                    .putString(KEY_ACCOUNT_NAME, accountName)
                    .putString(KEY_ACCOUNT_TYPE, accountType)
                    .build()
            )
            .build()
    }
}
