using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Abstractions;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace VcfEditor.Features.Files;

public sealed record LocalUploadItem(string LocalPath, string RemotePath);

public interface ILocalUploadPlanner
{
    Task<IReadOnlyList<LocalUploadItem>> BuildAsync(
        IEnumerable<string> localPaths,
        string remoteRoot,
        CancellationToken cancellationToken);
}

public sealed class LocalUploadPlanner : ILocalUploadPlanner
{
    private readonly IFileSystem _fileSystem;

    public LocalUploadPlanner(IFileSystem fileSystem)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);
        _fileSystem = fileSystem;
    }

    public Task<IReadOnlyList<LocalUploadItem>> BuildAsync(
        IEnumerable<string> localPaths,
        string remoteRoot,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(localPaths);
        ArgumentException.ThrowIfNullOrWhiteSpace(remoteRoot);
        var snapshot = localPaths.Where(path => !string.IsNullOrWhiteSpace(path)).ToArray();

        return Task.Run<IReadOnlyList<LocalUploadItem>>(
            () => Build(snapshot, remoteRoot, cancellationToken),
            cancellationToken);
    }

    private List<LocalUploadItem> Build(
        IReadOnlyList<string> localPaths,
        string remoteRoot,
        CancellationToken cancellationToken)
    {
        var result = new List<LocalUploadItem>();
        foreach (var path in localPaths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_fileSystem.File.Exists(path))
            {
                result.Add(new LocalUploadItem(
                    path,
                    CombineRemote(remoteRoot, Path.GetFileName(path))));
                continue;
            }

            if (!_fileSystem.Directory.Exists(path))
                continue;

            var directoryName = Path.GetFileName(path.TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar));
            foreach (var file in _fileSystem.Directory.GetFiles(
                         path,
                         "*",
                         SearchOption.AllDirectories))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var relativePath = Path.GetRelativePath(path, file).Replace('\\', '/');
                result.Add(new LocalUploadItem(
                    file,
                    CombineRemote(remoteRoot, $"{directoryName}/{relativePath}")));
            }
        }

        return result;
    }

    private static string CombineRemote(string remoteRoot, string relativePath)
    {
        var root = remoteRoot.TrimEnd('/');
        var relative = relativePath.TrimStart('/').Replace('\\', '/');
        return string.IsNullOrEmpty(root) ? $"/{relative}" : $"{root}/{relative}";
    }
}
