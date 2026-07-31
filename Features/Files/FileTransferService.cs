using System;
using System.Threading;
using System.Threading.Tasks;
using VcfEditor.Core;

namespace VcfEditor.Features.Files;

public class FileTransferService
{
    public static async Task DownloadFileAsync(
        FileSystemApi fileApi,
        string remotePath,
        string targetLocalPath,
        IProgress<(long received, long total)>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(fileApi);
        ArgumentNullException.ThrowIfNull(remotePath);
        ArgumentNullException.ThrowIfNull(targetLocalPath);

        cancellationToken.ThrowIfCancellationRequested();
        await fileApi.DownloadFileAsync(remotePath, targetLocalPath, progress, cancellationToken);
    }

    public static async Task UploadFileAsync(
        FileSystemApi fileApi,
        string localFilePath,
        string remoteDirectory,
        IProgress<(long sent, long total)>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(fileApi);
        ArgumentNullException.ThrowIfNull(localFilePath);
        ArgumentNullException.ThrowIfNull(remoteDirectory);

        cancellationToken.ThrowIfCancellationRequested();
        await fileApi.UploadFileAsync(localFilePath, remoteDirectory, progress, cancellationToken);
    }
}
