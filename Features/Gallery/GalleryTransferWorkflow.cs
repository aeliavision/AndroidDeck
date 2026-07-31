using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using VcfEditor.Core;
using VcfEditor.Models.DTOs;

namespace VcfEditor.Features.Gallery;

public interface IGalleryTransferWorkflow
{
    Task<List<AlbumDto>> GetAlbumsAsync(CancellationToken cancellationToken);
    Task<MediaPageDto> GetMediaPageAsync(
        string? albumId,
        string types = "image,video",
        int page = 1,
        int pageSize = 60,
        CancellationToken cancellationToken = default);
    Task<byte[]> GetThumbnailAsync(
        string mediaId,
        string mediaType,
        int maxDim = 256,
        CancellationToken cancellationToken = default);
    Task DownloadAsync(
        string mediaId,
        string mediaType,
        string localPath,
        IProgress<(long received, long total)>? progress = null,
        CancellationToken cancellationToken = default);
    Task<GalleryActionResultDto> DeleteAsync(
        IEnumerable<string> ids,
        string mediaType,
        CancellationToken cancellationToken = default);
    Task<GalleryActionResultDto> RenameAsync(
        string id,
        string newName,
        string mediaType,
        CancellationToken cancellationToken = default);
    Task<GalleryActionResultDto> MoveAsync(
        IEnumerable<string> ids,
        string targetRelativePath,
        string mediaType,
        CancellationToken cancellationToken = default);
    Task<GalleryActionResultDto> UpdateMetadataAsync(
        string id,
        string mediaType,
        bool? favorite,
        string? description,
        CancellationToken cancellationToken = default);
}

public sealed class GalleryTransferWorkflow : IGalleryTransferWorkflow
{
    private readonly GalleryApi _api;

    public GalleryTransferWorkflow(GalleryApi api)
    {
        ArgumentNullException.ThrowIfNull(api);
        _api = api;
    }

    public Task<List<AlbumDto>> GetAlbumsAsync(CancellationToken cancellationToken)
        => _api.GetAlbumsAsync(cancellationToken);

    public Task<MediaPageDto> GetMediaPageAsync(
        string? albumId,
        string types = "image,video",
        int page = 1,
        int pageSize = 60,
        CancellationToken cancellationToken = default)
        => _api.GetMediaAsync(albumId, types, page, pageSize, cancellationToken);

    public Task<byte[]> GetThumbnailAsync(
        string mediaId,
        string mediaType,
        int maxDim = 256,
        CancellationToken cancellationToken = default)
        => _api.GetThumbnailAsync(mediaId, mediaType, maxDim, cancellationToken);

    public Task DownloadAsync(
        string mediaId,
        string mediaType,
        string localPath,
        IProgress<(long received, long total)>? progress = null,
        CancellationToken cancellationToken = default)
        => _api.DownloadMediaAsync(mediaId, mediaType, localPath, progress, cancellationToken);

    public Task<GalleryActionResultDto> DeleteAsync(
        IEnumerable<string> ids,
        string mediaType,
        CancellationToken cancellationToken = default)
        => _api.DeleteMediaAsync(ids, mediaType, cancellationToken);

    public Task<GalleryActionResultDto> RenameAsync(
        string id,
        string newName,
        string mediaType,
        CancellationToken cancellationToken = default)
        => _api.RenameMediaAsync(id, newName, mediaType, cancellationToken);

    public Task<GalleryActionResultDto> MoveAsync(
        IEnumerable<string> ids,
        string targetRelativePath,
        string mediaType,
        CancellationToken cancellationToken = default)
        => _api.MoveMediaAsync(ids, targetRelativePath, mediaType, cancellationToken);

    public Task<GalleryActionResultDto> UpdateMetadataAsync(
        string id,
        string mediaType,
        bool? favorite,
        string? description,
        CancellationToken cancellationToken = default)
        => _api.UpdateMetadataAsync(id, mediaType, favorite, description, cancellationToken);
}
