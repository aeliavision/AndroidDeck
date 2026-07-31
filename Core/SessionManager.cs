using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using VcfEditor.Helpers;
using VcfEditor.Models.DTOs;

namespace VcfEditor.Core
{
    /// <summary>
    /// Owns: pairing state, session ID/secret/expiry, heartbeat timer, and
    /// connection-state property notifications.
    /// </summary>
    public sealed class SessionManager : IDisposable
    {
        private static readonly ILogger Logger = AppLoggerFactory.CreateLogger(nameof(SessionManager));

        // ── Constants ────────────────────────────────────────────────────────────

        private const int MaxHeartbeatFailures = 3;

        // ── Dependencies ─────────────────────────────────────────────────────────

        private readonly HttpTransport _transport;
        private readonly string _apiBasePath;

        // ── State ────────────────────────────────────────────────────────────────

        private string? _sessionId;
        private long _expiresAt;
        private Timer? _heartbeatTimer;
        private int _consecutiveHeartbeatFailures;
        private bool _disposed;
        private readonly SemaphoreSlim _heartbeatSemaphore = new(1, 1);

        // ── Events ───────────────────────────────────────────────────────────────

        /// <summary>Raised when the connection state changes.</summary>
        public event Action<PhoneConnectionState>? StateChanged;

        /// <summary>Raised when a non-recoverable connection error occurs.</summary>
        public event Action<string>? ErrorOccurred;

        // ── Properties ───────────────────────────────────────────────────────────

        public PhoneConnectionState State { get; private set; } = PhoneConnectionState.Disconnected;
        public string? SessionId => _sessionId;
        public bool IsConnected => State == PhoneConnectionState.Connected;
        public bool IsSessionValid => _sessionId != null && _transport.BaseUrl != null;

        public async Task<bool> TryRecoverAsync(CancellationToken cancellationToken = default)
        {
            if (!IsSessionValid) return false;

            try
            {
                var fullUrl = _transport.BaseUrl + _apiBasePath + "/status";
                var signaturePath = _apiBasePath + "/status";
                await _transport.SendAsync(System.Net.Http.HttpMethod.Get, fullUrl, signaturePath, cancellationToken: cancellationToken)
                    .ConfigureAwait(false);

                _consecutiveHeartbeatFailures = 0;
                SetState(PhoneConnectionState.Connected);
                StartHeartbeat();
                ErrorOccurred?.Invoke(string.Empty);
                return true;
            }
            catch (Exception ex)
            {
                LogMessages.SessionRecoveryFailed(Logger, ex);
                SetState(PhoneConnectionState.Error);
                ErrorOccurred?.Invoke(Constants.PhoneUnreachableMessage);
                return false;
            }
        }

        // ── Construction ─────────────────────────────────────────────────────────

        public SessionManager(HttpTransport transport, string apiBasePath = Constants.DefaultApiBasePath)
        {
            _transport = transport;
            _apiBasePath = apiBasePath;
        }

        // ── Pairing ──────────────────────────────────────────────────────────────

        /// <summary>
        /// Apply the result of a successful pairing response to establish the session.
        /// </summary>
        public void ApplyPairResponse(PairResponseDto response)
        {
            _sessionId = response.SessionId;
            var secret = response.HmacSecret != null
                ? Convert.FromBase64String(response.HmacSecret)
                : null;
            _transport.SetHmacSecret(secret);
            _expiresAt = response.ExpiresAt;

            SetState(PhoneConnectionState.Connected);
            StartHeartbeat();
        }

        public void ApplyPairResponseV3(PairResponseV3Dto response, byte[] derivedSecret)
        {
            ArgumentNullException.ThrowIfNull(response);
            ArgumentNullException.ThrowIfNull(derivedSecret);
            if (string.IsNullOrWhiteSpace(response.SessionId))
                throw new ArgumentException("Pairing response did not include a session ID.", nameof(response));

            _sessionId = response.SessionId;
            _transport.SetHmacSecret(derivedSecret);
            _expiresAt = response.ExpiresAt;
            SetState(PhoneConnectionState.Connected);
            StartHeartbeat();
        }

        /// <summary>Tear down the session and stop the heartbeat.</summary>
        public void ClearSession()
        {
            StopHeartbeat();
            _sessionId = null;
            _transport.SetHmacSecret(null);
            _expiresAt = 0;
            SetState(PhoneConnectionState.Disconnected);
        }

        // ── State helpers ────────────────────────────────────────────────────────

        public void SetState(PhoneConnectionState state)
        {
            State = state;
            StateChanged?.Invoke(state);
        }

        public void EnsureConnected()
        {
            if (State != PhoneConnectionState.Connected)
                throw new PhoneConnectionException("Not connected to a phone.");
            if (_sessionId == null)
                throw new PhoneConnectionException(Constants.SessionExpiredMessage, isSessionExpired: true);
        }

        // ── Heartbeat ────────────────────────────────────────────────────────────

        public void StartHeartbeat()
        {
            StopHeartbeat();
            _consecutiveHeartbeatFailures = 0;
            _heartbeatTimer = new Timer(
                HeartbeatCallback,
                null,
                TimeSpan.FromSeconds(Constants.HeartbeatIntervalSeconds),
                TimeSpan.FromSeconds(Constants.HeartbeatIntervalSeconds));
        }

        public void StopHeartbeat()
        {
            _heartbeatTimer?.Dispose();
            _heartbeatTimer = null;
        }

        private async void HeartbeatCallback(object? state)
        {
            if (_disposed) return;

            // BUG FIX: Prevent overlapping heartbeats on slow networks.
            // If the previous heartbeat is still active, skip this cycle.
            if (!await _heartbeatSemaphore.WaitAsync(0).ConfigureAwait(false))
            {
                LogMessages.HeartbeatSkipped(Logger);
                return;
            }

            try
            {
                var fullUrl = _transport.BaseUrl + _apiBasePath + "/status";
                var signaturePath = _apiBasePath + "/status";
                await _transport.SendAsync(System.Net.Http.HttpMethod.Get, fullUrl, signaturePath)
                    .ConfigureAwait(false);

                if (_consecutiveHeartbeatFailures > 0)
                {
                    _consecutiveHeartbeatFailures = 0;
                    if (State == PhoneConnectionState.Error && IsSessionValid)
                    {
                        SetState(PhoneConnectionState.Connected);
                        ErrorOccurred?.Invoke(string.Empty); // clear error
                    }
                }
            }
            catch
            {
                if (_disposed) return;
                _consecutiveHeartbeatFailures++;

                if (_consecutiveHeartbeatFailures < MaxHeartbeatFailures)
                {
                    LogMessages.HeartbeatTransientFailure(Logger, _consecutiveHeartbeatFailures, MaxHeartbeatFailures);
                    return;
                }

                SetState(PhoneConnectionState.Error);
                ErrorOccurred?.Invoke(Constants.PhoneUnreachableMessage);
                _ = System.Threading.Tasks.Task.Run(() => StopHeartbeat());
            }
            finally
            {
                _heartbeatSemaphore.Release();
            }
        }

        // ── IDisposable ──────────────────────────────────────────────────────────

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            StopHeartbeat();
            GC.SuppressFinalize(this);
        }
    }
}
