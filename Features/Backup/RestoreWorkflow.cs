using System;
using System.Threading;
using System.Threading.Tasks;
using VcfEditor.Core;
using VcfEditor.Core.Polling;

namespace VcfEditor.Features.Backup;

public interface IRestoreWorkflow
{
    Task<RestoreStartResponse> StartAsync(
        string archivePath,
        IProgress<double>? onProgress = null,
        CancellationToken ct = default);
    Task<RestoreStatusResponse> GetStatusAsync(
        string restoreId,
        CancellationToken cancellationToken);
    Task<RestoreStatusResponse> WaitUntilCompletedAsync(
        string restoreId,
        IProgress<RestoreStatusResponse>? progress = null,
        TimeSpan? pollInterval = null,
        CancellationToken cancellationToken = default);
}

public sealed class RestoreWorkflow : IRestoreWorkflow
{
    private readonly BackupApi _api;

    public RestoreWorkflow(BackupApi api)
    {
        ArgumentNullException.ThrowIfNull(api);
        _api = api;
    }

    public Task<RestoreStartResponse> StartAsync(
        string archivePath,
        IProgress<double>? onProgress = null,
        CancellationToken ct = default)
        => _api.StartRestoreAsync(archivePath, onProgress, ct);

    public Task<RestoreStatusResponse> GetStatusAsync(
        string restoreId,
        CancellationToken cancellationToken)
        => _api.GetRestoreStatusAsync(restoreId, cancellationToken);

    public async Task<RestoreStatusResponse> WaitUntilCompletedAsync(
        string restoreId,
        IProgress<RestoreStatusResponse>? progress = null,
        TimeSpan? pollInterval = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(restoreId);
        var delay = pollInterval ?? TimeSpan.FromMilliseconds(1500);
        return await OperationPollingPolicy.PollAsync(
                token => _api.GetRestoreStatusAsync(restoreId, token),
                status => status.Phase is "done" or "ready" or "complete" or "completed",
                status => status.Phase == "failed",
                interval: delay,
                timeout: TimeSpan.FromMinutes(30),
                maxAttempts: 1_200,
                cancellationToken: cancellationToken,
                progress: progress,
                failureMessage: status => $"Restore failed on device: {status.Error}")
            .ConfigureAwait(false);
    }
}
