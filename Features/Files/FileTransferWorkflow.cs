using System;
using System.Threading;
using System.Threading.Tasks;
using VcfEditor.Core;
using VcfEditor.Models.DTOs;

namespace VcfEditor.Features.Files;

public interface IFileTransferWorkflow
{
    Task<DirectoryListingDto> ListDirectoryAsync(string? path, CancellationToken cancellationToken);
    Task DownloadAsync(
        string remotePath,
        string localPath,
        IProgress<(long received, long total)>? progress,
        CancellationToken cancellationToken);
    Task<UploadResultDto> UploadToPathAsync(
        string localPath,
        string destinationPath,
        IProgress<(long sent, long total)>? progress,
        CancellationToken cancellationToken);
    Task<bool> DeleteAsync(string remotePath, bool recursive, CancellationToken cancellationToken);
    Task<string?> CreateDirectoryAsync(string remotePath, CancellationToken cancellationToken);
    Task<FileEntryDto> RenameAsync(
        string remotePath,
        string newName,
        bool overwrite,
        CancellationToken cancellationToken);
    Task<FileEntryDto> MoveAsync(
        string remotePath,
        string destinationPath,
        bool overwrite,
        CancellationToken cancellationToken);
}

public sealed class FileTransferWorkflow : IFileTransferWorkflow
{
    private readonly FileSystemApi _api;

    public FileTransferWorkflow(FileSystemApi api)
    {
        ArgumentNullException.ThrowIfNull(api);
        _api = api;
    }

    public Task<DirectoryListingDto> ListDirectoryAsync(
        string? path,
        CancellationToken cancellationToken)
        => _api.ListDirectoryAsync(path, cancellationToken);

    public Task DownloadAsync(
        string remotePath,
        string localPath,
        IProgress<(long received, long total)>? progress,
        CancellationToken cancellationToken)
        => _api.DownloadFileAsync(remotePath, localPath, progress, cancellationToken);

    public Task<UploadResultDto> UploadToPathAsync(
        string localPath,
        string destinationPath,
        IProgress<(long sent, long total)>? progress,
        CancellationToken cancellationToken)
        => _api.UploadFileToPathAsync(localPath, destinationPath, progress, cancellationToken);

    public Task<bool> DeleteAsync(
        string remotePath,
        bool recursive,
        CancellationToken cancellationToken)
        => _api.DeleteAsync(remotePath, recursive, cancellationToken);

    public Task<string?> CreateDirectoryAsync(
        string remotePath,
        CancellationToken cancellationToken)
        => _api.MkdirAsync(remotePath, cancellationToken);

    public Task<FileEntryDto> RenameAsync(
        string remotePath,
        string newName,
        bool overwrite,
        CancellationToken cancellationToken)
        => _api.RenameAsync(remotePath, newName, overwrite, cancellationToken);

    public Task<FileEntryDto> MoveAsync(
        string remotePath,
        string destinationPath,
        bool overwrite,
        CancellationToken cancellationToken)
        => _api.MoveAsync(remotePath, destinationPath, overwrite, cancellationToken);
}
