using VcfEditor.Features.Backup;

namespace VcfEditor.Core.Tests;

public sealed class BackupArchiveServiceTests
{
    [Fact]
    public async Task EncryptThenDecryptRestoresOriginalContent()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var source = Path.Combine(directory, "source.deckbak");
            var encrypted = Path.Combine(directory, "encrypted.deckbak");
            var restored = Path.Combine(directory, "restored.deckbak");
            var original = Enumerable.Range(0, 8192)
                .Select(index => (byte)(index % 251))
                .ToArray();
            await File.WriteAllBytesAsync(source, original);
            var service = new BackupArchiveService();

            await service.EncryptAsync(
                source,
                encrypted,
                "correct horse battery staple",
                progress: null,
                CancellationToken.None);
            await service.DecryptAsync(
                encrypted,
                restored,
                "correct horse battery staple",
                progress: null,
                CancellationToken.None);

            Assert.True(service.IsLocallyEncrypted(encrypted));
            Assert.Equal(original, await File.ReadAllBytesAsync(restored));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task DecryptRejectsTamperedArchiveBeforeWritingOutput()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var source = Path.Combine(directory, "source.deckbak");
            var encrypted = Path.Combine(directory, "encrypted.deckbak");
            var restored = Path.Combine(directory, "restored.deckbak");
            await File.WriteAllTextAsync(source, "authenticated backup payload");
            var service = new BackupArchiveService();
            await service.EncryptAsync(
                source,
                encrypted,
                "secret-password",
                progress: null,
                CancellationToken.None);

            var bytes = await File.ReadAllBytesAsync(encrypted);
            bytes[^1] ^= 0x5A;
            await File.WriteAllBytesAsync(encrypted, bytes);

            await Assert.ThrowsAsync<InvalidDataException>(() =>
                service.DecryptAsync(
                    encrypted,
                    restored,
                    "secret-password",
                    progress: null,
                    CancellationToken.None));
            Assert.False(File.Exists(restored));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task PreviewIdentifiesLocallyEncryptedArchive()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var source = Path.Combine(directory, "source.deckbak");
            var encrypted = Path.Combine(directory, "encrypted.deckbak");
            await File.WriteAllTextAsync(source, "preview payload");
            var service = new BackupArchiveService();
            await service.EncryptAsync(
                source,
                encrypted,
                "secret-password",
                progress: null,
                CancellationToken.None);

            var rows = service.BuildPreviewRows(encrypted);

            Assert.Contains(rows, row => row.Label == "Locally encrypted" && row.Value == "Yes");
            Assert.Contains(rows, row => row.Label == "File" && row.Value == "encrypted.deckbak");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static string CreateTemporaryDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), $"AndroidDeckTests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }
}
