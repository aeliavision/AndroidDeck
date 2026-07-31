using System;
using System.IO;
using System.Globalization;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using VcfEditor.Helpers;
using VcfEditor.Core.Settings;

namespace VcfEditor.Core
{
    /// <summary>
    /// Owns the HttpClient lifecycle, TOFU certificate pinning, HMAC request signing,
    /// and retry logic. All other API modules (ContactsApi, FileSystemApi, …) depend on
    /// this class for raw HTTP sends.
    /// </summary>
    public sealed class HttpTransport : IDisposable
    {
        private static readonly ILogger Logger = AppLoggerFactory.CreateLogger(nameof(HttpTransport));

        // ── Constants ────────────────────────────────────────────────────────────

        private const int MaxRetryAttempts = 3;
        private static readonly JsonSerializerOptions ErrorJsonOptions = new()
        {
            PropertyNameCaseInsensitive = true
        };
        // much longer than typical JSON requests. Use a separate client with a longer timeout.
        private static readonly TimeSpan TransferTimeout = TimeSpan.FromHours(2);
        // keeping connections warm for streaming transfers.
        private static readonly TimeSpan PooledLifetime = TimeSpan.FromMinutes(5);
        private static readonly TimeSpan PooledIdleTimeout = TimeSpan.FromMinutes(2);
        private static readonly TimeSpan BaseRetryDelay = TimeSpan.FromMilliseconds(250);
        private const int MaxJitterMs = 200;

        // ── State ────────────────────────────────────────────────────────────────

        private HttpClient? _httpClient;
        private HttpClient? _transferHttpClient;
        private SocketsHttpHandler? _socketsHandler;
        private readonly object _httpClientLock = new();
        private string? _currentEndpointKey;

        private readonly string _clientId;
        private readonly bool _useHttps;
        private readonly IAppSettingsStore _settingsStore;

        // Allows cert re-pinning only during an explicit pair flow.
        private volatile bool _allowCertRePinDuringPairing;

        // Set by SessionManager after a successful pair.
        private byte[]? _hmacSecret;

        /// <summary>
        /// Base URL including scheme, host, port, and API prefix
        /// e.g. "https://192.168.1.5:8732/api/v1"
        /// </summary>
        public string? BaseUrl { get; private set; }

        public string? LastServerCertificateFingerprint { get; private set; }

        // ── Construction ─────────────────────────────────────────────────────────

        public HttpTransport(string clientId, IAppSettingsStore settingsStore, bool useHttps = false)
        {
            _clientId = clientId;
            ArgumentNullException.ThrowIfNull(settingsStore);
            _settingsStore = settingsStore;
            _useHttps = useHttps;
        }
        internal HttpTransport(string clientId, HttpMessageHandler handler, string baseUrl, bool useHttps = false)
            : this(clientId, handler, baseUrl, NullAppSettingsStore.Instance, useHttps)
        {
        }

        internal HttpTransport(
            string clientId,
            HttpMessageHandler handler,
            string baseUrl,
            IAppSettingsStore settingsStore,
            bool useHttps = false)
        {
            _clientId = clientId;
            ArgumentNullException.ThrowIfNull(settingsStore);
            _settingsStore = settingsStore;
            _useHttps = useHttps;
            BaseUrl = baseUrl;
            _currentEndpointKey = "test";
            _httpClient = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(Constants.RequestTimeoutSeconds)
            };

            // For tests, use the same handler/client instance for transfers.
            _transferHttpClient = _httpClient;
        }

        // ── Initialisation ───────────────────────────────────────────────────────

