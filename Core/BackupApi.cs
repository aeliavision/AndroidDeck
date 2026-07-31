using System;
using System.Collections.Generic;
using System.Buffers.Binary;
using System.IO;
using System.Globalization;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using VcfEditor.Core.Settings;
using VcfEditor.Core.IO;

namespace VcfEditor.Core
{
    // ── DTOs ─────────────────────────────────────────────────────────────────

    public record BackupCreateRequest(
        [property: JsonPropertyName("types")]  List<string> Types,
        [property: JsonPropertyName("paths")]  List<string> Paths,
        [property: JsonPropertyName("encrypt")] bool Encrypt,
        [property: JsonPropertyName("incremental")] bool Incremental,
        [property: JsonPropertyName("sinceMs")] long? SinceMs
    );

    public record BackupCreateResponse(
        [property: JsonPropertyName("backupId")]           string BackupId,
        [property: JsonPropertyName("estimatedItemCount")] int    EstimatedItemCount,
        [property: JsonPropertyName("status")]             string Status
    );

    public record BackupStatusResponse(
        [property: JsonPropertyName("backupId")]        string  BackupId,
        [property: JsonPropertyName("progress")]        float   Progress,
        [property: JsonPropertyName("phase")]           string  Phase,
        [property: JsonPropertyName("currentItem")]     string  CurrentItem,
        [property: JsonPropertyName("itemCount")]       int     ItemCount,
        [property: JsonPropertyName("processedItems")]  int     ProcessedItems,
        [property: JsonPropertyName("archiveSize")]     long    ArchiveSize,
        [property: JsonPropertyName("error")]           string? Error
    );

    public record RestoreStartResponse(
        [property: JsonPropertyName("restoreId")] string RestoreId,
        [property: JsonPropertyName("status")]    string Status
    );

    public record RestoreItemOutcome(
        [property: JsonPropertyName("path")] string Path,
        [property: JsonPropertyName("status")] string Status,
        [property: JsonPropertyName("conflict")] string? Conflict,
        [property: JsonPropertyName("error")] string? Error)
    {
        [JsonIgnore]
        public string Detail => !string.IsNullOrWhiteSpace(Error)
            ? Error
            : !string.IsNullOrWhiteSpace(Conflict)
                ? Conflict
                : Status;
    }

    public record RestoreStatusResponse(
        [property: JsonPropertyName("restoreId")]     string  RestoreId,
        [property: JsonPropertyName("progress")]      float   Progress,
        [property: JsonPropertyName("phase")]         string  Phase,
        [property: JsonPropertyName("restoredItems")] int     RestoredItems,
        [property: JsonPropertyName("failedItems")]   int     FailedItems,
        [property: JsonPropertyName("skippedItems")]  int     SkippedItems,
        [property: JsonPropertyName("error")]         string? Error,
        [property: JsonPropertyName("itemResults")]   List<RestoreItemOutcome>? ItemResults = null
    );

    public record BackupHistoryEntry(
        [property: JsonPropertyName("backupId")]    string       BackupId,
        [property: JsonPropertyName("createdAt")]   long         CreatedAt,
        [property: JsonPropertyName("types")]       List<string> Types,
        [property: JsonPropertyName("archiveSize")] long         ArchiveSize,
        [property: JsonPropertyName("itemCount")]   int          ItemCount
    )
    {
        // ── Display helpers (no converters needed in XAML) ────────────────────

        /// <summary>Epoch ms → local date/time string for GridView binding.</summary>
        public string CreatedAtDisplay =>
            DateTimeOffset.FromUnixTimeMilliseconds(CreatedAt).LocalDateTime
                          .ToString("yyyy-MM-dd  HH:mm", CultureInfo.CurrentCulture);

        /// <summary>Comma-joined type list for GridView binding.</summary>
        public string TypesDisplay => string.Join(", ", Types);

        /// <summary>Human-readable file size for GridView binding.</summary>
        public string ArchiveSizeDisplay => ArchiveSize switch
        {
            >= 1_073_741_824 => $"{ArchiveSize / 1_073_741_824.0:F1} GB",
            >= 1_048_576     => $"{ArchiveSize / 1_048_576.0:F1} MB",
            >= 1_024         => $"{ArchiveSize / 1_024.0:F1} KB",
            _                => $"{ArchiveSize} B"
        };
    }

