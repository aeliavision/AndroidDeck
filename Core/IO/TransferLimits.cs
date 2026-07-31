namespace VcfEditor.Core.IO;

internal static class TransferLimits
{
    public const long MaxBackupArchiveBytes = 64L * 1024 * 1024 * 1024;
    public const long MaxFileTransferBytes = 64L * 1024 * 1024 * 1024;
    public const long MaxThumbnailBytes = 16L * 1024 * 1024;
    public const long MaxContactPhotoBytes = 32L * 1024 * 1024;
    public const long MaxDecompressedBackupBytes = 256L * 1024 * 1024 * 1024;
    public const int DefaultStreamBufferBytes = 512 * 1024;
}