        /// <summary>
        /// Initialise (or re-initialise) the HttpClient for the given endpoint.
        /// Must be called before the first request and when the endpoint changes.
        /// Safe to call multiple times — only recreates the client when the endpoint key changes.
        /// </summary>
        public void EnsureInitialised(string host, int port)
        {
            var endpointKey = $"{host}:{port}";
            lock (_httpClientLock)
            {
                if (_httpClient != null &&
                    string.Equals(_currentEndpointKey, endpointKey, StringComparison.OrdinalIgnoreCase))
                    return;

                _currentEndpointKey = endpointKey;
                _httpClient?.Dispose();
                _transferHttpClient?.Dispose();
                _socketsHandler?.Dispose();
                _httpClient = null;
                _transferHttpClient = null;
                _socketsHandler = null;

                var scheme = _useHttps ? "https" : "http";
                BaseUrl = $"{scheme}://{host}:{port}";

                var socketsHandler = new SocketsHttpHandler
                {
                    PooledConnectionLifetime = PooledLifetime,
                    PooledConnectionIdleTimeout = PooledIdleTimeout,
                    // decompress them. Ktor server uses Compression plugin.
                    AutomaticDecompression = System.Net.DecompressionMethods.GZip |
                                              System.Net.DecompressionMethods.Deflate |
                                              System.Net.DecompressionMethods.Brotli,
                    // Keep small to avoid overwhelming the phone.
                    MaxConnectionsPerServer = 6,

                    // Prefer HTTP/2 where possible (Ktor/Netty may still negotiate HTTP/1.1).
                    EnableMultipleHttp2Connections = true
                };
                socketsHandler.KeepAlivePingDelay = TimeSpan.FromSeconds(30);
                socketsHandler.KeepAlivePingTimeout = TimeSpan.FromSeconds(15);
                socketsHandler.KeepAlivePingPolicy = HttpKeepAlivePingPolicy.Always;

                if (_useHttps)
                {
                    var storedPin = NormalizeFingerprint(
                        _settingsStore.GetPinnedCertSha256(endpointKey));

                    socketsHandler.SslOptions = new System.Net.Security.SslClientAuthenticationOptions
                    {
                        RemoteCertificateValidationCallback = (_, cert, _, _) =>
                        {
                            try
                            {
                                if (cert == null) return false;
                                using var cert2 = cert as X509Certificate2
                                    ?? X509CertificateLoader.LoadCertificate(cert.Export(X509ContentType.Cert));
                                var actual = NormalizeFingerprint(
                                    Convert.ToHexString(cert2.GetCertHash(HashAlgorithmName.SHA256)));
                                LastServerCertificateFingerprint = actual;

                                if (!string.IsNullOrWhiteSpace(storedPin))
                                {
                                    if (string.Equals(actual, storedPin, StringComparison.OrdinalIgnoreCase))
                                        return true;

                                    if (_allowCertRePinDuringPairing)
                                    {
                                        _settingsStore.SetPinnedCertSha256(endpointKey, actual ?? string.Empty);
                                        storedPin = actual;
                                        return true;
                                    }
                                    return false;
                                }

                                // TOFU: first connection — pin and trust.
                                _settingsStore.SetPinnedCertSha256(endpointKey, actual ?? string.Empty);
                                storedPin = actual;
                                return true;
                            }
                            catch { return false; }
                        }
                    };
                }

                _socketsHandler = socketsHandler;

                // Normal request client.
                _httpClient = new HttpClient(_socketsHandler, disposeHandler: false)
                {
                    Timeout = TimeSpan.FromSeconds(Constants.RequestTimeoutSeconds)
                };

                // Transfer client for large streaming bodies.
                _transferHttpClient = new HttpClient(_socketsHandler, disposeHandler: false)
                {
                    Timeout = TransferTimeout
                };
                // if we already have one from a previous session.
                if (_hmacSecret != null)
                {
                    SetHmacSecret(_hmacSecret);
                }
            }
        }

        /// <summary>Set the HMAC secret received from the pairing response.</summary>
        public void SetHmacSecret(byte[]? secret)
        {
            var previous = Interlocked.Exchange(ref _hmacSecret, secret?.ToArray());
            if (previous != null) CryptographicOperations.ZeroMemory(previous);
        }

        /// <summary>Allow cert re-pinning only during an active pairing flow.</summary>
        public void SetAllowCertRePin(bool allow) => _allowCertRePinDuringPairing = allow;

        // ── HTTP operations ──────────────────────────────────────────────────────

