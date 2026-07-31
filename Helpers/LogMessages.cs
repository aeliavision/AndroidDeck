using System;
using System.Net;
using Microsoft.Extensions.Logging;

namespace VcfEditor.Helpers;

/// <summary>
/// Source-generated logging events for the desktop application.
/// Keeps message templates stable, avoids params-array allocations, and prevents
/// expensive formatting when a log level is disabled.
/// </summary>
internal static partial class LogMessages
{
    [LoggerMessage(1000, LogLevel.Information, "AndroidDeck started")]
    internal static partial void ApplicationStarted(ILogger logger);

    [LoggerMessage(1001, LogLevel.Information, "AndroidDeck exiting")]
    internal static partial void ApplicationExiting(ILogger logger);

    [LoggerMessage(1002, LogLevel.Error, "AppDomain unhandled exception")]
    internal static partial void AppDomainUnhandledException(ILogger logger, Exception exception);

    [LoggerMessage(1003, LogLevel.Error, "AppDomain unhandled exception object: {ExceptionObject}")]
    internal static partial void AppDomainUnhandledExceptionObject(ILogger logger, object? exceptionObject);

    [LoggerMessage(1004, LogLevel.Error, "Unobserved task exception")]
    internal static partial void UnobservedTaskException(ILogger logger, Exception exception);

    [LoggerMessage(1005, LogLevel.Information, "Application started at {StartTime}")]
    internal static partial void MainWindowStarted(ILogger logger, DateTime startTime);

    [LoggerMessage(1006, LogLevel.Information, "Contacts view loaded at {LoadTime}")]
    internal static partial void ContactsViewLoaded(ILogger logger, DateTime loadTime);

    [LoggerMessage(1100, LogLevel.Information, "Contacts fetch started")]
    internal static partial void ContactsFetchStarted(ILogger logger);

    [LoggerMessage(1101, LogLevel.Warning, "Contacts page {Page} repeated the previous IDs; paging stopped")]
    internal static partial void ContactsRepeatedPage(ILogger logger, int page);

    [LoggerMessage(1102, LogLevel.Information, "Contacts fetch completed with {Count} contacts")]
    internal static partial void ContactsFetchCompleted(ILogger logger, int count);

    [LoggerMessage(1103, LogLevel.Warning, "Contacts batch endpoint unavailable; using parallel detail requests")]
    internal static partial void ContactsBatchFallback(ILogger logger);

    [LoggerMessage(1104, LogLevel.Warning, "Failed to fetch contact detail {AndroidId}")]
    internal static partial void ContactDetailFetchFailed(ILogger logger, Exception exception, string androidId);

    [LoggerMessage(1105, LogLevel.Error, "Loading VCF file {FilePath} failed")]
    internal static partial void LoadVcfFailed(ILogger logger, Exception exception, string filePath);

    [LoggerMessage(1106, LogLevel.Error, "Saving VCF file {FilePath} failed")]
    internal static partial void SaveVcfFailed(ILogger logger, Exception exception, string? filePath);

    [LoggerMessage(1107, LogLevel.Error, "Refreshing contacts from phone failed")]
    internal static partial void RefreshContactsFailed(ILogger logger, Exception exception);

    [LoggerMessage(1108, LogLevel.Warning, "Phone operation {Action} failed: {Message}")]
    internal static partial void PhoneOperationFailed(ILogger logger, string action, string message);

    [LoggerMessage(1109, LogLevel.Warning, "Selected contact details could not be fetched")]
    internal static partial void SelectedContactFetchFailed(ILogger logger, Exception exception);

    [LoggerMessage(1110, LogLevel.Warning, "VCF input was truncated; emitted final contact without END:VCARD")]
    internal static partial void TruncatedVcf(ILogger logger);

    [LoggerMessage(1111, LogLevel.Information, "Exported {Count} contacts to VCF")]
    internal static partial void VcfExportCompleted(ILogger logger, int count);

    [LoggerMessage(1200, LogLevel.Information, "Downloaded {RemotePath} to {LocalPath}")]
    internal static partial void FileDownloaded(ILogger logger, string remotePath, string localPath);

