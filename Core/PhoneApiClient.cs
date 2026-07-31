using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Text;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using VcfEditor.Helpers;
using VcfEditor.Core.Settings;
using VcfEditor.Core.Security;
using VcfEditor.Models;
using VcfEditor.Models.DTOs;

namespace VcfEditor.Core
{
    /// <summary>
    ///
    /// Architecture:
    ///   PhoneApiClient (this façade — owns lifecycle, INotifyPropertyChanged, IDisposable)
    ///   ├── HttpTransport   — raw HTTP, HMAC signing, TOFU cert pinning, retry
    ///   ├── SessionManager  — pairing state, session lifecycle, heartbeat
    ///   └── ContactsApi     — contact CRUD, batch fetch, groups, photos
    ///
    /// PhoneContactsClient is kept as a thin compatibility shim (see bottom of file) so
    /// existing callers use the compatibility shim with an explicitly supplied settings store.
    /// </summary>
    public sealed class PhoneApiClient : INotifyPropertyChanged, IDisposable, IAsyncDisposable
    {
        private static readonly ILogger Logger = AppLoggerFactory.CreateLogger(nameof(PhoneApiClient));

        // ── Sub-modules ──────────────────────────────────────────────────────────

        private readonly HttpTransport _transport;
        private readonly SessionManager _session;

        /// <summary>Contact CRUD, batch fetch, groups, photos.</summary>
        public ContactsApi Contacts { get; }
        public FileSystemApi FileSystem { get; }
        public GalleryApi Gallery { get; }

        /// <summary>P5 — Exposes the underlying transport so BackupApi can share the same
        /// HMAC-signed HTTP connection without requiring its own pairing flow.</summary>
        public HttpTransport Transport => _transport;

        // ── State ────────────────────────────────────────────────────────────────

        private readonly string _clientId;
        private readonly SemaphoreSlim _pairLock = new(1, 1);
        private readonly WeakReference<SynchronizationContext>? _syncContextRef;
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true
        };
        private bool _disposed;

        private PhoneConnectionState _state = PhoneConnectionState.Disconnected;
        private string? _deviceName;
        private string? _errorMessage;

        // ── Properties ───────────────────────────────────────────────────────────

        public PhoneConnectionState State
        {
            get => _state;
            private set { _state = value; OnPropertyChanged(nameof(State)); OnPropertyChanged(nameof(IsConnected)); }
        }

        public string? DeviceName
        {
            get => _deviceName;
            private set { _deviceName = value; OnPropertyChanged(nameof(DeviceName)); }
        }

        public string? ErrorMessage
        {
            get => _errorMessage;
            private set { _errorMessage = value; OnPropertyChanged(nameof(ErrorMessage)); }
        }

        public bool IsConnected => State == PhoneConnectionState.Connected;

        /// <summary>
        /// SHA-256, colon-separated hex (e.g. "AA:BB:CC:…"). Null if server used v1 pairing
        /// or the fingerprint was not returned. Shown to the user for out-of-band verification.
        /// </summary>
        public string? LastCertFingerprint { get; private set; }

        // ── Construction ─────────────────────────────────────────────────────────

        public PhoneApiClient(IAppSettingsStore settingsStore) : this(useHttps: false, settingsStore) { }

        public PhoneApiClient(bool useHttps, IAppSettingsStore settingsStore)
        {
            ArgumentNullException.ThrowIfNull(settingsStore);
            _clientId = Guid.NewGuid().ToString();

            var ctx = SynchronizationContext.Current;
            _syncContextRef = ctx != null ? new WeakReference<SynchronizationContext>(ctx) : null;

            _transport = new HttpTransport(_clientId, settingsStore, useHttps);
            _session = new SessionManager(_transport);
            Contacts = new ContactsApi(_transport, _session);
            FileSystem = new FileSystemApi(_transport, _session);
            Gallery = new GalleryApi(_transport, _session);

            // Wire session events back to our observable properties
            _session.StateChanged += s => State = s;
            _session.ErrorOccurred += msg =>
            {
                if (!string.IsNullOrEmpty(msg))
                    ErrorMessage = msg;
            };
        }


        // ── Pairing & Connection ─────────────────────────────────────────────────

        /// <summary>
        /// Pair with the Android companion app using IP/port and a pairing code.
        /// Replaces PhoneContactsClient.PairAsync().
        /// </summary>
        public async Task<bool> PairAsync(
            string host, int port, string pairingCode,
            CancellationToken cancellationToken = default)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            _transport.EnsureInitialised(host, port);

            await _pairLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                State = PhoneConnectionState.Pairing;
                ErrorMessage = null;

                var baseUrl = _transport.BaseUrl!;
                var pairUrl = $"{baseUrl}/api/v3/pair";

                using var keyExchange = new PairingKeyExchange();
                var request = new PairRequestV3Dto
                {
                    PairingCode = pairingCode,
                    ClientId = _clientId,
                    ClientPublicKey = keyExchange.PublicKeyBase64
                };
                var body = JsonSerializer.Serialize(request, JsonOptions);
                var content = new System.Net.Http.StringContent(body, Encoding.UTF8, "application/json");

