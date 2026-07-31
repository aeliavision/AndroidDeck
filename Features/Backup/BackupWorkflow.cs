using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using VcfEditor.Core;
using VcfEditor.Core.Polling;

namespace VcfEditor.Features.Backup;

public interface IBackupWorkflow
{
    Task<BackupManifestResponse> GetManifestAsync(CancellationToken cancellationToken);
    Task<BackupCreateResponse> CreateAsync(
        IEnumerable<string> types,
        IEnumerable<string>? extraPaths = null,
        bool encrypt = true,
        bool incremental = false,
        long? sinceMs = null,
        CancellationToken ct = default);
    Task<BackupStatusResponse> GetStatusAsync(
        string backupId,
        CancellationToken cancellationToken);
    Task<BackupStatusResponse> WaitUntilReadyAsync(
        string backupId,
        IProgress<BackupStatusResponse>? progress = null,
        TimeSpan? pollInterval = null,
        CancellationToken cancellationToken = default);
    Task DownloadAsync(
        string backupId,
        string destinationPath,
        long totalBytes,
        IProgress<double>? onProgress = null,
        CancellationToken ct = default);
}

public sealed class BackupWorkflow : IBackupWorkflow
{
    private readonly BackupApi _api;

    public BackupWorkflow(BackupApi api)
    {
        ArgumentNullException.ThrowIfNull(api);
        _api = api;
    }

    public Task<BackupManifestResponse> GetManifestAsync(CancellationToken cancellationToken)
        => _api.GetManifestAsync(cancellationToken);

    public Task<BackupCreateResponse> CreateAsync(
        IEnumerable<string> types,
        IEnumerable<string>? extraPaths = null,
        bool encrypt = true,
        bool incremental = false,
        long? sinceMs = null,
        CancellationToken ct = default)
        => _api.CreateBackupAsync(
            types,
            extraPaths,
            encrypt,
            incremental,
            sinceMs,
            ct);

    public Task<BackupStatusResponse> GetStatusAsync(
        string backupId,
        CancellationToken cancellationToken)
        => _api.GetStatusAsync(backupId, cancellationToken);

    public async Task<BackupStatusResponse> WaitUntilReadyAsync(
        string backupId,
        IProgress<BackupStatusResponse>? progress = null,
        TimeSpan? pollInterval = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(backupId);
        var delay = pollInterval ?? TimeSpan.FromMilliseconds(1500);
        return await OperationPollingPolicy.PollAsync(
                token => _api.GetStatusAsync(backupId, token),
                status => status.Phase is "ready" or "done" or "complete" or "completed",
                status => status.Phase == "failed",
                interval: delay,
                timeout: TimeSpan.FromMinutes(30),
                maxAttempts: 1_200,
                cancellationToken: cancellationToken,
                progress: progress,
                failureMessage: status => $"Backup failed on device: {status.Error}")
            .ConfigureAwait(false);
    }

    public Task DownloadAsync(
        string backupId,
        string destinationPath,
        long totalBytes,
        IProgress<double>? onProgress = null,
        CancellationToken ct = default)
        => _api.DownloadArchiveAsync(
            backupId,
            destinationPath,
            totalBytes,
            onProgress,
            ct);
}
