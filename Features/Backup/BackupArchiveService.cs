using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using VcfEditor.Helpers;
using VcfEditor.Core.IO;

namespace VcfEditor.Features.Backup;

public interface IBackupArchiveService
{
    List<(string Label, string Value)> BuildPreviewRows(string archivePath);
    long GetFileSize(string archivePath);
    bool IsLocallyEncrypted(string archivePath);
    void TryDeleteTemporaryFile(string? path);
    Task EncryptAsync(
        string inputPath,
        string outputPath,
        string password,
        IProgress<double>? progress,
        CancellationToken cancellationToken);
    Task DecryptAsync(
        string inputPath,
        string outputPath,
        string password,
        IProgress<double>? progress,
        CancellationToken cancellationToken);
}

public sealed class BackupArchiveService : IBackupArchiveService
{
    private const string CurrentMagic = "DECKBAK2";
    private const string CompatibleMagic = "VCFBAK02";
    private const string LegacyMagic = "VCFBAK01";
    private const int BufferSize = 512 * 1024;
    private readonly ILogger<BackupArchiveService> _logger;

    public BackupArchiveService(ILogger<BackupArchiveService>? logger = null)
    {
        _logger = logger ?? NullLogger<BackupArchiveService>.Instance;
    }

    public List<(string Label, string Value)> BuildPreviewRows(string archivePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(archivePath);
        var size = GetFileSize(archivePath);
        var locallyEncrypted = IsLocallyEncrypted(archivePath);
        var format = DetectFormat(archivePath, locallyEncrypted);

        return
        [
            ("File", Path.GetFileName(archivePath)),
            ("Location", archivePath),
            ("Size", size > 0 ? FormatBytes(size) : string.Empty),
            ("Format", format),
            ("Locally encrypted", locallyEncrypted ? "Yes" : "No")
        ];
    }


    public long GetFileSize(string archivePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(archivePath);
        try
        {
            var file = new FileInfo(archivePath);
            return file.Exists ? file.Length : 0;
        }
        catch (IOException)
        {
            return 0;
        }
        catch (UnauthorizedAccessException)
        {
            return 0;
        }
    }

    public void TryDeleteTemporaryFile(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;

        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (IOException ex)
        {
            LogMessages.BackupTemporaryFileCleanupFailed(_logger, ex, path);
        }
        catch (UnauthorizedAccessException ex)
        {
            LogMessages.BackupTemporaryFileCleanupFailed(_logger, ex, path);
        }
    }

