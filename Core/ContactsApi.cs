using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using VcfEditor.Helpers;
using VcfEditor.Core.IO;
using VcfEditor.Core.Paging;
using VcfEditor.Models;
using VcfEditor.Models.DTOs;

namespace VcfEditor.Core
{
    /// <summary>
    /// All contact CRUD operations against /api/v1/contacts/* and the new
    /// </summary>
    public sealed class ContactsApi
    {
        private static readonly ILogger Logger = AppLoggerFactory.CreateLogger(nameof(ContactsApi));

        private readonly HttpTransport _transport;
        private readonly SessionManager _session;
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true
        };

        private const string V1 = "/api/v1";
        private const string V2 = "/api/v2";

        public ContactsApi(HttpTransport transport, SessionManager session)
        {
            _transport = transport;
            _session = session;
        }

        // ── Read ─────────────────────────────────────────────────────────────────

        /// <summary>Fetch a single page of contact summaries.</summary>
        public async Task<List<Contact>> FetchContactsAsync(
            int page = 1, int pageSize = 50, string? query = null,
            CancellationToken cancellationToken = default)
        {
            _session.EnsureConnected();
            var qs = $"?page={page}&pageSize={pageSize}";
            if (!string.IsNullOrWhiteSpace(query))
                qs += $"&query={Uri.EscapeDataString(query)}";

            var path = $"/contacts{qs}";
            var json = await Send(HttpMethod.Get, V1, path, cancellationToken: cancellationToken).ConfigureAwait(false);
            var page_ = JsonSerializer.Deserialize<ContactsPageDto>(json, JsonOptions);
            return DtoMapper.ToContacts(page_?.Items);
        }

        /// <summary>
        /// Fetch ALL contact summaries, paging automatically.
        /// Includes a duplicate-page guard for providers that ignore OFFSET.
        /// </summary>
        public async Task<List<Contact>> FetchAllContactsAsync(
            IProgress<(int current, int total)>? progress = null,
            CancellationToken cancellationToken = default)
        {
            _session.EnsureConnected();
            LogMessages.ContactsFetchStarted(Logger);
            var pages = await PagedFetch.FetchAllAsync(
                async (page, token) =>
                {
                    var qs = $"?page={page}&pageSize=50";
                    var json = await Send(
                            HttpMethod.Get,
                            V1,
                            $"/contacts{qs}",
                            cancellationToken: token)
                        .ConfigureAwait(false);
                    return string.IsNullOrWhiteSpace(json)
                        ? new ContactsPageDto()
                        : JsonSerializer.Deserialize<ContactsPageDto>(json, JsonOptions)
                          ?? new ContactsPageDto();
                },
                pageDto => pageDto.Items,
                pageDto => pageDto.NextPage,
                item => item.Id,
                reportItemCount: count => progress?.Report((count, -1)),
                cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            var all = DtoMapper.ToContacts(pages);
            LogMessages.ContactsFetchCompleted(Logger, all.Count);
            return all;
        }

        /// <summary>Fetch full detail for a single contact.</summary>
        public async Task<Contact?> FetchContactDetailAsync(
            string androidId, CancellationToken cancellationToken = default)
        {
            _session.EnsureConnected();
            var json = await Send(HttpMethod.Get, V1, $"/contacts/{androidId}", cancellationToken: cancellationToken).ConfigureAwait(false);
            var dto = JsonSerializer.Deserialize<ContactDto>(json, JsonOptions);
            return DtoMapper.ToContact(dto);
        }

        /// <summary>
        /// Falls back to parallel individual fetches if the server does not support v2.
        /// </summary>
        public async Task<List<Contact>> FetchContactsBatchAsync(
            IEnumerable<string> androidIds,
            int maxConcurrency = 8,
            IProgress<(int current, int total)>? progress = null,
            CancellationToken cancellationToken = default)
        {
            _session.EnsureConnected();
            var ids = androidIds.ToList();

            // Try the v2 batch endpoint first.
            try
            {
                var body = JsonSerializer.Serialize(new { ids }, JsonOptions);
                var json = await Send(HttpMethod.Post, V2, "/contacts/batch", body, cancellationToken)
                    .ConfigureAwait(false);
                var dtos = JsonSerializer.Deserialize<List<ContactDto>>(json, JsonOptions);
                return DtoMapper.ToContacts(dtos);
            }
            catch (PhoneConnectionException ex) when (!ex.IsSessionExpired && !ex.IsPermissionDenied)
            {
                // Server returned 404 (v2 not yet deployed) — fall back to parallel v1 calls.
                LogMessages.ContactsBatchFallback(Logger);
            }

            // Fallback: parallel individual fetches with bounded concurrency.
            // No rate limiting on the server (pairing-only limit) so parallel is safe and fast.
            var total = ids.Count;
            var results = new Contact?[total];
            var completed = 0;
            var gate = new SemaphoreSlim(maxConcurrency, maxConcurrency);

            var tasks = ids.Select(async (id, index) =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
                try
                {
                    var detail = await FetchContactDetailAsync(id, cancellationToken).ConfigureAwait(false);
                    results[index] = detail;
                    var done = Interlocked.Increment(ref completed);
                    progress?.Report((done, total));
                }
                catch (OperationCanceledException) { }
                catch (Exception ex)
                {
                    LogMessages.ContactDetailFetchFailed(Logger, ex, id);
                }
                finally { gate.Release(); }
            });

            await Task.WhenAll(tasks).ConfigureAwait(false);
            gate.Dispose();

            return results.Where(c => c != null).Select(c => c!).ToList();
        }

