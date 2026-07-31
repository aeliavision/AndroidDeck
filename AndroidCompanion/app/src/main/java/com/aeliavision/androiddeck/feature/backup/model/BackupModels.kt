package com.aeliavision.androiddeck.feature.backup.model

// -- Backup type flags ---------------------------------------------------------

enum class BackupType { CONTACTS, GALLERY, FILES }

// -- Request / Response DTOs (JSON) -------------------------------------------

/**
 * POST /api/v2/backup/create
 * Body: { types: ["contacts","gallery","files"], paths: ["/sdcard/Documents"] }
 */
data class BackupCreateRequest(
    /** Which data types to include in this backup. */
    val types: List<String> = listOf("contacts", "gallery", "files"),
    /** Extra file-system paths to include (type = FILES). Defaults to Downloads + Documents. */
    val paths: List<String> = emptyList(),
    /** If false, produce a plaintext ZIP archive instead of an encrypted .vcfbak. */
    val encrypt: Boolean = true,
    /** If true, only include items modified since [sinceMs] (MVP: gallery + files). */
    val incremental: Boolean = false,
    /** Epoch ms watermark; if null and incremental=true, server uses last saved watermark per type. */
    val sinceMs: Long? = null
)

/**
 * POST /api/v2/backup/create → 202 Accepted
 */
data class BackupCreateResponse(
    val backupId: String,
    /** Estimated total items (contacts + media + files). */
    val estimatedItemCount: Int,
    val status: String = "started"
)

/**
 * GET /api/v2/backup/{backupId}/status
 */
data class BackupStatusResponse(
    val backupId: String,
    /** 0.0 – 1.0 */
    val progress: Float,
    /** "indexing" | "packaging" | "encrypting" | "ready" | "failed" */
    val phase: String,
    val currentItem: String = "",
    val itemCount: Int = 0,
    val processedItems: Int = 0,
    /** Size in bytes of the completed archive (only set when phase == "ready"). */
    val archiveSize: Long = 0L,
    val error: String? = null
)

/**
 * POST /api/v2/backup/restore → 202 Accepted
 */
data class RestoreStartResponse(
    val restoreId: String,
    val status: String = "started"
)

/**
 * GET /api/v2/backup/restore/{restoreId}/status
 */
data class RestoreStatusResponse(
    val restoreId: String,
    val progress: Float,
    /** "extracting" | "restoring_contacts" | "restoring_files" | "done" | "failed" */
    val phase: String,
    val restoredItems: Int = 0,
    val failedItems: Int = 0,
    val skippedItems: Int = 0,
    val error: String? = null
)

/**
 * GET /api/v2/backup/history — one entry per completed backup.
 */
data class BackupHistoryEntry(
    val backupId: String,
    val createdAt: Long,          // epoch ms
    val types: List<String>,
    val archiveSize: Long,
    val itemCount: Int
)

data class BackupHistoryResponse(
    val backups: List<BackupHistoryEntry>
)

/**
 * GET /api/v2.1/backup/manifest
 * Capability + defaults discovery for the desktop Backup UI.
 */
data class BackupManifestResponse(
    val version: Int = 1,
    /** Supported type strings accepted by POST /api/v2/backup/create */
    val supportedTypes: List<String> = listOf("contacts", "gallery", "files"),
    /** Recommended default file roots for type="files" */
    val defaultPaths: List<String> = emptyList(),
    /** Reserved for future incremental backup support. */
    val supportsIncremental: Boolean = true
)

// -- Internal state (not serialised) ------------------------------------------

enum class BackupPhase {
    INDEXING, PACKAGING, ENCRYPTING, READY, FAILED
}

data class BackupState(
    val backupId: String,
    @Volatile var phase: BackupPhase = BackupPhase.INDEXING,
    @Volatile var progress: Float = 0f,
    @Volatile var currentItem: String = "",
    @Volatile var itemCount: Int = 0,
    @Volatile var processedItems: Int = 0,
    @Volatile var archiveFile: java.io.File? = null,
    @Volatile var archiveSize: Long = 0L,
    @Volatile var error: String? = null,
    val types: List<String> = emptyList(),
    val createdAt: Long = System.currentTimeMillis()
)

enum class RestorePhase {
    EXTRACTING, RESTORING_CONTACTS, RESTORING_FILES, DONE, FAILED
}

data class RestoreState(
    val restoreId: String,
    @Volatile var phase: RestorePhase = RestorePhase.EXTRACTING,
    @Volatile var progress: Float = 0f,
    @Volatile var restoredItems: Int = 0,
    @Volatile var failedItems: Int = 0,
    @Volatile var skippedItems: Int = 0,
    @Volatile var error: String? = null
)
