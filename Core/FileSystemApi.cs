using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using VcfEditor.Helpers;
using VcfEditor.Core.IO;
using VcfEditor.Models.DTOs;

namespace VcfEditor.Core
{
    /// <summary>
    /// Implements all /api/v2/files/* and /api/v2/stream/* endpoints.
    ///
    /// Features:
    ///   • Directory listing with on-demand navigation
    ///   • Streaming download with SHA-256 verification and progress reporting
    ///   • Single-request upload for files &lt;= 10 MB
    ///   • Delete and mkdir operations
    /// </summary>
    public sealed class FileSystemApi
    {
        private static readonly ILogger Logger = AppLoggerFactory.CreateLogger(nameof(FileSystemApi));

        private const string V2 = "/api/v2";
        private const long ChunkedThresholdBytes = 10 * 1024 * 1024;  // 10 MB
        private const int DefaultChunkSize = 1024 * 1024;              // 1 MB
        private const int MinimumNegotiatedChunkSize = 64 * 1024;
        private const int MaximumNegotiatedChunkSize = 4 * 1024 * 1024;

        private readonly HttpTransport _transport;
        private readonly SessionManager _session;
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true
        };

        public FileSystemApi(HttpTransport transport, SessionManager session)
        {
            _transport = transport;
            _session = session;
        }

        // ── Directory listing ─────────────────────────────────────────────────