        /// <summary>
        /// Send an HMAC-authenticated request and return the raw response body string.
        /// </summary>
        public async Task<string> SendAsync(
            HttpMethod method,
            string fullUrl,
            string signaturePath,
            string? body = null,
            CancellationToken cancellationToken = default)
        {
            var bodyHash = body != null ? ComputeSha256(body) : ComputeSha256(string.Empty);
            var isIdempotent = IsIdempotent(method);

            return await ExecuteWithRetryAsync(async () =>
            {
                var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture);
                var nonce = Guid.NewGuid().ToString();
                var signatureInput = $"{method.Method}\n{signaturePath}\n{timestamp}\n{nonce}\n{bodyHash}";
                var signature = ComputeHmac(signatureInput);

                using var request = new HttpRequestMessage(method, fullUrl);
                request.Headers.Add("X-Client-Id", _clientId);
                request.Headers.Add("X-Timestamp", timestamp);
                request.Headers.Add("X-Nonce", nonce);
                request.Headers.Authorization = new AuthenticationHeaderValue("HMAC", signature);
                if (body != null) request.Headers.Add("X-Content-SHA256", bodyHash);

                if (body != null)
                    request.Content = new StringContent(body, Encoding.UTF8, "application/json");

                HttpClient httpClient;
                lock (_httpClientLock)
                {
                    httpClient = _httpClient
                        ?? throw new InvalidOperationException("HttpTransport not initialised. Call EnsureInitialised() first.");
                }

                using var response = await httpClient
                    .SendAsync(request, cancellationToken).ConfigureAwait(false);
                var responseBody = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

                LogMessages.HttpRequestCompleted(Logger, method.Method, fullUrl, (int)response.StatusCode);

                if (!response.IsSuccessStatusCode)
                    HandleErrorResponse(response.StatusCode, responseBody ?? string.Empty);

                return responseBody ?? string.Empty;
            }, isIdempotent, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Send a raw (non-HMAC) request — used for the pairing handshake.
        /// </summary>
        public async Task<HttpResponseMessage> SendRawAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken = default)
        {
            HttpClient httpClient;
            lock (_httpClientLock)
            {
                httpClient = _httpClient
                    ?? throw new InvalidOperationException("HttpTransport not initialised.");
            }
            return await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Send an HMAC-authenticated request and return the raw <see cref="HttpResponseMessage"/>.
        /// Used for binary responses (e.g. contact photos) where the body is not JSON text.
        /// </summary>
        public async Task<HttpResponseMessage> SendRawAuthenticatedAsync(
            HttpMethod method,
            string fullUrl,
            string signaturePath,
            HttpContent? content = null,
            Action<HttpRequestMessage>? configureRequest = null,
            HttpCompletionOption completionOption = HttpCompletionOption.ResponseHeadersRead,
            CancellationToken cancellationToken = default)
        {
            byte[]? contentBytes = null;
            string bodyHash = ComputeSha256(Array.Empty<byte>());
            System.Net.Http.Headers.HttpContentHeaders? originalHeaders = null;

            if (content != null)
            {
                contentBytes = await content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
                bodyHash = ComputeSha256(contentBytes);
                originalHeaders = content.Headers;
            }

            var isIdempotent = IsIdempotent(method);
            return await ExecuteWithRetryAsync(async () =>
            {
                var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture);
                var nonce = Guid.NewGuid().ToString();
                var signatureInput = $"{method.Method}\n{signaturePath}\n{timestamp}\n{nonce}\n{bodyHash}";
                var signature = ComputeHmac(signatureInput);

                var request = new HttpRequestMessage(method, fullUrl);
                request.Headers.Add("X-Client-Id", _clientId);
                request.Headers.Add("X-Timestamp", timestamp);
                request.Headers.Add("X-Nonce", nonce);
                request.Headers.Authorization = new AuthenticationHeaderValue("HMAC", signature);

                if (contentBytes != null)
                {
                    var clonedContent = new ByteArrayContent(contentBytes);
                    if (originalHeaders != null)
                    {
                        foreach (var header in originalHeaders)
                            clonedContent.Headers.TryAddWithoutValidation(header.Key, header.Value);
                    }
                    request.Headers.Add("X-Content-SHA256", bodyHash);
                    request.Content = clonedContent;
                }

                configureRequest?.Invoke(request);

                HttpClient httpClient;
                lock (_httpClientLock)
                {
                    httpClient = _httpClient
                        ?? throw new InvalidOperationException("HttpTransport not initialised. Call EnsureInitialised() first.");
                }

                return await httpClient.SendAsync(request, completionOption, cancellationToken).ConfigureAwait(false);
            }, isIdempotent, cancellationToken).ConfigureAwait(false);
        }

        // ── Retry ────────────────────────────────────────────────────────────────

