package com.aeliavision.androiddeck.feature.filesystem.ui

import android.content.Context
import android.content.Intent
import android.os.Build
import android.os.Environment
import android.provider.Settings
import androidx.activity.result.ActivityResultLauncher
import androidx.activity.result.contract.ActivityResultContracts
import androidx.core.net.toUri
import androidx.fragment.app.FragmentActivity

/**
 */
class StoragePermissionHelper(
    private val onResult: (granted: Boolean) -> Unit
) {
    private var manageStorageLauncher: ActivityResultLauncher<Intent>? = null
    private var legacyStorageLauncher: ActivityResultLauncher<String>? = null

    fun register(activity: FragmentActivity) {
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.R) {
            manageStorageLauncher = activity.registerForActivityResult(
                ActivityResultContracts.StartActivityForResult()
            ) {
                onResult(Environment.isExternalStorageManager())
            }
        } else {
            legacyStorageLauncher = activity.registerForActivityResult(
                ActivityResultContracts.RequestPermission()
            ) { granted ->
                onResult(granted)
            }
        }
    }

    fun isGranted(context: Context): Boolean {
        return if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.R) {
            Environment.isExternalStorageManager()
        } else {
            context.checkSelfPermission(android.Manifest.permission.READ_EXTERNAL_STORAGE) ==
                android.content.pm.PackageManager.PERMISSION_GRANTED
        }
    }

    fun requestPermission(context: Context) {
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.R) {
            val intent = Intent(
                Settings.ACTION_MANAGE_APP_ALL_FILES_ACCESS_PERMISSION,
                "package:${context.packageName}".toUri()
            )
            manageStorageLauncher?.launch(intent)
                ?: throw IllegalStateException("Call register() before requestPermission()")
        } else {
            legacyStorageLauncher?.launch(android.Manifest.permission.READ_EXTERNAL_STORAGE)
                ?: throw IllegalStateException("Call register() before requestPermission()")
        }
    }

    companion object {
        fun rationaleMessage(): String = buildString {
            append("AndroidDeck needs access to your phone's storage to let you ")
            append("browse, download, and upload files from your desktop.\n\n")
            if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.R) {
                append("You will be taken to Settings to grant \"All Files Access\" ")
                append("(MANAGE_EXTERNAL_STORAGE). This is required on Android 11+.")
            } else {
                append("\"Read External Storage\" permission is required to access files.")
            }
        }
    }
}