        /// <summary>List the contents of [path] on the phone. Defaults to /sdcard/.</summary>
        public async Task<DirectoryListingDto> ListDirectoryAsync(
            string? path = null,
            CancellationToken cancellationToken = default)
        {
            _session.EnsureConnected();
            var qs = path != null ? $"?path={Uri.EscapeDataString(path)}" : string.Empty;
            var json = await Send(HttpMethod.Get, $"/files{qs}", cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            return JsonSerializer.Deserialize<DirectoryListingDto>(json, JsonOptions)
                ?? new DirectoryListingDto();
        }

        // ── Download ──────────────────────────────────────────────────────────

        /// <summary>
        /// Streams in chunks, verifies SHA-256 on completion.
        /// Reports progress via [progress] callback: (bytesReceived, totalBytes).
        /// </summary>
        public async Task DownloadFileAsync(
            string remotePath,
            string localPath,
            IProgress<(long received, long total)>? progress = null,
            CancellationToken cancellationToken = default)
        {
            _session.EnsureConnected();
            var fullUrl = _transport.BaseUrl + V2 + $"/files/download?path={Uri.EscapeDataString(remotePath)}";
            var signaturePath = V2 + "/files/download";
            var tempPath = localPath + ".partial";
            var localDirectory = Path.GetDirectoryName(localPath);
            if (!string.IsNullOrWhiteSpace(localDirectory)) Directory.CreateDirectory(localDirectory);

            long resumeOffset = 0;
            if (File.Exists(tempPath))
            {
                try { resumeOffset = new FileInfo(tempPath).Length; }
                catch { resumeOffset = 0; }
            }
            if (resumeOffset > TransferLimits.MaxFileTransferBytes)
            {
                File.Delete(tempPath);
                throw new TransferLimitExceededException(
                    TransferLimits.MaxFileTransferBytes,
                    resumeOffset);
            }

            using var response = await _transport.SendRawAuthenticatedAsync(
                HttpMethod.Get,
                fullUrl,
                signaturePath,
                content: null,
                configureRequest: req =>
                {
                    if (resumeOffset > 0)
                        req.Headers.Range = new RangeHeaderValue(resumeOffset, null);
                },
                cancellationToken: cancellationToken).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                var err = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                throw new PhoneConnectionException($"Download failed ({(int)response.StatusCode}): {err}");
            }

            var expectedChecksum = response.Headers.TryGetValues("X-Checksum-SHA256", out var vals)
                ? vals.FirstOrDefault() : null;

            // If server didn't honour Range (200 OK), restart from scratch.
            if (response.StatusCode == System.Net.HttpStatusCode.OK && resumeOffset > 0)
            {
                resumeOffset = 0;
                try { File.Delete(tempPath); } catch { /* ignore */ }
            }

            var responseLength = response.Content.Headers.ContentLength;
            var total = responseLength ?? -1L;
            if (response.StatusCode == System.Net.HttpStatusCode.PartialContent && total >= 0)
                total += resumeOffset;
            if (total > TransferLimits.MaxFileTransferBytes)
                throw new TransferLimitExceededException(TransferLimits.MaxFileTransferBytes, total);

            var remainingLimit = TransferLimits.MaxFileTransferBytes - resumeOffset;
            BoundedStreamCopy.ValidateDeclaredLength(
                responseLength,
                remainingLimit,
                "file download");

            try
            {
                await using var responseStream = await response.Content
                    .ReadAsStreamAsync(cancellationToken)
                    .ConfigureAwait(false);
                await using var fileStream = new FileStream(
                    tempPath,
                    resumeOffset > 0 ? FileMode.Append : FileMode.Create,
                    FileAccess.Write,
                    FileShare.None,
                    TransferLimits.DefaultStreamBufferBytes,
                    useAsync: true);
                var byteProgress = progress is null
                    ? null
                    : new Progress<long>(copied => progress.Report((resumeOffset + copied, total)));
                await BoundedStreamCopy.CopyAsync(
                        responseStream,
                        fileStream,
                        remainingLimit,
                        progress: byteProgress,
                        cancellationToken: cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (TransferLimitExceededException)
            {
                if (File.Exists(tempPath)) File.Delete(tempPath);
                throw;
            }

            // Verify checksum AFTER full file exists (works for resumed downloads).
            if (expectedChecksum != null)
            {
                await using var verifyStream = new FileStream(tempPath, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, useAsync: true);
                var hash = await SHA256.HashDataAsync(verifyStream, cancellationToken).ConfigureAwait(false);
                var actual = Convert.ToHexStringLower(hash);
                if (!string.Equals(actual, expectedChecksum, StringComparison.OrdinalIgnoreCase))
                {
                    File.Delete(tempPath);
                    throw new InvalidDataException(
                        $"Download checksum mismatch for '{remotePath}'. " +
                        $"Expected={expectedChecksum} Actual={actual}");
                }
            }

            File.Move(tempPath, localPath, overwrite: true);
            LogMessages.FileDownloaded(Logger, remotePath, localPath);
        }

        // ── Upload ────────────────────────────────────────────────────────────

        /// <summary>
        /// Files &lt;= 10 MB: single multipart POST.
        /// Files &gt; 10 MB: chunked upload via /api/v2/stream/*.
        /// </summary>
        public async Task<UploadResultDto> UploadFileAsync(
            string localPath,
            string remoteDirectory,
            IProgress<(long sent, long total)>? progress = null,
            CancellationToken cancellationToken = default)
        {
            _session.EnsureConnected();
            var fileInfo = new FileInfo(localPath);
            if (!fileInfo.Exists)
                throw new FileNotFoundException($"Local file not found: {localPath}");
            if (fileInfo.Length > TransferLimits.MaxFileTransferBytes)
                throw new TransferLimitExceededException(
                    TransferLimits.MaxFileTransferBytes,
                    fileInfo.Length);

            var checksum = await ComputeSha256FileAsync(localPath, cancellationToken).ConfigureAwait(false);

            if (fileInfo.Length <= ChunkedThresholdBytes)
                return await UploadSingleAsync(localPath, remoteDirectory, checksum, progress, cancellationToken)
                    .ConfigureAwait(false);
            else
                return await UploadChunkedAsync(localPath, remoteDirectory, checksum, progress, cancellationToken)
                    .ConfigureAwait(false);
        }

        public async Task<UploadResultDto> UploadFileToPathAsync(
            string localPath,
            string destinationPath,
            IProgress<(long sent, long total)>? progress = null,
            CancellationToken cancellationToken = default)
        {
            _session.EnsureConnected();
            var fileInfo = new FileInfo(localPath);
            if (!fileInfo.Exists)
                throw new FileNotFoundException($"Local file not found: {localPath}");
            if (fileInfo.Length > TransferLimits.MaxFileTransferBytes)
                throw new TransferLimitExceededException(
                    TransferLimits.MaxFileTransferBytes,
                    fileInfo.Length);

            var checksum = await ComputeSha256FileAsync(localPath, cancellationToken).ConfigureAwait(false);

            var destDir = Path.GetDirectoryName(destinationPath.Replace('/', Path.DirectorySeparatorChar))
                ?.Replace(Path.DirectorySeparatorChar, '/')
                ?? "/sdcard";
            var destName = Path.GetFileName(destinationPath);
            if (string.IsNullOrWhiteSpace(destName))
                throw new ArgumentException("destinationPath must include a file name", nameof(destinationPath));

            if (fileInfo.Length <= ChunkedThresholdBytes)
                return await UploadSingleToPathAsync(localPath, destinationPath, checksum, progress, cancellationToken)
                    .ConfigureAwait(false);
            else
                return await UploadChunkedWithNameAsync(localPath, destDir, destName, checksum, progress, cancellationToken)
                    .ConfigureAwait(false);
        }

        private async Task<UploadResultDto> UploadSingleAsync(
            string localPath, string remoteDirectory, string checksum,
            IProgress<(long sent, long total)>? progress,
            CancellationToken cancellationToken)
        {
            var fileName = Path.GetFileName(localPath);
            var destPath = remoteDirectory.TrimEnd('/') + "/" + fileName;

            using var content = new MultipartFormDataContent();
            content.Add(new StringContent(destPath), "destinationPath");
            content.Add(new StringContent(checksum), "checksum");

            await using var fileStream = new FileStream(
                localPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                TransferLimits.DefaultStreamBufferBytes,
                useAsync: true);
            var uploadProgress = progress is null
                ? null
                : new Progress<double>(value => progress.Report(((long)(value * fileStream.Length), fileStream.Length)));
            using var fileContent = new ProgressableStreamContent(
                fileStream,
                fileStream.Length,
                uploadProgress,
                maxBytes: TransferLimits.MaxFileTransferBytes);
            fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
            content.Add(fileContent, "file", fileName);

            var fullUrl = _transport.BaseUrl + V2 + "/files/upload";
            var signaturePath = V2 + "/files/upload";
            using var response = await _transport.SendRawAuthenticatedAsync(
                HttpMethod.Post, fullUrl, signaturePath, content: content, cancellationToken: cancellationToken).ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
                throw new PhoneConnectionException($"Upload failed ({(int)response.StatusCode}): {body}");

            return JsonSerializer.Deserialize<UploadResultDto>(body, JsonOptions) ?? new UploadResultDto();
        }

        private async Task<UploadResultDto> UploadSingleToPathAsync(
            string localPath, string destinationPath, string checksum,
            IProgress<(long sent, long total)>? progress,
            CancellationToken cancellationToken)
        {
            var fileName = Path.GetFileName(destinationPath);

            using var content = new MultipartFormDataContent();
            content.Add(new StringContent(destinationPath), "destinationPath");
            content.Add(new StringContent(checksum), "checksum");

            await using var fileStream = new FileStream(
                localPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                TransferLimits.DefaultStreamBufferBytes,
                useAsync: true);
            var uploadProgress = progress is null
                ? null
                : new Progress<double>(value => progress.Report(((long)(value * fileStream.Length), fileStream.Length)));
            using var fileContent = new ProgressableStreamContent(
                fileStream,
                fileStream.Length,
                uploadProgress,
                maxBytes: TransferLimits.MaxFileTransferBytes);
            fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
            content.Add(fileContent, "file", fileName);

            var fullUrl = _transport.BaseUrl + V2 + "/files/upload";
            var signaturePath = V2 + "/files/upload";
            using var response = await _transport.SendRawAuthenticatedAsync(
                HttpMethod.Post, fullUrl, signaturePath, content: content, cancellationToken: cancellationToken).ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
                throw new PhoneConnectionException($"Upload failed ({(int)response.StatusCode}): {body}");

            return JsonSerializer.Deserialize<UploadResultDto>(body, JsonOptions) ?? new UploadResultDto();
        }

        private async Task<UploadResultDto> UploadChunkedAsync(
            string localPath, string remoteDirectory, string checksum,
            IProgress<(long sent, long total)>? progress,
            CancellationToken cancellationToken)
        {
            var fileName = Path.GetFileName(localPath);
            var fileSize = new FileInfo(localPath).Length;

            // 1. Init session
            var initBody = JsonSerializer.Serialize(new
            {
                fileName,
                destinationDirectory = remoteDirectory,
                totalSize = fileSize,
                checksum,
                chunkSize = DefaultChunkSize
            }, JsonOptions);

            var initJson = await Send(HttpMethod.Post, "/stream/init", initBody, cancellationToken)
                .ConfigureAwait(false);
            var initResp = JsonSerializer.Deserialize<StreamInitResponseDto>(initJson, JsonOptions);
            var (transferId, chunkSize, totalChunks) = ValidateTransferSession(initResp, fileSize);

            // 2. Check for partially uploaded chunks (resume support)
            var statusJson = await Send(HttpMethod.Get, $"/stream/{transferId}/status",
                cancellationToken: cancellationToken).ConfigureAwait(false);
            var status = JsonSerializer.Deserialize<StreamStatusResponseDto>(statusJson, JsonOptions);
            var receivedChunks = status?.ChunksReceived?.ToHashSet() ?? new HashSet<int>();

            // 3. Upload chunks
            long sent = CalculateReceivedBytes(receivedChunks, fileSize, chunkSize, totalChunks);

            using var fileStream = new FileStream(localPath, FileMode.Open, FileAccess.Read,
                FileShare.Read, chunkSize, useAsync: true);

            var buffer = new byte[chunkSize];
            for (int i = 0; i < totalChunks; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (receivedChunks.Contains(i)) { fileStream.Seek((long)i * chunkSize, SeekOrigin.Begin); continue; }

                fileStream.Seek((long)i * chunkSize, SeekOrigin.Begin);
                var expectedBytes = (int)Math.Min(chunkSize, fileSize - ((long)i * chunkSize));
                var read = await ReadExactlyAsync(fileStream, buffer, expectedBytes, cancellationToken)
                    .ConfigureAwait(false);

                var chunkUrl = _transport.BaseUrl + V2 + $"/stream/{transferId}/chunk/{i}";
                var chunkSignaturePath = V2 + $"/stream/{transferId}/chunk/{i}";
                var chunkContent = new ByteArrayContent(buffer, 0, read);
                using var chunkResp = await _transport.SendRawAuthenticatedAsync(
                    HttpMethod.Put, chunkUrl, chunkSignaturePath, content: chunkContent, cancellationToken: cancellationToken)
                    .ConfigureAwait(false);
                if (!chunkResp.IsSuccessStatusCode)
                    throw new PhoneConnectionException($"Chunk {i} upload failed: {(int)chunkResp.StatusCode}");

                sent += read;
                progress?.Report((sent, fileSize));
            }

            // 4. Complete
            var completeJson = await Send(HttpMethod.Post, $"/stream/{transferId}/complete",
                cancellationToken: cancellationToken).ConfigureAwait(false);
            var complete = JsonSerializer.Deserialize<StreamCompleteResponseDto>(completeJson, JsonOptions)!;

            LogMessages.ChunkedUploadCompleted(Logger, complete.FinalPath);
            return new UploadResultDto { Path = complete.FinalPath, Size = fileSize, Checksum = checksum };
        }

        private async Task<UploadResultDto> UploadChunkedWithNameAsync(
            string localPath, string remoteDirectory, string remoteFileName, string checksum,
            IProgress<(long sent, long total)>? progress,
            CancellationToken cancellationToken)
        {
            var fileSize = new FileInfo(localPath).Length;

            var initBody = JsonSerializer.Serialize(new
            {
                fileName = remoteFileName,
                destinationDirectory = remoteDirectory,
                totalSize = fileSize,
                checksum,
                chunkSize = DefaultChunkSize
            }, JsonOptions);

            var initJson = await Send(HttpMethod.Post, "/stream/init", initBody, cancellationToken)
                .ConfigureAwait(false);
            var initResp = JsonSerializer.Deserialize<StreamInitResponseDto>(initJson, JsonOptions);
            var (transferId, chunkSize, totalChunks) = ValidateTransferSession(initResp, fileSize);

            var statusJson = await Send(HttpMethod.Get, $"/stream/{transferId}/status",
                cancellationToken: cancellationToken).ConfigureAwait(false);
            var status = JsonSerializer.Deserialize<StreamStatusResponseDto>(statusJson, JsonOptions);
            var receivedChunks = status?.ChunksReceived?.ToHashSet() ?? new HashSet<int>();

            long sent = CalculateReceivedBytes(receivedChunks, fileSize, chunkSize, totalChunks);

            using var fileStream = new FileStream(localPath, FileMode.Open, FileAccess.Read,
                FileShare.Read, chunkSize, useAsync: true);

            var buffer = new byte[chunkSize];
            for (int i = 0; i < totalChunks; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (receivedChunks.Contains(i)) { fileStream.Seek((long)i * chunkSize, SeekOrigin.Begin); continue; }

                fileStream.Seek((long)i * chunkSize, SeekOrigin.Begin);
                var expectedBytes = (int)Math.Min(chunkSize, fileSize - ((long)i * chunkSize));
                var read = await ReadExactlyAsync(fileStream, buffer, expectedBytes, cancellationToken)
                    .ConfigureAwait(false);

                var chunkUrl = _transport.BaseUrl + V2 + $"/stream/{transferId}/chunk/{i}";
                var chunkSignaturePath = V2 + $"/stream/{transferId}/chunk/{i}";
                var chunkContent = new ByteArrayContent(buffer, 0, read);
                using var chunkResp = await _transport.SendRawAuthenticatedAsync(
                    HttpMethod.Put, chunkUrl, chunkSignaturePath, content: chunkContent, cancellationToken: cancellationToken)
                    .ConfigureAwait(false);
                if (!chunkResp.IsSuccessStatusCode)
                    throw new PhoneConnectionException($"Chunk {i} upload failed: {(int)chunkResp.StatusCode}");

                sent += read;
                progress?.Report((sent, fileSize));
            }

            var completeJson = await Send(HttpMethod.Post, $"/stream/{transferId}/complete",
                cancellationToken: cancellationToken).ConfigureAwait(false);
            var complete = JsonSerializer.Deserialize<StreamCompleteResponseDto>(completeJson, JsonOptions)!;

            LogMessages.ChunkedUploadCompleted(Logger, complete.FinalPath);
            return new UploadResultDto { Path = complete.FinalPath, Size = fileSize, Checksum = checksum };
        }

        private static (string TransferId, int ChunkSize, int TotalChunks) ValidateTransferSession(
            StreamInitResponseDto? response, long fileSize)
        {
            if (response == null || string.IsNullOrWhiteSpace(response.TransferId))
                throw new InvalidDataException("The phone returned an invalid transfer session.");
            if (response.ChunkSize < MinimumNegotiatedChunkSize ||
                response.ChunkSize > MaximumNegotiatedChunkSize)
            {
                throw new InvalidDataException(
                    $"The phone negotiated an unsupported chunk size: {response.ChunkSize} bytes.");
            }

            var totalChunksLong = fileSize == 0
                ? 0
                : ((fileSize - 1) / response.ChunkSize) + 1;
            if (totalChunksLong > int.MaxValue)
                throw new InvalidDataException("The transfer requires too many chunks.");

            return (response.TransferId, response.ChunkSize, (int)totalChunksLong);
        }

        private static long CalculateReceivedBytes(
            IEnumerable<int> receivedChunks, long fileSize, int chunkSize, int totalChunks)
        {
            long received = 0;
            foreach (var index in receivedChunks.Distinct())
            {
                if (index < 0 || index >= totalChunks) continue;
                var offset = (long)index * chunkSize;
                received += Math.Min(chunkSize, fileSize - offset);
            }
            return received;
        }

        private static async Task<int> ReadExactlyAsync(
            Stream stream, byte[] buffer, int expectedBytes, CancellationToken cancellationToken)
        {
            var totalRead = 0;
            while (totalRead < expectedBytes)
            {
                var read = await stream.ReadAsync(
                    buffer.AsMemory(totalRead, expectedBytes - totalRead), cancellationToken)
                    .ConfigureAwait(false);
                if (read == 0)
                    throw new EndOfStreamException("The local file ended before the expected chunk was read.");
                totalRead += read;
            }
            return totalRead;
        }

        // ── Delete & Mkdir ─────────────────────────────────────────────────────

        public async Task<bool> DeleteAsync(string remotePath, bool recursive = false,
            CancellationToken cancellationToken = default)
        {
            _session.EnsureConnected();
            var qs = $"?path={Uri.EscapeDataString(remotePath)}" + (recursive ? "&recursive=true" : "");
            var json = await Send(HttpMethod.Delete, $"/files{qs}", cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            var result = JsonSerializer.Deserialize<DeleteResultDto>(json, JsonOptions);
            return result?.Success == true;
        }

        public async Task<string?> MkdirAsync(string remotePath,
            CancellationToken cancellationToken = default)
        {
            _session.EnsureConnected();
            var body = JsonSerializer.Serialize(new { path = remotePath }, JsonOptions);
            var json = await Send(HttpMethod.Post, "/files/mkdir", body, cancellationToken)
                .ConfigureAwait(false);
            var result = JsonSerializer.Deserialize<MkdirResultDto>(json, JsonOptions);
            return result?.Path;
        }

        public async Task<FileEntryDto> RenameAsync(
            string remotePath,
            string newName,
            bool overwrite = false,
            CancellationToken cancellationToken = default)
        {
            _session.EnsureConnected();
            var body = JsonSerializer.Serialize(new { path = remotePath, newName, overwrite }, JsonOptions);
            var json = await Send(HttpMethod.Post, "/files/rename", body, cancellationToken)
                .ConfigureAwait(false);
            return JsonSerializer.Deserialize<FileEntryDto>(json, JsonOptions) ?? new FileEntryDto();
        }

        public async Task<FileEntryDto> MoveAsync(
            string fromPath,
            string toPath,
            bool overwrite = false,
            CancellationToken cancellationToken = default)
        {
            _session.EnsureConnected();
            var body = JsonSerializer.Serialize(new { fromPath, toPath, overwrite }, JsonOptions);
            var json = await Send(HttpMethod.Post, "/files/move", body, cancellationToken)
                .ConfigureAwait(false);
            return JsonSerializer.Deserialize<FileEntryDto>(json, JsonOptions) ?? new FileEntryDto();
        }

        // ── Private helpers ───────────────────────────────────────────────────

        private Task<string> Send(
            HttpMethod method, string path, string? body = null,
            CancellationToken cancellationToken = default)
        {
            var queryIdx = path.IndexOf('?');
            var signaturePath = V2 + (queryIdx != -1 ? path[..queryIdx] : path);
            var fullUrl = _transport.BaseUrl + V2 + path;
            return _transport.SendAsync(method, fullUrl, signaturePath, body, cancellationToken);
        }

        private static string ComputeSha256(string input) =>
            Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(input)));

        private static async Task<string> ComputeSha256FileAsync(
            string filePath, CancellationToken cancellationToken)
        {
            await using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read,
                FileShare.Read, 65536, useAsync: true);
            var hash = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
            return Convert.ToHexStringLower(hash);
        }
    }
}