        // ── Write ────────────────────────────────────────────────────────────────

        /// <summary>Create a new contact on the phone.</summary>
        public async Task<Contact?> CreateContactAsync(Contact contact, CancellationToken cancellationToken = default)
        {
            _session.EnsureConnected();
            var dto = DtoMapper.ToDto(contact);
            var body = JsonSerializer.Serialize(dto, JsonOptions);
            var json = await Send(HttpMethod.Post, V1, "/contacts", body, cancellationToken).ConfigureAwait(false);
            return DtoMapper.ToContact(JsonSerializer.Deserialize<ContactDto>(json, JsonOptions));
        }

        /// <summary>Update an existing contact on the phone.</summary>
        public async Task<Contact?> UpdateContactAsync(Contact contact, CancellationToken cancellationToken = default)
        {
            _session.EnsureConnected();
            if (string.IsNullOrEmpty(contact.AndroidId))
                throw new PhoneConnectionException("Contact has no Android ID. Cannot update.");
            var dto = DtoMapper.ToDto(contact);
            var body = JsonSerializer.Serialize(dto, JsonOptions);
            var json = await Send(HttpMethod.Put, V1, $"/contacts/{contact.AndroidId}", body, cancellationToken).ConfigureAwait(false);
            return DtoMapper.ToContact(JsonSerializer.Deserialize<ContactDto>(json, JsonOptions));
        }

        /// <summary>Delete a contact from the phone.</summary>
        public async Task<bool> DeleteContactAsync(string androidId, CancellationToken cancellationToken = default)
        {
            _session.EnsureConnected();
            if (string.IsNullOrEmpty(androidId))
                throw new PhoneConnectionException("No Android ID provided.");
            await Send(HttpMethod.Delete, V1, $"/contacts/{androidId}", cancellationToken: cancellationToken).ConfigureAwait(false);
            return true;
        }

        // ── Photos ───────────────────────────────────────────────────────────────