    [LoggerMessage(1201, LogLevel.Information, "Chunked upload completed at {FinalPath}")]
    internal static partial void ChunkedUploadCompleted(ILogger logger, string? finalPath);

    [LoggerMessage(1202, LogLevel.Warning, "File browser operation failed")]
    internal static partial void FileBrowserFailed(ILogger logger, Exception exception);

    [LoggerMessage(1203, LogLevel.Warning, "File transfer failed")]
    internal static partial void FileTransferFailed(ILogger logger, Exception exception);

    [LoggerMessage(1300, LogLevel.Information, "Downloaded media {MediaId} to {LocalPath}")]
    internal static partial void MediaDownloaded(ILogger logger, string mediaId, string localPath);

    [LoggerMessage(1301, LogLevel.Warning, "Gallery initialization failed")]
    internal static partial void GalleryInitializationFailed(ILogger logger, Exception exception);

    [LoggerMessage(1302, LogLevel.Warning, "Gallery media load failed")]
    internal static partial void GalleryMediaLoadFailed(ILogger logger, Exception exception);

    [LoggerMessage(1303, LogLevel.Warning, "Gallery thumbnail background task failed")]
    internal static partial void GalleryThumbnailTaskFailed(ILogger logger, Exception? exception);

    [LoggerMessage(1304, LogLevel.Warning, "Gallery thumbnail fetch failed for media {MediaId}")]
    internal static partial void GalleryThumbnailFetchFailed(ILogger logger, Exception exception, string? mediaId);

    [LoggerMessage(1305, LogLevel.Warning, "Gallery preview load failed")]
    internal static partial void GalleryPreviewLoadFailed(ILogger logger, Exception exception);

    [LoggerMessage(1306, LogLevel.Warning, "Gallery JPEG decode failed")]
    internal static partial void GalleryJpegDecodeFailed(ILogger logger, Exception exception);

    [LoggerMessage(1400, LogLevel.Information, "HTTP {Method} {Url} returned {StatusCode}")]
    internal static partial void HttpRequestCompleted(ILogger logger, string method, string url, int statusCode);

    [LoggerMessage(1401, LogLevel.Information, "Pairing v3 returned {StatusCode}")]
    internal static partial void PairingResponseReceived(ILogger logger, HttpStatusCode statusCode);

    [LoggerMessage(1402, LogLevel.Warning, "Optional post-pair status check failed")]
    internal static partial void PairingStatusCheckFailed(ILogger logger, Exception exception);

    [LoggerMessage(1403, LogLevel.Warning, "Session recovery ping failed")]
    internal static partial void SessionRecoveryFailed(ILogger logger, Exception exception);

    [LoggerMessage(1404, LogLevel.Debug, "Heartbeat skipped because the previous request is still active")]
    internal static partial void HeartbeatSkipped(ILogger logger);

    [LoggerMessage(1405, LogLevel.Warning, "Heartbeat transient failure {Failure} of {Maximum}")]
    internal static partial void HeartbeatTransientFailure(ILogger logger, int failure, int maximum);

    [LoggerMessage(1500, LogLevel.Warning, "Unable to decrypt secret {SecretId}")]
    internal static partial void SecretDecryptFailed(ILogger logger, Exception exception, string secretId);

    [LoggerMessage(1501, LogLevel.Warning, "Secret store was corrupt and moved to {CorruptPath}")]
    internal static partial void SecretStoreCorrupt(ILogger logger, Exception exception, string corruptPath);

    [LoggerMessage(1502, LogLevel.Warning, "Settings file was corrupt and moved to {CorruptPath}")]
    internal static partial void SettingsFileCorrupt(ILogger logger, Exception exception, string corruptPath);

    [LoggerMessage(1503, LogLevel.Warning, "Skipped malformed legacy backup seed {SeedId}")]
    internal static partial void LegacySeedMalformed(ILogger logger, Exception exception, string seedId);

    [LoggerMessage(1504, LogLevel.Information, "Migrated {SeedCount} legacy backup seeds to protected storage")]
    internal static partial void LegacySeedsMigrated(ILogger logger, int seedCount);

    [LoggerMessage(1600, LogLevel.Information, "VCF file dropped: {FilePath}")]
    internal static partial void VcfFileDropped(ILogger logger, string filePath);

