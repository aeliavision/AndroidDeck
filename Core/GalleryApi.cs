using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using VcfEditor.Helpers;
using VcfEditor.Core.IO;
using VcfEditor.Core.Paging;
using VcfEditor.Models.DTOs;

namespace VcfEditor.Core
{
    /// <summary>
    /// Implements all /api/v2/gallery/* endpoints.
    ///
    /// Features:
    ///   • Album listing (images + videos grouped by MediaStore bucket)
    /// </summary>
    public sealed class GalleryApi
    {
        private static readonly ILogger Logger = AppLoggerFactory.CreateLogger(nameof(GalleryApi));

        private const string V2 = "/api/v2";

        private readonly HttpTransport _transport;
        private readonly SessionManager _session;
        private readonly ThumbnailCache _thumbnailCache;
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true
        };

        public GalleryApi(HttpTransport transport, SessionManager session)
        {
            _transport = transport;
            _session = session;
            _thumbnailCache = new ThumbnailCache();
        }

        // ── Albums ────────────────────────────────────────────────────────────

        /// <summary>Fetch all albums from the phone gallery.</summary>
        public async Task<List<AlbumDto>> GetAlbumsAsync(CancellationToken cancellationToken = default)
        {
            _session.EnsureConnected();
            var json = await Send(HttpMethod.Get, "/gallery/albums", cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            var resp = JsonSerializer.Deserialize<AlbumsResponseDto>(json, JsonOptions);
            return resp?.Albums ?? new List<AlbumDto>();
        }

        // ── Media listing ─────────────────────────────────────────────────────

        /// <summary>
        /// Fetch a page of media items from [albumId].
        /// Pass null albumId to get all media across all albums.
        /// </summary>
        public async Task<MediaPageDto> GetMediaAsync(
            string? albumId = null,
            string types = "image,video",
            int page = 1,
            int pageSize = 50,
            CancellationToken cancellationToken = default)
        {
            _session.EnsureConnected();
            var qs = $"?page={page}&pageSize={pageSize}&types={Uri.EscapeDataString(types)}";
            if (albumId != null) qs += $"&albumId={Uri.EscapeDataString(albumId)}";
            var json = await Send(HttpMethod.Get, $"/gallery/media{qs}", cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            return JsonSerializer.Deserialize<MediaPageDto>(json, JsonOptions) ?? new MediaPageDto();
        }

        /// <summary>Fetch ALL media for [albumId], paging automatically.</summary>
        public async Task<List<GalleryMediaDto>> GetAllMediaAsync(
            string? albumId = null,
            string types = "image,video",
            IProgress<(int current, int total)>? progress = null,
            CancellationToken cancellationToken = default)
        {
            _session.EnsureConnected();
            return await PagedFetch.FetchAllAsync(
                    (page, token) => GetMediaAsync(albumId, types, page, 50, token),
                    pageData => pageData.Items,
                    pageData => pageData.NextPage,
                    item => $"{item.MediaType}:{item.Id}",
                    reportItemCount: count => progress?.Report((count, -1)),
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }

        /// <summary>
        /// Returns JPEG bytes.
        /// </summary>
        public async Task<byte[]> GetThumbnailAsync(
            string mediaId,
            string mediaType = "image",
            int maxDim = 256,
            CancellationToken cancellationToken = default)
        {
            _session.EnsureConnected();

            // Check disk cache first.
            var cacheKey = $"{mediaId}_{mediaType}_{maxDim}";
            var cached = await ThumbnailCache.GetAsync(cacheKey, cancellationToken).ConfigureAwait(false);
            if (cached != null) return cached;

            var path = $"/gallery/thumbnail/{Uri.EscapeDataString(mediaId)}?type={mediaType}&maxDim={maxDim}";
            var url = $"{_transport.BaseUrl}{V2}{path}";
            var signaturePath = $"{V2}/gallery/thumbnail/{Uri.EscapeDataString(mediaId)}";
            using var response = await _transport.SendRawAuthenticatedAsync(
                HttpMethod.Get, url, signaturePath, cancellationToken: cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                throw new PhoneConnectionException($"Thumbnail fetch failed: {(int)response.StatusCode}");

            BoundedStreamCopy.ValidateDeclaredLength(
                response.Content.Headers.ContentLength,
                TransferLimits.MaxThumbnailBytes,
                "gallery thumbnail");
            await using var thumbnailStream = await response.Content
                .ReadAsStreamAsync(cancellationToken)
                .ConfigureAwait(false);
            var bytes = await BoundedStreamCopy.ReadAllBytesAsync(
                    thumbnailStream,
                    TransferLimits.MaxThumbnailBytes,
                    cancellationToken)
                .ConfigureAwait(false);

            // Store in disk cache.
            await _thumbnailCache.SetAsync(cacheKey, bytes, cancellationToken).ConfigureAwait(false);
            return bytes;
        }

        /// <summary>
        /// Reports progress via [progress] callback.
        /// </summary>
        public async Task DownloadMediaAsync(
            string mediaId,
            string mediaType,
            string localPath,
            IProgress<(long received, long total)>? progress = null,
            CancellationToken cancellationToken = default)
        {
            _session.EnsureConnected();
            var url = $"{_transport.BaseUrl}{V2}/gallery/download/{Uri.EscapeDataString(mediaId)}?type={mediaType}";
            var signaturePath = $"{V2}/gallery/download/{Uri.EscapeDataString(mediaId)}";
            using var response = await _transport.SendRawAuthenticatedAsync(
                HttpMethod.Get, url, signaturePath, cancellationToken: cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                throw new PhoneConnectionException($"Gallery download failed: {(int)response.StatusCode}");

            var total = response.Content.Headers.ContentLength ?? -1L;
            BoundedStreamCopy.ValidateDeclaredLength(
                response.Content.Headers.ContentLength,
                TransferLimits.MaxFileTransferBytes,
                "gallery download");
            var tempPath = localPath + ".partial";
            var localDirectory = Path.GetDirectoryName(localPath);
            if (!string.IsNullOrWhiteSpace(localDirectory)) Directory.CreateDirectory(localDirectory);

            try
            {
                await using var stream = await response.Content
                    .ReadAsStreamAsync(cancellationToken)
                    .ConfigureAwait(false);
                await using var file = new FileStream(
                    tempPath,
                    FileMode.Create,
                    FileAccess.Write,
                    FileShare.None,
                    TransferLimits.DefaultStreamBufferBytes,
                    useAsync: true);
                var byteProgress = progress is null
                    ? null
                    : new Progress<long>(received => progress.Report((received, total)));
                await BoundedStreamCopy.CopyAsync(
                        stream,
                        file,
                        TransferLimits.MaxFileTransferBytes,
                        progress: byteProgress,
                        cancellationToken: cancellationToken)
                    .ConfigureAwait(false);
            }
            catch
            {
                if (File.Exists(tempPath)) File.Delete(tempPath);
                throw;
            }

            File.Move(tempPath, localPath, overwrite: true);
            LogMessages.MediaDownloaded(Logger, mediaId, localPath);
        }

        public async Task<GalleryActionResultDto> DeleteMediaAsync(
            IEnumerable<string> ids,
            string mediaType,
            CancellationToken cancellationToken = default)
        {
            _session.EnsureConnected();
            var body = JsonSerializer.Serialize(new GalleryDeleteRequestDto
            {
                Ids = ids.ToList(),
                MediaType = mediaType
            }, JsonOptions);

            var json = await Send(HttpMethod.Post, "/gallery/delete", body, cancellationToken)
                .ConfigureAwait(false);
            return JsonSerializer.Deserialize<GalleryActionResultDto>(json, JsonOptions) ?? new GalleryActionResultDto();
        }

        public async Task<GalleryActionResultDto> RenameMediaAsync(
            string id,
            string newName,
            string mediaType,
            CancellationToken cancellationToken = default)
        {
            _session.EnsureConnected();
            var body = JsonSerializer.Serialize(new GalleryRenameRequestDto
            {
                Id = id,
                NewName = newName,
                MediaType = mediaType
            }, JsonOptions);

            var json = await Send(HttpMethod.Post, "/gallery/rename", body, cancellationToken)
                .ConfigureAwait(false);
            return JsonSerializer.Deserialize<GalleryActionResultDto>(json, JsonOptions) ?? new GalleryActionResultDto();
        }

        public async Task<GalleryActionResultDto> MoveMediaAsync(
            IEnumerable<string> ids,
            string targetRelativePath,
            string mediaType,
            CancellationToken cancellationToken = default)
        {
            _session.EnsureConnected();
            var body = JsonSerializer.Serialize(new GalleryMoveRequestDto
            {
                Ids = ids.ToList(),
                TargetRelativePath = targetRelativePath,
                MediaType = mediaType
            }, JsonOptions);

            var json = await Send(HttpMethod.Post, "/gallery/move", body, cancellationToken)
                .ConfigureAwait(false);
            return JsonSerializer.Deserialize<GalleryActionResultDto>(json, JsonOptions) ?? new GalleryActionResultDto();
        }

        public async Task<GalleryActionResultDto> UpdateMetadataAsync(
            string id,
            string mediaType,
            bool? favorite,
            string? description,
            CancellationToken cancellationToken = default)
        {
            _session.EnsureConnected();
            var body = JsonSerializer.Serialize(new GalleryMetadataRequestDto
            {
                Id = id,
                MediaType = mediaType,
                Favorite = favorite,
                Description = description
            }, JsonOptions);

            var json = await Send(HttpMethod.Post, "/gallery/metadata", body, cancellationToken)
                .ConfigureAwait(false);
            return JsonSerializer.Deserialize<GalleryActionResultDto>(json, JsonOptions) ?? new GalleryActionResultDto();
        }

        // ── Private helpers ────────────────────────────────────────────────────

        private Task<string> Send(
            HttpMethod method, string path, string? body = null,
            CancellationToken cancellationToken = default)
        {
            var queryIdx = path.IndexOf('?');
            var signaturePath = V2 + (queryIdx != -1 ? path[..queryIdx] : path);
            var fullUrl = _transport.BaseUrl + V2 + path;
            return _transport.SendAsync(method, fullUrl, signaturePath, body, cancellationToken);
        }
    }

    /// <summary>
    /// Stores JPEG bytes in %LocalAppData%/VcfEditor/ThumbnailCache/.
    /// Max cache size: 200 MB. Entries expire after 7 days.
    /// </summary>
    public sealed class ThumbnailCache
    {
        private static readonly string CacheDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "VcfEditor", "ThumbnailCache");

        private const long MaxCacheSizeBytes = 200L * 1024 * 1024; // 200 MB
        private static readonly TimeSpan CacheTtl = TimeSpan.FromDays(7);

        public ThumbnailCache()
        {
            Directory.CreateDirectory(CacheDir);
        }

        public static async Task<byte[]?> GetAsync(
            string key,
            CancellationToken cancellationToken = default)
        {
            var path = GetCachePath(key);
            if (!File.Exists(path)) return null;
            var info = new FileInfo(path);
            if (DateTime.UtcNow - info.LastWriteTimeUtc > CacheTtl ||
                info.Length > TransferLimits.MaxThumbnailBytes)
            {
                File.Delete(path);
                return null;
            }

            File.SetLastWriteTimeUtc(path, DateTime.UtcNow);
            await using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                TransferLimits.DefaultStreamBufferBytes,
                useAsync: true);
            return await BoundedStreamCopy.ReadAllBytesAsync(
                    stream,
                    TransferLimits.MaxThumbnailBytes,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        public async Task SetAsync(
            string key,
            byte[] data,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(data);
            if (data.LongLength > TransferLimits.MaxThumbnailBytes)
                throw new TransferLimitExceededException(
                    TransferLimits.MaxThumbnailBytes,
                    data.LongLength);

            var path = GetCachePath(key);
            await File.WriteAllBytesAsync(path, data, cancellationToken).ConfigureAwait(false);
            _ = Task.Run(EvictIfNeeded, CancellationToken.None);
        }

        private void EvictIfNeeded()
        {
            try
            {
                // Sort newest-first so we keep recent entries and delete oldest.
                var files = new DirectoryInfo(CacheDir)
                    .GetFiles("*.thumb")
                    .OrderByDescending(f => f.LastWriteTimeUtc)
                    .ToList();

                long total = files.Sum(f => f.Length);
                // Iterate from oldest (end of list) to newest, deleting until under budget.
                for (int i = files.Count - 1; i >= 0; i--)
                {
                    if (total <= MaxCacheSizeBytes) break;
                    total -= files[i].Length;
                    files[i].Delete();
                }
            }
            catch { /* ignore eviction errors */ }
        }

        private static string GetCachePath(string key)
        {
            // Sanitise key → safe filename
            var safe = Convert.ToHexString(
                System.Security.Cryptography.SHA256.HashData(
                    System.Text.Encoding.UTF8.GetBytes(key)));
            return Path.Combine(CacheDir, safe + ".thumb");
        }
    }
}