    public record BackupHistoryResponse(
        [property: JsonPropertyName("backups")] List<BackupHistoryEntry> Backups
    );

    public record BackupManifestResponse(
        [property: JsonPropertyName("version")] int Version,
        [property: JsonPropertyName("supportedTypes")] List<string> SupportedTypes,
        [property: JsonPropertyName("defaultPaths")] List<string> DefaultPaths,
        [property: JsonPropertyName("supportsIncremental")] bool SupportsIncremental
    );

    // ── BackupApi ─────────────────────────────────────────────────────────────

    /// <summary>
    /// P5 — Client for the /api/v2/backup/* endpoints on the Android companion.
    /// Uses <see cref="HttpTransport.SendAsync"/> (HMAC-signed JSON) for text endpoints
    /// and <see cref="HttpTransport.SendRawAuthenticatedAsync"/> for binary streaming.
    /// </summary>
    public class BackupApi
    {
        private readonly HttpTransport _transport;
        private readonly IAppSettingsStore _settingsStore;

        private static class BackupArchiveFormat
        {
            public static readonly byte[] Magic = { 0x56, 0x43, 0x46, 0x42 }; // "VCFB"

            public const byte VersionV1 = 0x01;
            public const byte VersionV2 = 0x02;
            public const byte VersionV3 = 0x03;

            public const int SaltBytes = 16;
            public const int IvBytes = 12;
            public const int SeedIdBytes = 16;
            public const int ZipHashBytes = 32;
            public const int ZipSizeBytes = 8;
            public const int GcmTagBytes = 16;

            public const int HeaderV1Bytes = 4 + 1 + SaltBytes + IvBytes;
            public const int HeaderV2Bytes = HeaderV1Bytes + ZipHashBytes + ZipSizeBytes;
            public const int HeaderV3Bytes = 4 + 1 + SeedIdBytes + SaltBytes + IvBytes + ZipHashBytes + ZipSizeBytes;
        }

        private static readonly JsonSerializerOptions JsonOpts = new()
        {
            PropertyNameCaseInsensitive = true
        };

        private record SeedExportResponse(
            [property: JsonPropertyName("seedId")] string SeedId,
            [property: JsonPropertyName("seed")] string Seed
        );