    public bool IsLocallyEncrypted(string archivePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(archivePath);
        try
        {
            using var stream = new FileStream(
                archivePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read);
            Span<byte> buffer = stackalloc byte[8];
            var read = stream.Read(buffer);
            if (read != buffer.Length) return false;
            var magic = Encoding.ASCII.GetString(buffer);
            return magic is CurrentMagic or CompatibleMagic or LegacyMagic;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    public async Task EncryptAsync(
        string inputPath,
        string outputPath,
        string password,
        IProgress<double>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(inputPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(password);

        var inputLength = new FileInfo(inputPath).Length;
        if (inputLength > TransferLimits.MaxBackupArchiveBytes)
            throw new TransferLimitExceededException(
                TransferLimits.MaxBackupArchiveBytes,
                inputLength);

        const int saltBytes = 16;
        const int ivBytes = 16;
        const int hmacBytes = 32;

        var salt = RandomNumberGenerator.GetBytes(saltBytes);
        var iv = RandomNumberGenerator.GetBytes(ivBytes);
        var keyMaterial = Rfc2898DeriveBytes.Pbkdf2(
            password,
            salt,
            100_000,
            HashAlgorithmName.SHA256,
            64);
        var encKey = keyMaterial.AsSpan(0, 32).ToArray();
        var macKey = keyMaterial.AsSpan(32, 32).ToArray();

        using var aes = Aes.Create();
        aes.KeySize = 256;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;
        aes.Key = encKey;
        aes.IV = iv;

        await using var input = new FileStream(
            inputPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            BufferSize,
            useAsync: true);
        await using var output = new FileStream(
            outputPath,
            FileMode.Create,
            FileAccess.ReadWrite,
            FileShare.None,
            BufferSize,
            useAsync: true);

        var magic = Encoding.ASCII.GetBytes(CurrentMagic);
        await output.WriteAsync(magic, cancellationToken);
        await output.WriteAsync(salt, cancellationToken);
        await output.WriteAsync(iv, cancellationToken);

        var hmacOffset = output.Position;
        await output.WriteAsync(new byte[hmacBytes], cancellationToken);

        using var hmac = new HMACSHA256(macKey);
        hmac.TransformBlock(magic, 0, magic.Length, null, 0);
        hmac.TransformBlock(salt, 0, salt.Length, null, 0);
        hmac.TransformBlock(iv, 0, iv.Length, null, 0);

        await using (var crypto = new CryptoStream(
            output,
            aes.CreateEncryptor(),
            CryptoStreamMode.Write,
            leaveOpen: true))
        {
            var byteProgress = progress is null
                ? null
                : new Progress<long>(readTotal =>
                {
                    if (input.Length > 0)
                        progress.Report(Math.Min(1.0, (double)readTotal / input.Length));
                });
            await BoundedStreamCopy.CopyAsync(
                    input,
                    crypto,
                    TransferLimits.MaxBackupArchiveBytes,
                    progress: byteProgress,
                    bufferSize: BufferSize,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            crypto.FlushFinalBlock();
        }

        var cipherStart = hmacOffset + hmacBytes;
        output.Position = cipherStart;
        var macBuffer = new byte[BufferSize];
        int macRead;
        while ((macRead = await output.ReadAsync(
                   macBuffer.AsMemory(0, macBuffer.Length),
                   cancellationToken)) > 0)
        {
            hmac.TransformBlock(macBuffer, 0, macRead, null, 0);
        }
        hmac.TransformFinalBlock(Array.Empty<byte>(), 0, 0);

        output.Position = hmacOffset;
        var tag = hmac.Hash ?? throw new InvalidOperationException("HMAC computation failed.");
        await output.WriteAsync(tag.AsMemory(0, hmacBytes), cancellationToken);
        progress?.Report(1.0);
    }

    public async Task DecryptAsync(
        string inputPath,
        string outputPath,
        string password,
        IProgress<double>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(inputPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(password);

        var encryptedLength = new FileInfo(inputPath).Length;
        if (encryptedLength > TransferLimits.MaxBackupArchiveBytes)
            throw new TransferLimitExceededException(
                TransferLimits.MaxBackupArchiveBytes,
                encryptedLength);

        await using var input = new FileStream(
            inputPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            BufferSize,
            useAsync: true);
        var magicBuffer = new byte[8];
        var magicRead = await input.ReadAsync(
            magicBuffer.AsMemory(0, magicBuffer.Length),
            cancellationToken);
        if (magicRead != magicBuffer.Length)
            throw new InvalidDataException("Invalid encrypted backup header.");

        var magic = Encoding.ASCII.GetString(magicBuffer);
        if (magic == LegacyMagic)
        {
            await DecryptLegacyAsync(
                input,
                outputPath,
                password,
                progress,
                cancellationToken);
            return;
        }

        if (magic is not CurrentMagic and not CompatibleMagic)
            throw new InvalidDataException("Not a locally encrypted backup archive.");

        var salt = new byte[16];
        var iv = new byte[16];
        var expectedHmac = new byte[32];
        await ReadExactAsync(input, salt, cancellationToken);
        await ReadExactAsync(input, iv, cancellationToken);
        await ReadExactAsync(input, expectedHmac, cancellationToken);

        var keyMaterial = Rfc2898DeriveBytes.Pbkdf2(
            password,
            salt,
            100_000,
            HashAlgorithmName.SHA256,
            64);
        var encKey = keyMaterial.AsSpan(0, 32).ToArray();
        var macKey = keyMaterial.AsSpan(32, 32).ToArray();

        using (var hmac = new HMACSHA256(macKey))
        {
            hmac.TransformBlock(magicBuffer, 0, magicBuffer.Length, null, 0);
            hmac.TransformBlock(salt, 0, salt.Length, null, 0);
            hmac.TransformBlock(iv, 0, iv.Length, null, 0);

            var buffer = new byte[BufferSize];
            int read;
            while ((read = await input.ReadAsync(
                       buffer.AsMemory(0, buffer.Length),
                       cancellationToken)) > 0)
            {
                hmac.TransformBlock(buffer, 0, read, null, 0);
            }
            hmac.TransformFinalBlock(Array.Empty<byte>(), 0, 0);

            var actual = hmac.Hash ?? throw new InvalidOperationException("HMAC computation failed.");
            if (!CryptographicOperations.FixedTimeEquals(actual, expectedHmac))
                throw new InvalidDataException("Encrypted backup integrity check failed.");
        }

        input.Position = 8 + 16 + 16 + 32;
        using var aes = Aes.Create();
        aes.KeySize = 256;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;
        aes.Key = encKey;
        aes.IV = iv;

        await using var output = new FileStream(
            outputPath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            BufferSize,
            useAsync: true);
        await using var crypto = new CryptoStream(
            input,
            aes.CreateDecryptor(),
            CryptoStreamMode.Read,
            leaveOpen: true);

        var total = input.Length - input.Position;
        await CopyWithProgressAsync(
            crypto,
            output,
            total,
            progress,
            cancellationToken);
    }

    private static string DetectFormat(string archivePath, bool locallyEncrypted)
    {
        try
        {
            using var stream = new FileStream(
                archivePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read);
            Span<byte> buffer = stackalloc byte[4];
            var read = stream.Read(buffer);
            return read == buffer.Length && buffer.SequenceEqual("VCFB"u8)
                ? "Device backup archive"
                : locallyEncrypted ? "Locally encrypted wrapper" : "Unknown";
        }
        catch (IOException)
        {
            return locallyEncrypted ? "Locally encrypted wrapper" : "Unknown";
        }
        catch (UnauthorizedAccessException)
        {
            return locallyEncrypted ? "Locally encrypted wrapper" : "Unknown";
        }
    }

    private static async Task DecryptLegacyAsync(
        FileStream input,
        string outputPath,
        string password,
        IProgress<double>? progress,
        CancellationToken cancellationToken)
    {
        var header = new byte[32];
        await ReadExactAsync(input, header, cancellationToken);
        var salt = header.AsSpan(0, 16).ToArray();
        var iv = header.AsSpan(16, 16).ToArray();
        var key = Rfc2898DeriveBytes.Pbkdf2(
            password,
            salt,
            100_000,
            HashAlgorithmName.SHA256,
            32);

        using var aes = Aes.Create();
        aes.KeySize = 256;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;
        aes.Key = key;
        aes.IV = iv;

        await using var output = new FileStream(
            outputPath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            BufferSize,
            useAsync: true);
        await using var crypto = new CryptoStream(
            input,
            aes.CreateDecryptor(),
            CryptoStreamMode.Read,
            leaveOpen: true);
        var total = input.Length - input.Position;
        await CopyWithProgressAsync(
            crypto,
            output,
            total,
            progress,
            cancellationToken);
    }

    private static async Task ReadExactAsync(
        Stream stream,
        byte[] buffer,
        CancellationToken cancellationToken)
    {
        var offset = 0;
        while (offset < buffer.Length)
        {
            var read = await stream.ReadAsync(
                buffer.AsMemory(offset, buffer.Length - offset),
                cancellationToken);
            if (read == 0)
                throw new InvalidDataException("Invalid encrypted backup header.");
            offset += read;
        }
    }

    private static async Task CopyWithProgressAsync(
        Stream input,
        Stream output,
        long total,
        IProgress<double>? progress,
        CancellationToken cancellationToken)
    {
        var byteProgress = progress is null
            ? null
            : new Progress<long>(written =>
            {
                if (total > 0)
                    progress.Report(Math.Min(1.0, (double)written / total));
            });
        await BoundedStreamCopy.CopyAsync(
                input,
                output,
                TransferLimits.MaxDecompressedBackupBytes,
                progress: byteProgress,
                bufferSize: BufferSize,
                cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        progress?.Report(1.0);
    }

    private static string FormatBytes(long bytes) => bytes switch
    {
        >= 1_073_741_824 => $"{bytes / 1_073_741_824.0:F1} GB",
        >= 1_048_576 => $"{bytes / 1_048_576.0:F1} MB",
        >= 1_024 => $"{bytes / 1_024.0:F1} KB",
        _ => $"{bytes} B"
    };
}
