using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using VcfEditor.Core;

namespace VcfEditor.Features.Backup;

public interface IBackupHistoryService
{
    Task<List<BackupHistoryEntry>> GetHistoryAsync(CancellationToken cancellationToken);
}

public sealed class BackupHistoryService : IBackupHistoryService
{
    private readonly BackupApi _api;

    public BackupHistoryService(BackupApi api)
    {
        ArgumentNullException.ThrowIfNull(api);
        _api = api;
    }

    public Task<List<BackupHistoryEntry>> GetHistoryAsync(CancellationToken cancellationToken)
        => _api.GetHistoryAsync(cancellationToken);
}