    [LoggerMessage(1601, LogLevel.Error, "Processing dropped files failed")]
    internal static partial void DroppedFileProcessingFailed(ILogger logger, Exception exception);

    [LoggerMessage(1602, LogLevel.Error, "{Entry}")]
    internal static partial void DiagnosticError(ILogger logger, Exception? exception, string entry);

    [LoggerMessage(1603, LogLevel.Warning, "{Entry}")]
    internal static partial void DiagnosticWarning(ILogger logger, Exception? exception, string entry);

    [LoggerMessage(1604, LogLevel.Information, "{Entry}")]
    internal static partial void DiagnosticInformation(ILogger logger, Exception? exception, string entry);

    [LoggerMessage(1700, LogLevel.Warning, "Recoverable UI exception {CorrelationId}")]
    internal static partial void RecoverableUiException(ILogger logger, Exception exception, string correlationId);

    [LoggerMessage(1701, LogLevel.Critical, "Fatal UI exception {CorrelationId}")]
    internal static partial void FatalUiException(ILogger logger, Exception exception, string correlationId);

    [LoggerMessage(1800, LogLevel.Error, "Downloading backup history entry failed")]
    internal static partial void BackupHistoryDownloadFailed(ILogger logger, Exception exception);

    [LoggerMessage(1801, LogLevel.Error, "Restoring backup history entry failed")]
    internal static partial void BackupHistoryRestoreFailed(ILogger logger, Exception exception);

    [LoggerMessage(1802, LogLevel.Warning, "Loading backup manifest failed")]
    internal static partial void BackupManifestLoadFailed(ILogger logger, Exception exception);

    [LoggerMessage(1803, LogLevel.Information, "Backup download starting: backupId={BackupId}, archiveSize={ArchiveSize}, types={Types}")]
    internal static partial void BackupDownloadStarting(ILogger logger, string backupId, long archiveSize, string types);

    [LoggerMessage(1804, LogLevel.Error, "Creating backup timed out")]
    internal static partial void BackupCreateTimedOut(ILogger logger, Exception exception);

    [LoggerMessage(1805, LogLevel.Error, "Creating backup failed")]
    internal static partial void BackupCreateFailed(ILogger logger, Exception exception);

    [LoggerMessage(1806, LogLevel.Error, "Restoring backup failed")]
    internal static partial void BackupRestoreFailed(ILogger logger, Exception exception);

    [LoggerMessage(1807, LogLevel.Warning, "Refreshing backup history failed")]
    internal static partial void BackupHistoryRefreshFailed(ILogger logger, Exception exception);

    [LoggerMessage(1808, LogLevel.Warning, "Displaying backup workflow dialog failed")]
    internal static partial void BackupDialogFailed(ILogger logger, Exception exception);

    [LoggerMessage(1809, LogLevel.Warning, "Reading backup archive preview failed for {ArchivePath}")]
    internal static partial void BackupPreviewFailed(ILogger logger, Exception exception, string archivePath);

    [LoggerMessage(1810, LogLevel.Warning, "Deleting temporary backup file failed for {TemporaryPath}")]
    internal static partial void BackupTemporaryFileCleanupFailed(ILogger logger, Exception exception, string temporaryPath);

    [LoggerMessage(1900, LogLevel.Information, "User verified certificate fingerprint {CertificateFingerprint}")]
    internal static partial void CertificateFingerprintVerified(ILogger logger, string? certificateFingerprint);

    [LoggerMessage(1901, LogLevel.Warning, "Capability discovery failed for {Endpoint}")]
    internal static partial void CapabilityDiscoveryFailed(ILogger logger, Exception exception, string? endpoint);

    [LoggerMessage(2000, LogLevel.Error, "Shell navigation to {DestinationKey} failed")]
    internal static partial void ShellNavigationFailed(ILogger logger, Exception exception, string destinationKey);
    [LoggerMessage(2100, LogLevel.Warning, "WPF UI thread stall detected: {StallMilliseconds:F0} ms beyond the sampling interval")]
    internal static partial void UiThreadStallDetected(ILogger logger, double stallMilliseconds);

}