                _transport.SetAllowCertRePin(true);
                var rawReq = new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Post, pairUrl)
                {
                    Content = content
                };

                using var response = await _transport.SendRawAsync(rawReq, cancellationToken).ConfigureAwait(false);
                var responseBody = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                LogMessages.PairingResponseReceived(Logger, response.StatusCode);

                if (!response.IsSuccessStatusCode)
                {
                    ApiErrorDto? err = null;
                    try { err = JsonSerializer.Deserialize<ApiErrorDto>(responseBody, JsonOptions); } catch { }
                    State = PhoneConnectionState.Error;
                    ErrorMessage = err?.Message ?? Constants.PairingFailedMessage;
                    return false;
                }

                var pairResp = JsonSerializer.Deserialize<PairResponseV3Dto>(responseBody, JsonOptions);
                if (pairResp == null || string.IsNullOrWhiteSpace(pairResp.ServerPublicKey))
                {
                    State = PhoneConnectionState.Error;
                    ErrorMessage = "Invalid pairing response from phone.";
                    return false;
                }

                // On HTTP (local, no TLS), there is no transport-layer fingerprint to verify;
                // skip cert pinning entirely. On HTTPS the transport fingerprint must match
                // what the server returns in the JSON body (out-of-band TOFU verification).
                var transportFingerprint = NormalizeFingerprint(_transport.LastServerCertificateFingerprint);
                var responseFingerprint  = NormalizeFingerprint(pairResp.CertFingerprint);
                if (!string.IsNullOrWhiteSpace(transportFingerprint))
                {
                    // HTTPS path — fingerprints must match in constant time.
                    var tfBytes = Encoding.ASCII.GetBytes(transportFingerprint);
                    var rfBytes = Encoding.ASCII.GetBytes(responseFingerprint ?? string.Empty);
                    if (tfBytes.Length != rfBytes.Length ||
                        !CryptographicOperations.FixedTimeEquals(tfBytes, rfBytes))
                    {
                        State = PhoneConnectionState.Error;
                        ErrorMessage = "The phone identity could not be verified.";
                        return false;
                    }
                }
                // HTTP path — no TLS cert to pin; trust the ECDH-derived session secret instead.

                var derivedSecret = keyExchange.DeriveSecret(pairResp.ServerPublicKey, pairResp.SessionId);
                try
                {
                    _session.ApplyPairResponseV3(pairResp, derivedSecret);
                }
                finally
                {
                    CryptographicOperations.ZeroMemory(derivedSecret);
                }
                LastCertFingerprint = pairResp.CertFingerprint;
                // State is now Connected (set by SessionManager via event)

                try
                {
                    var statusJson = await _transport.SendAsync(
                        System.Net.Http.HttpMethod.Get,
                        $"{baseUrl}{Constants.DefaultApiBasePath}/status",
                        $"{Constants.DefaultApiBasePath}/status",
                        cancellationToken: cancellationToken).ConfigureAwait(false);
                    var status = JsonSerializer.Deserialize<DeviceStatusDto>(statusJson, JsonOptions);
                    DeviceName = status?.DeviceName ?? "Android Device";
                }
                catch (Exception ex)
                {
                    LogMessages.PairingStatusCheckFailed(Logger, ex);
                    DeviceName = "Android Device";
                }

                return true;
            }
            catch (System.Net.Http.HttpRequestException ex)
            {
                State = PhoneConnectionState.Error;
                ErrorMessage = $"Connection failed: {ex.InnerException?.Message ?? ex.Message}";
                return false;
            }
            catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
            {
                State = PhoneConnectionState.Error;
                ErrorMessage = $"{Constants.PhoneUnreachableMessage} (timeout: {ex.Message})";
                return false;
            }
            catch (TaskCanceledException)
            {
                // User-initiated cancel: don't report as an error.
                State = PhoneConnectionState.Disconnected;
                ErrorMessage = null;
                return false;
            }
            catch (Exception ex)
            {
                State = PhoneConnectionState.Error;
                ErrorMessage = ex.Message;
                return false;
            }
            finally
            {
                _transport.SetAllowCertRePin(false);
                _pairLock.Release();
            }
        }

        /// <summary>Disconnect from the phone and clear the session.</summary>
        public void Disconnect()
        {
            _session.ClearSession();
            DeviceName = null;
            ErrorMessage = null;
        }

        public Task<bool> TryRecoverAsync(CancellationToken cancellationToken = default)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _session.TryRecoverAsync(cancellationToken);
        }

        /// <summary>Fetch device status — also used by the heartbeat.</summary>
        public async Task<DeviceStatusDto?> GetStatusAsync()
        {
            _session.EnsureConnected();
            var baseUrl = _transport.BaseUrl!;
            var json = await _transport.SendAsync(
                System.Net.Http.HttpMethod.Get,
                $"{baseUrl}{Constants.DefaultApiBasePath}/status",
                $"{Constants.DefaultApiBasePath}/status").ConfigureAwait(false);
            return JsonSerializer.Deserialize<DeviceStatusDto>(json, JsonOptions);
        }

        // ── INotifyPropertyChanged ────────────────────────────────────────────────

        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged(string name)
        {
            var handler = PropertyChanged;
            if (handler == null) return;
            var args = new PropertyChangedEventArgs(name);
            if (_syncContextRef != null &&
                _syncContextRef.TryGetTarget(out var ctx) &&
                SynchronizationContext.Current != ctx)
            {
                ctx.Post(_ => handler(this, args), null);
                return;
            }
            handler(this, args);
        }

        // ── IDisposable / IAsyncDisposable ───────────────────────────────────────

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _session.Dispose();
            _transport.Dispose();
            _pairLock.Dispose();
            GC.SuppressFinalize(this);
        }

        public async ValueTask DisposeAsync()
        {
            if (_disposed) return;
            _disposed = true;
            _session.Dispose();
            if (_pairLock.CurrentCount == 0)
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                try { await _pairLock.WaitAsync(cts.Token).ConfigureAwait(false); }
                catch (OperationCanceledException) { }
            }
            _transport.Dispose();
            _pairLock.Dispose();
            GC.SuppressFinalize(this);
        }
        private static string? NormalizeFingerprint(string? value) =>
            value?.Replace(":", string.Empty, StringComparison.Ordinal)
                  .Replace(" ", string.Empty, StringComparison.Ordinal)
                  .ToUpperInvariant();

    }
}