        private static async Task<T> ExecuteWithRetryAsync<T>(
            Func<Task<T>> operation,
            bool allowRetry,
            CancellationToken cancellationToken)
        {
            if (!allowRetry)
                return await operation().ConfigureAwait(false);

            Exception? lastException = null;
            var rng = Random.Shared;

            for (int attempt = 0; attempt < MaxRetryAttempts; attempt++)
            {
                try
                {
                    return await operation().ConfigureAwait(false);
                }
                catch (PhoneConnectionException pce) when (pce.IsRateLimited)
                {
                    // 429 — never retry at transport level.
                    throw;
                }
                catch (PhoneConnectionException)
                {
                    // Business errors — never retry.
                    throw;
                }
                catch (HttpRequestException ex)
                {
                    lastException = ex;
                }
                catch (IOException ex)
                {
                    lastException = ex;
                }
                catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
                {
                    // Treat non-user-initiated cancellations as transient (e.g. HttpClient timeout / socket abort).
                    lastException = ex;
                }
                catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
                {
                    // Treat timeouts as transient.
                    lastException = ex;
                }

                // Last attempt: break without sleeping.
                if (attempt == MaxRetryAttempts - 1)
                    break;

                // Exponential backoff with jitter.
                var exp = Math.Pow(2, attempt); // 1,2,4...
                var jitter = TimeSpan.FromMilliseconds(rng.Next(0, MaxJitterMs));
                var delay = TimeSpan.FromMilliseconds(BaseRetryDelay.TotalMilliseconds * exp) + jitter;

                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            }

            throw lastException ?? new HttpRequestException("HTTP request failed after retries.");
        }

        private static bool IsIdempotent(HttpMethod method) =>
            method == HttpMethod.Get ||
            method == HttpMethod.Head ||
            method == HttpMethod.Options;

        // ── Error handling ───────────────────────────────────────────────────────

        private static void HandleErrorResponse(System.Net.HttpStatusCode statusCode, string body)
        {
            Models.DTOs.ApiErrorDto? apiError = null;
            try { apiError = JsonSerializer.Deserialize<Models.DTOs.ApiErrorDto>(body, ErrorJsonOptions); }
            catch { /* ignore */ }

            switch (statusCode)
            {
                case System.Net.HttpStatusCode.Unauthorized:
                    throw new PhoneConnectionException(
                        apiError?.Message ?? Constants.SessionExpiredMessage, isSessionExpired: true);

                case System.Net.HttpStatusCode.Conflict:
                    throw new PhoneConnectionException(
                        apiError?.Message ?? "Contact was modified on the phone. Please refresh and try again.");

                case System.Net.HttpStatusCode.Forbidden:
                    if (apiError?.Error == "read_only")
                        throw new PhoneConnectionException(Constants.ReadOnlyContactMessage, isReadOnly: true);
                    throw new PhoneConnectionException(
                        Constants.WritePermissionDeniedMessage, isPermissionDenied: true);

                case System.Net.HttpStatusCode.TooManyRequests:
                    throw new PhoneConnectionException(
                        apiError?.Message ?? "Rate limit exceeded.", isRateLimited: true);

                default:
                    throw new PhoneConnectionException(
                        apiError?.Message ?? $"Server returned {(int)statusCode}");
            }
        }

        // ── HMAC / SHA-256 ───────────────────────────────────────────────────────

        private string ComputeHmac(string input)
        {
            using var hmac = new HMACSHA256(
                _hmacSecret ?? throw new InvalidOperationException("HMAC secret not set."));
            return Convert.ToBase64String(hmac.ComputeHash(Encoding.UTF8.GetBytes(input)));
        }

        private static string ComputeSha256(string input) =>
            ComputeSha256(Encoding.UTF8.GetBytes(input));

        private static string ComputeSha256(byte[] input) =>
            Convert.ToBase64String(SHA256.HashData(input));

        private static string? NormalizeFingerprint(string? fp) =>
            fp?.Replace(":", string.Empty).Replace(" ", string.Empty).ToUpperInvariant();

        // ── IDisposable ──────────────────────────────────────────────────────────

        public void Dispose()
        {
            var secret = Interlocked.Exchange(ref _hmacSecret, null);
            if (secret != null) CryptographicOperations.ZeroMemory(secret);
            lock (_httpClientLock)
            {
                _httpClient?.Dispose();
                _httpClient = null;

                _transferHttpClient?.Dispose();
                _transferHttpClient = null;

                _socketsHandler?.Dispose();
                _socketsHandler = null;
            }

            GC.SuppressFinalize(this);
        }
    }
}