        /// <summary>Fetch the photo for a contact as raw JPEG bytes. Returns null if no photo.</summary>
        public async Task<byte[]?> FetchContactPhotoAsync(string androidId, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(androidId)) return null;
            _session.EnsureConnected();
            try
            {
                var fullUrl = _transport.BaseUrl + V1 + $"/contacts/{androidId}/photo";
                var signaturePath = V1 + $"/contacts/{androidId}/photo";

                // Photo endpoint returns binary — use HMAC-authenticated raw request.
                var response = await _transport.SendRawAuthenticatedAsync(
                    HttpMethod.Get, fullUrl, signaturePath, cancellationToken: cancellationToken).ConfigureAwait(false);
                if (response.StatusCode == System.Net.HttpStatusCode.NotFound) return null;

                if (!response.IsSuccessStatusCode)
                {
                    string? body = null;
                    try { body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false); } catch { /* ignore */ }
                    var preview = string.IsNullOrWhiteSpace(body)
                        ? string.Empty
                        : body[..Math.Min(300, body.Length)];
                    throw new PhoneConnectionException(
                        $"Failed to fetch contact photo (HTTP {(int)response.StatusCode}). {preview}");
                }

                BoundedStreamCopy.ValidateDeclaredLength(
                    response.Content.Headers.ContentLength,
                    TransferLimits.MaxContactPhotoBytes,
                    "contact photo");
                await using var photoStream = await response.Content
                    .ReadAsStreamAsync(cancellationToken)
                    .ConfigureAwait(false);
                return await BoundedStreamCopy.ReadAllBytesAsync(
                        photoStream,
                        TransferLimits.MaxContactPhotoBytes,
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (PhoneConnectionException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new PhoneConnectionException($"Failed to fetch contact photo: {ex.Message}", ex);
            }
        }

        /// <summary>Upload a JPEG photo for a contact.</summary>
        public async Task UploadContactPhotoAsync(
            string androidId,
            byte[] photoBytes,
            CancellationToken cancellationToken = default)
        {
            _session.EnsureConnected();
            if (string.IsNullOrEmpty(androidId))
                throw new PhoneConnectionException("No Android ID provided.");
            ArgumentNullException.ThrowIfNull(photoBytes);
            if (photoBytes.LongLength > TransferLimits.MaxContactPhotoBytes)
                throw new TransferLimitExceededException(
                    TransferLimits.MaxContactPhotoBytes,
                    photoBytes.LongLength);

            var base64 = Convert.ToBase64String(photoBytes);
            var body = JsonSerializer.Serialize(new { photo = base64 }, JsonOptions);
            await Send(
                    HttpMethod.Put,
                    V1,
                    $"/contacts/{androidId}/photo",
                    body,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        // ── Groups ───────────────────────────────────────────────────────────────

        /// <summary>Fetch all contact groups from the phone.</summary>
        public async Task<List<GroupDto>> FetchGroupsAsync()
        {
            _session.EnsureConnected();
            var json = await Send(HttpMethod.Get, V1, "/groups").ConfigureAwait(false);
            var page = JsonSerializer.Deserialize<GroupsPageDto>(json, JsonOptions);
            return page?.Items ?? new List<GroupDto>();
        }

        /// <summary>Fetch contacts belonging to a specific group.</summary>
        public async Task<List<Contact>> FetchContactsByGroupAsync(string groupId)
        {
            _session.EnsureConnected();
            var json = await Send(HttpMethod.Get, V1, $"/groups/{groupId}/contacts").ConfigureAwait(false);
            var dtos = JsonSerializer.Deserialize<List<ContactDto>>(json, JsonOptions);
            return DtoMapper.ToContacts(dtos);
        }

        // ── Private helpers ───────────────────────────────────────────────────────

        private Task<string> Send(
            HttpMethod method,
            string apiVersion,
            string path,
            string? body = null,
            CancellationToken cancellationToken = default)
        {
            // Strip query string for HMAC signature path
            var queryIdx = path.IndexOf('?');
            var signaturePath = apiVersion + (queryIdx != -1 ? path[..queryIdx] : path);
            var fullUrl = _transport.BaseUrl + apiVersion + path;
            return _transport.SendAsync(method, fullUrl, signaturePath, body, cancellationToken);
        }
    }
}
