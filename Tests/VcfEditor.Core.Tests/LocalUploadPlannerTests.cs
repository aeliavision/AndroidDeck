using System.IO.Abstractions;
using VcfEditor.Features.Files;

namespace VcfEditor.Core.Tests;

public sealed class LocalUploadPlannerTests
{
    [Fact]
    public async Task BuildAsyncPreservesFolderStructureUnderRemoteRoot()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var looseFile = Path.Combine(directory, "readme.txt");
            await File.WriteAllTextAsync(looseFile, "readme");

            var photos = Path.Combine(directory, "photos");
            var nested = Path.Combine(photos, "nested");
            Directory.CreateDirectory(nested);
            var firstPhoto = Path.Combine(photos, "first.jpg");
            var secondPhoto = Path.Combine(nested, "second.jpg");
            await File.WriteAllTextAsync(firstPhoto, "first");
            await File.WriteAllTextAsync(secondPhoto, "second");

            var planner = new LocalUploadPlanner(new FileSystem());

            var result = await planner.BuildAsync(
                [looseFile, photos],
                "/sdcard/Documents",
                CancellationToken.None);

            Assert.Contains(result, item =>
                item.LocalPath == looseFile &&
                item.RemotePath == "/sdcard/Documents/readme.txt");
            Assert.Contains(result, item =>
                item.LocalPath == firstPhoto &&
                item.RemotePath == "/sdcard/Documents/photos/first.jpg");
            Assert.Contains(result, item =>
                item.LocalPath == secondPhoto &&
                item.RemotePath == "/sdcard/Documents/photos/nested/second.jpg");
            Assert.Equal(3, result.Count);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task BuildAsyncHonorsPreCancelledToken()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var planner = new LocalUploadPlanner(new FileSystem());

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            planner.BuildAsync(
                [Path.Combine(Path.GetTempPath(), "not-read.txt")],
                "/sdcard",
                cancellation.Token));
    }

    private static string CreateTemporaryDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), $"AndroidDeckTests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }
}