        public BackupApi(HttpTransport transport, IAppSettingsStore settingsStore)
        {
            ArgumentNullException.ThrowIfNull(transport);
            _transport = transport;
            ArgumentNullException.ThrowIfNull(settingsStore);
            _settingsStore = settingsStore;
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private string BaseUrl => _transport.BaseUrl
            ?? throw new InvalidOperationException("HttpTransport not initialised.");

        private static T Deserialize<T>(string json) =>
            JsonSerializer.Deserialize<T>(json, JsonOpts)
            ?? throw new InvalidOperationException($"Empty/null response deserialising {typeof(T).Name}");

        // ── Create ────────────────────────────────────────────────────────────

        /// <summary>
        /// POST /api/v2/backup/create — initiates a backup on the device.
        /// Returns immediately with a backupId; poll <see cref="GetStatusAsync"/> for progress.
        /// </summary>
        public async Task<BackupCreateResponse> CreateBackupAsync(
            IEnumerable<string> types,
            IEnumerable<string>? extraPaths = null,
            bool encrypt = true,
            bool incremental = false,
            long? sinceMs = null,
            CancellationToken ct = default)
        {
            var req = new BackupCreateRequest(
                Types: new List<string>(types),
                Paths: extraPaths is null ? new List<string>() : new List<string>(extraPaths),
                Encrypt: encrypt,
                Incremental: incremental,
                SinceMs: sinceMs
            );
            var body = JsonSerializer.Serialize(req, JsonOpts);
            const string path = "/api/v2/backup/create";
            var json = await _transport.SendAsync(HttpMethod.Post, BaseUrl + path, path, body, ct);
            return Deserialize<BackupCreateResponse>(json);
        }

        /// <summary>GET /api/v2.1/backup/manifest — capability + defaults discovery.</summary>
        public async Task<BackupManifestResponse> GetManifestAsync(CancellationToken ct = default)
        {
            const string path = "/api/v2.1/backup/manifest";
            var json = await _transport.SendAsync(HttpMethod.Get, BaseUrl + path, path, null, ct);
            return Deserialize<BackupManifestResponse>(json);
        }

        // ── Status ────────────────────────────────────────────────────────────

        /// <summary>GET /api/v2/backup/{backupId}/status</summary>
        public async Task<BackupStatusResponse> GetStatusAsync(
            string backupId, CancellationToken ct = default)
        {
            var path = $"/api/v2/backup/{backupId}/status";
            var json = await _transport.SendAsync(HttpMethod.Get, BaseUrl + path, path, null, ct);
            return Deserialize<BackupStatusResponse>(json);
        }

        // ── Download ──────────────────────────────────────────────────────────

        /// <summary>
        /// GET /api/v2/backup/{backupId}/download — streams the encrypted .vcfbak archive
        /// to <paramref name="destinationPath"/>.
        /// </summary>
        public async Task DownloadArchiveAsync(
            string backupId,
            string destinationPath,
            long totalBytes,
            IProgress<double>? onProgress = null,
            CancellationToken ct = default)
        {
            var path = $"/api/v2/backup/{backupId}/download";
            using var response = await _transport.SendRawAuthenticatedAsync(
                HttpMethod.Get, BaseUrl + path, path, cancellationToken: ct);
            response.EnsureSuccessStatusCode();

            var dir = Path.GetDirectoryName(destinationPath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

            BoundedStreamCopy.ValidateDeclaredLength(
                response.Content.Headers.ContentLength,
                TransferLimits.MaxBackupArchiveBytes,
                "backup archive");
            if (totalBytes > TransferLimits.MaxBackupArchiveBytes)
                throw new TransferLimitExceededException(
                    TransferLimits.MaxBackupArchiveBytes,
                    totalBytes);

            var temporaryPath = destinationPath + ".partial";
            try
            {
                await using (var networkStream = await response.Content.ReadAsStreamAsync(ct))
                await using (var fileStream = new FileStream(
                    temporaryPath,
                    FileMode.Create,
                    FileAccess.Write,
                    FileShare.None,
                    bufferSize: TransferLimits.DefaultStreamBufferBytes,
                    useAsync: true))
                {
                    var byteProgress = onProgress is null
                        ? null
                        : new Progress<long>(received =>
                        {
                            var denominator = totalBytes > 0
                                ? totalBytes
                                : response.Content.Headers.ContentLength ?? 0;
                            if (denominator > 0)
                                onProgress.Report(Math.Min(1.0, (double)received / denominator));
                        });
                    await BoundedStreamCopy.CopyAsync(
                            networkStream,
                            fileStream,
                            TransferLimits.MaxBackupArchiveBytes,
                            progress: byteProgress,
                            cancellationToken: ct)
                        .ConfigureAwait(false);
                    await fileStream.FlushAsync(ct).ConfigureAwait(false);
                }

                // Detect truncated/corrupted archives only after the writer is closed.
                ValidateBackupArchiveFileLength(temporaryPath);
                File.Move(temporaryPath, destinationPath, overwrite: true);
                onProgress?.Report(1.0);
            }
            catch
            {
                if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
                throw;
            }

            // Cross-device restore: cache the seed for v3 archives while we are still
            // connected to the source device.
            var seedId = TryParseSeedIdFromArchive(destinationPath);
            if (!string.IsNullOrWhiteSpace(seedId))
            {
                var cachedSeed = await TryFetchAndCacheSeedAsync(seedId, ct).ConfigureAwait(false);
                if (cachedSeed is not null)
                    CryptographicOperations.ZeroMemory(cachedSeed);
            }
        }

        // ── Restore ───────────────────────────────────────────────────────────

        /// <summary>
        /// POST /api/v2/backup/restore — uploads a .vcfbak archive to the device via multipart.
        /// </summary>
        public async Task<RestoreStartResponse> StartRestoreAsync(
            string archivePath,
            IProgress<double>? onProgress = null,
            CancellationToken ct = default)
        {
            // Preflight before upload: validate format and enforce the archive size ceiling.
            ValidateBackupArchiveFileLength(archivePath);
            var archiveLength = new FileInfo(archivePath).Length;
            if (archiveLength > TransferLimits.MaxBackupArchiveBytes)
                throw new TransferLimitExceededException(
                    TransferLimits.MaxBackupArchiveBytes,
                    archiveLength);

            await using var fileStream = new FileStream(
                archivePath, FileMode.Open, FileAccess.Read,
                FileShare.Read, bufferSize: TransferLimits.DefaultStreamBufferBytes, useAsync: true);

            using var content = new MultipartFormDataContent();

            // Cross-device restore: if this is a v3 archive and we have the seed cached,
            // provide it to the device so it can import + decrypt.
            var seedId = TryParseSeedIdFromArchive(archivePath);
            if (!string.IsNullOrWhiteSpace(seedId))
            {
                var seed = _settingsStore.GetBackupSeed(seedId);
                try
                {
                    if (seed is null || seed.Length == 0)
                    {
                        if (seed is not null)
                            CryptographicOperations.ZeroMemory(seed);
                        seed = await TryFetchAndCacheSeedAsync(seedId, ct).ConfigureAwait(false);
                    }

                    if (seed is { Length: > 0 })
                    {
                        content.Add(new StringContent(seedId, Encoding.UTF8), "seedId");
                        content.Add(new StringContent(Convert.ToBase64String(seed), Encoding.UTF8), "seed");
                    }
                }
                finally
                {
                    if (seed is not null)
                        CryptographicOperations.ZeroMemory(seed);
                }
            }
            using var sc = new ProgressableStreamContent(
                fileStream,
                totalBytes: fileStream.Length,
                progress: onProgress,
                maxBytes: TransferLimits.MaxBackupArchiveBytes);
            sc.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
            content.Add(sc, "archive", Path.GetFileName(archivePath));

            const string path = "/api/v2/backup/restore";
            using var response = await _transport.SendRawAuthenticatedAsync(
                HttpMethod.Post, BaseUrl + path, path, content: content, cancellationToken: ct);
            response.EnsureSuccessStatusCode();
            var json = await response.Content.ReadAsStringAsync(ct);
            return Deserialize<RestoreStartResponse>(json);
        }

        private async Task<byte[]?> TryFetchAndCacheSeedAsync(string seedId, CancellationToken ct)
        {
            var cached = _settingsStore.GetBackupSeed(seedId);
            if (cached is { Length: > 0 })
                return cached;
            if (cached is not null)
                CryptographicOperations.ZeroMemory(cached);

            try
            {
                var path = $"/api/v2/backup/seed/{seedId}";
                var json = await _transport.SendAsync(HttpMethod.Get, BaseUrl + path, path, null, ct);
                var response = Deserialize<SeedExportResponse>(json);
                if (string.IsNullOrWhiteSpace(response.Seed))
                    return null;

                var seed = Convert.FromBase64String(response.Seed);
                _settingsStore.SetBackupSeed(response.SeedId, seed);
                return seed;
            }
            catch (FormatException)
            {
                return null;
            }
            catch (HttpRequestException)
            {
                return null;
            }
        }

        private static string? TryParseSeedIdFromArchive(string archivePath)
        {
            try
            {
                using var fs = new FileStream(archivePath, FileMode.Open, FileAccess.Read, FileShare.Read);

                Span<byte> magic = stackalloc byte[4];
                if (fs.Read(magic) != 4) return null;
                if (!magic.SequenceEqual(BackupArchiveFormat.Magic)) return null;

                int ver = fs.ReadByte();
                if (ver != BackupArchiveFormat.VersionV3) return null;

                Span<byte> seedIdBytes = stackalloc byte[BackupArchiveFormat.SeedIdBytes];
                if (fs.Read(seedIdBytes) != BackupArchiveFormat.SeedIdBytes) return null;

                // Kotlin writes UUID as (mostSigBits, leastSigBits) using ByteBuffer (big-endian).
                long msb = BinaryPrimitives.ReadInt64BigEndian(seedIdBytes[..8]);
                long lsb = BinaryPrimitives.ReadInt64BigEndian(seedIdBytes.Slice(8, 8));

                // Do NOT use Guid(msb/lsb) because Guid string formatting has mixed endianness.
                // We want the exact Java/Kotlin UUID.toString() representation.
                uint a = (uint)(msb >> 32);
                ushort b = (ushort)((msb >> 16) & 0xFFFF);
                ushort c = (ushort)(msb & 0xFFFF);
                ushort d = (ushort)((lsb >> 48) & 0xFFFF);
                ulong e = (ulong)(lsb & 0xFFFFFFFFFFFF);

                return $"{a:x8}-{b:x4}-{c:x4}-{d:x4}-{e:x12}";
            }
            catch
            {
                return null;
            }
        }

        /// <summary>GET /api/v2/backup/restore/{restoreId}/status</summary>
        public async Task<RestoreStatusResponse> GetRestoreStatusAsync(
            string restoreId, CancellationToken ct = default)
        {
            var path = $"/api/v2/backup/restore/{restoreId}/status";
            var json = await _transport.SendAsync(HttpMethod.Get, BaseUrl + path, path, null, ct);
            return Deserialize<RestoreStatusResponse>(json);
        }

        // ── History ───────────────────────────────────────────────────────────

        /// <summary>GET /api/v2/backup/history</summary>
        public async Task<List<BackupHistoryEntry>> GetHistoryAsync(CancellationToken ct = default)
        {
            const string path = "/api/v2/backup/history";
            var json = await _transport.SendAsync(HttpMethod.Get, BaseUrl + path, path, null, ct);
            var result = JsonSerializer.Deserialize<BackupHistoryResponse>(json, JsonOpts);
            return result?.Backups ?? new List<BackupHistoryEntry>();
        }

        private static void ValidateBackupArchiveFileLength(string archivePath)
        {
            var archiveLength = new FileInfo(archivePath).Length;
            if (archiveLength > TransferLimits.MaxBackupArchiveBytes)
                throw new TransferLimitExceededException(
                    TransferLimits.MaxBackupArchiveBytes,
                    archiveLength);

            using var fs = new FileStream(archivePath, FileMode.Open, FileAccess.Read, FileShare.Read);

            Span<byte> magic = stackalloc byte[4];
            if (fs.Read(magic) != 4) return;
            if (!magic.SequenceEqual(BackupArchiveFormat.Magic)) return;

            int ver = fs.ReadByte();
            if (ver < 0) return;

            if (ver == BackupArchiveFormat.VersionV1)
            {
                // v1 has no integrity metadata; nothing to validate.
                return;
            }

            if (ver != BackupArchiveFormat.VersionV2 && ver != BackupArchiveFormat.VersionV3)
                throw new InvalidDataException($"Unsupported backup archive version: {ver}");

            if (ver == BackupArchiveFormat.VersionV3)
            {
                // seedId
                fs.Position += BackupArchiveFormat.SeedIdBytes;
            }

            // SALT + IV
            fs.Position += BackupArchiveFormat.SaltBytes + BackupArchiveFormat.IvBytes;

            // ZIP SHA-256 (ignored here; verified on-device during restore)
            fs.Position += BackupArchiveFormat.ZipHashBytes;

            Span<byte> zipSizeBuf = stackalloc byte[BackupArchiveFormat.ZipSizeBytes];
            if (fs.Read(zipSizeBuf) != BackupArchiveFormat.ZipSizeBytes)
                throw new InvalidDataException("Invalid backup archive header.");
            long zipSize = BinaryPrimitives.ReadInt64BigEndian(zipSizeBuf);
            if (zipSize < 0)
                throw new InvalidDataException("Invalid backup archive header (negative ZIP size).");

            long headerBytes = ver == BackupArchiveFormat.VersionV3
                ? BackupArchiveFormat.HeaderV3Bytes
                : BackupArchiveFormat.HeaderV2Bytes;
            long expectedTotal = headerBytes + zipSize + BackupArchiveFormat.GcmTagBytes;
            long actualTotal = fs.Length;
            if (actualTotal != expectedTotal)
            {
                throw new InvalidDataException(
                    $"Backup archive appears corrupted/truncated. Expected {expectedTotal} bytes, got {actualTotal} bytes.");
            }
        }
    }
}
