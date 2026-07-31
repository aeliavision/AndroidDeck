using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using VcfEditor.Helpers;
using VcfEditor.Core.Settings;
using VcfEditor.Models;
using VcfEditor.Models.DTOs;

namespace VcfEditor.Core
{
    /// <summary>
    /// Represents the state of the connection to the Android phone.
    /// </summary>
    public enum PhoneConnectionState
    {
        Disconnected,
        Pairing,
        Connected,
        Error
    }

    /// <summary>
    /// Exception thrown when a phone connection operation fails.
    /// </summary>
    public class PhoneConnectionException : Exception
    {
        public bool IsReadOnly { get; }
        public bool IsSessionExpired { get; }
        public bool IsPermissionDenied { get; }
        /// <summary>True when the server returned 429 Too Many Requests.</summary>
        public bool IsRateLimited { get; }

        public PhoneConnectionException(string message)
            : base(message) { }

        public PhoneConnectionException(string message, Exception innerException)
            : base(message, innerException) { }

        public PhoneConnectionException(string message, bool isReadOnly = false,
            bool isSessionExpired = false, bool isPermissionDenied = false,
            bool isRateLimited = false)
            : base(message)
        {
            IsReadOnly = isReadOnly;
            IsSessionExpired = isSessionExpired;
            IsPermissionDenied = isPermissionDenied;
            IsRateLimited = isRateLimited;
        }
    }

    /// <summary>
    /// HTTP client for communicating with the Android companion app.
    /// Handles pairing, HMAC authentication, and contact CRUD operations.
    /// modular <see cref="PhoneApiClient"/>. All business logic lives in:
    ///   <see cref="HttpTransport"/>   — raw HTTP, HMAC, TOFU cert pinning, retry
    ///   <see cref="SessionManager"/>  — pairing state, heartbeat
    ///   <see cref="ContactsApi"/>     — contact CRUD, batch fetch, groups, photos
    ///
    /// Existing callers that use PhoneContactsClient directly continue to compile
    /// and work unchanged. New code should depend on PhoneApiClient instead.
    /// </summary>
    public class PhoneContactsClient : INotifyPropertyChanged, IDisposable, IAsyncDisposable
    {
        private readonly PhoneApiClient _inner;

        public PhoneContactsClient(bool useHttps, IAppSettingsStore settingsStore)
        {
            _inner = new PhoneApiClient(useHttps, settingsStore);
            // Forward property changes from the inner client
            _inner.PropertyChanged += (s, e) => PropertyChanged?.Invoke(this, e);
        }


        public PhoneConnectionState State => _inner.State;
        public string? DeviceName => _inner.DeviceName;
        public string? ErrorMessage => _inner.ErrorMessage;
        public bool IsConnected => _inner.IsConnected;
        public string? LastCertFingerprint => _inner.LastCertFingerprint;
        public FileSystemApi FileSystem => _inner.FileSystem;
        public GalleryApi Gallery => _inner.Gallery;

        /// <summary>P3/P4: Exposes the underlying PhoneApiClient for views that need direct access.</summary>
        public PhoneApiClient InnerApiClient => _inner;

        public Task<bool> PairAsync(string host, int port, string pairingCode,
            CancellationToken cancellationToken = default)
            => _inner.PairAsync(host, port, pairingCode, cancellationToken);

        public void Disconnect() => _inner.Disconnect();

        public Task<DeviceStatusDto?> GetStatusAsync() => _inner.GetStatusAsync();

        public Task<List<Contact>> FetchContactsAsync(
            int page = 1, int pageSize = 50, string? query = null,
            CancellationToken cancellationToken = default)
            => _inner.Contacts.FetchContactsAsync(page, pageSize, query, cancellationToken);

        public Task<List<Contact>> FetchAllContactsAsync(
            IProgress<(int current, int total)>? progress = null,
            CancellationToken cancellationToken = default)
            => _inner.Contacts.FetchAllContactsAsync(progress, cancellationToken);

        public Task<List<Contact>> FetchContactDetailsInParallelAsync(
            IEnumerable<Contact> summaries, int maxConcurrency = 8,
            IProgress<(int current, int total)>? progress = null,
            CancellationToken cancellationToken = default)
            => _inner.Contacts.FetchContactsBatchAsync(
                summaries.Select(c => c.AndroidId!).Where(id => !string.IsNullOrEmpty(id)),
                maxConcurrency, progress, cancellationToken);

        public Task<Contact?> FetchContactDetailAsync(
            string androidId, CancellationToken cancellationToken = default)
            => _inner.Contacts.FetchContactDetailAsync(androidId, cancellationToken);

        public Task<Contact?> CreateContactAsync(Contact contact)
            => _inner.Contacts.CreateContactAsync(contact);

        public Task<Contact?> UpdateContactAsync(Contact contact)
            => _inner.Contacts.UpdateContactAsync(contact);

        public Task<bool> DeleteContactAsync(string androidId)
            => _inner.Contacts.DeleteContactAsync(androidId);

        public Task<byte[]?> FetchContactPhotoAsync(string androidId, CancellationToken cancellationToken = default)
            => _inner.Contacts.FetchContactPhotoAsync(androidId, cancellationToken);

        public Task UploadContactPhotoAsync(string androidId, byte[] photoBytes)
            => _inner.Contacts.UploadContactPhotoAsync(androidId, photoBytes);

        public Task<List<GroupDto>> FetchGroupsAsync()
            => _inner.Contacts.FetchGroupsAsync();

        public Task<List<Contact>> FetchContactsByGroupAsync(string groupId)
            => _inner.Contacts.FetchContactsByGroupAsync(groupId);

        public event PropertyChangedEventHandler? PropertyChanged;

        public void Dispose()
        {
            _inner.Dispose();
            GC.SuppressFinalize(this);
        }

        public async ValueTask DisposeAsync()
        {
            await _inner.DisposeAsync().ConfigureAwait(false);
            GC.SuppressFinalize(this);
        }
    }
}
