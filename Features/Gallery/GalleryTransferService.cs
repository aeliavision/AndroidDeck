using System;
using System.Threading;
using System.Threading.Tasks;
using VcfEditor.Core;

namespace VcfEditor.Features.Gallery;

public class GalleryTransferService
{
    public static async Task DownloadMediaItemAsync(
        GalleryApi galleryApi,
        string mediaId,
        string mediaType,
        string targetFilePath,
        IProgress<(long received, long total)>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(galleryApi);
        ArgumentNullException.ThrowIfNull(mediaId);
        ArgumentNullException.ThrowIfNull(mediaType);
        ArgumentNullException.ThrowIfNull(targetFilePath);

        cancellationToken.ThrowIfCancellationRequested();
        await galleryApi.DownloadMediaAsync(mediaId, mediaType, targetFilePath, progress, cancellationToken);
    }
}
