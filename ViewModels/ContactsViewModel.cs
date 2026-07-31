using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Data;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using VcfEditor.Core;
using VcfEditor.Features.Contacts;
using VcfEditor.Helpers;
using VcfEditor.Models;
using VcfEditor.Services;

namespace VcfEditor.ViewModels
{
    /// <summary>
    /// ViewModel for the contacts list view. Owns all state that was previously
    /// scattered across ContactsView.xaml.cs private fields and event handlers.
    /// </summary>
    public partial class ContactsViewModel : ObservableObject, IContactMetrics, IDisposable
    {
        private readonly IContactFileWorkflow _contactFileWorkflow;
        private readonly ILogger<ContactsViewModel> _logger;

        [ObservableProperty]
        private BulkObservableCollection<Contact> _contacts = new();

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(HasContacts))]
        [NotifyCanExecuteChangedFor(nameof(SaveFileCommand))]
        private int _contactCount;

        [ObservableProperty]
        private Contact? _selectedContact;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(HasSearchText))]
        [NotifyPropertyChangedFor(nameof(HasNoSearchResults))]
        private string _searchText = string.Empty;

        [ObservableProperty]
        private bool _isBusy;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(HasNoSource))]
        [NotifyPropertyChangedFor(nameof(HasNoContacts))]
        [NotifyPropertyChangedFor(nameof(HasNoSearchResults))]
        private bool _isSourceLoaded;

        [ObservableProperty]
        private string _statusMessage = string.Empty;

        [ObservableProperty]
        private string? _currentFilePath;

        [ObservableProperty]
        private bool _isDirty;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(SourceStatusText))]
        private ContactSource _activeSource = ContactSource.LocalVcf;

        [ObservableProperty]
        private PhoneContactsClient? _phoneClient;

        // --- Collection view for filtering/sorting (stays here, not in code-behind) ---
        public ICollectionView? ContactsView { get; private set; }

        // --- Computed ---
        public bool HasContacts => ContactCount > 0;
        public bool HasSearchText => !string.IsNullOrWhiteSpace(SearchText);
        public bool HasNoSource => !IsSourceLoaded && ActiveSource == ContactSource.LocalVcf;
        public bool HasNoContacts => IsSourceLoaded && ContactCount == 0 && !HasSearchText;
        public bool HasNoSearchResults => IsSourceLoaded && HasSearchText && VisibleContactCount == 0;
        public string SourceStatusText => ActiveSource == ContactSource.AndroidPhone
            ? "Live Android contacts"
            : string.IsNullOrWhiteSpace(CurrentFilePath)
                ? "No VCF file open"
                : System.IO.Path.GetFileName(CurrentFilePath);

        private int _visibleContactCount;
        public int VisibleContactCount
        {
            get => _visibleContactCount;
            private set
            {
                if (!SetProperty(ref _visibleContactCount, value)) return;
                OnPropertyChanged(nameof(HasNoSearchResults));
            }
        }
        // so it survives navigation, is testable, and is not hidden in UI event handlers.
        private string _sortColumn = "FullName";
        private ListSortDirection _sortDirection = ListSortDirection.Ascending;
        // now owned by the ViewModel so the filter predicate can use it without touching
        // any UI elements. The code-behind binds SearchFilterIndex to this property.
        [ObservableProperty]
        private int _searchFilterIndex;

        partial void OnSearchFilterIndexChanged(int value) => DebouncedRefreshFilter();

        // --- CancellationTokenSource for background phone fetches ---
        private CancellationTokenSource? _fetchCts;
        private long _phoneFetchGeneration;
        private bool _disposed;
        // filter pass on every keystroke.
        private DispatcherTimer? _filterDebounceTimer;
        private string _normalizedSearchText = string.Empty;

        public ContactsViewModel(
            VcfParser parser,
            ILogger<ContactsViewModel>? logger = null)
            : this(new ContactFileWorkflow(parser), logger)
        {
        }

        public ContactsViewModel(
            IContactFileWorkflow contactFileWorkflow,
            ILogger<ContactsViewModel>? logger = null)
        {
            ArgumentNullException.ThrowIfNull(contactFileWorkflow);
            _contactFileWorkflow = contactFileWorkflow;
            _logger = logger ?? AppLoggerFactory.CreateLogger<ContactsViewModel>();

            // Initialise the collection view for filtering
            ContactsView = CollectionViewSource.GetDefaultView(Contacts);
            ContactsView.Filter = FilterContact;

            _filterDebounceTimer = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromMilliseconds(250)
            };
            _filterDebounceTimer.Tick += (_, _) =>
            {
                _filterDebounceTimer.Stop();
                ContactsView?.Refresh();
                UpdateVisibleContactCount();
            };

            // Keep ContactCount in sync with the collection
            Contacts.CollectionChanged += (_, e) =>
            {
                ContactCount = Contacts.Count;
                UpdateVisibleContactCount();
                // Only mark dirty when the user actually modifies the set.
                if (e.Action != System.Collections.Specialized.NotifyCollectionChangedAction.Reset)
                    IsDirty = true;
            };
        }

        /// <summary>
        ///
        /// IMPORTANT: We must NOT replace the Contacts ObservableCollection reference itself.
        /// WPF binds directly to the collection instance — swapping the reference breaks the
        /// ICollectionView (which is keyed to the original instance by CollectionViewSource)
        /// and causes the DataGrid/ListView to stop receiving CollectionChanged notifications.
        ///
        /// Instead: temporarily unhook CollectionChanged, call Clear() + Add() in a tight
        /// loop, then re-raise a single Reset notification. This gives the ItemsControl one
        /// layout pass while keeping the original collection reference intact.
        /// </summary>
        private void ReplaceAllContacts(IEnumerable<Contact> newContacts, bool isDirty = false)
        {
            // NOTE: Must be called on the UI thread.
            // PERF: BulkObservableCollection.ReplaceAll fires exactly ONE Reset notification
            // → WPF gets one layout pass for all N items instead of N individual passes.
            if (ContactsView != null)
                ContactsView.Filter = null;   // Disconnect filter during reset to avoid N filter evaluations

            Contacts.ReplaceAll(newContacts);

            // Restore filter — one Refresh() = one filter pass for all items.
            if (ContactsView != null)
            {
                ContactsView.Filter = FilterContact;
                ContactsView.Refresh();
            }

            ContactCount = Contacts.Count;
            UpdateVisibleContactCount();
            IsDirty = isDirty;
        }
        // Commands

        /// <summary>Load a VCF file from disk into the Contacts collection.</summary>
        [RelayCommand]
        public async Task LoadFileAsync(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath)) return;

            IsBusy = true;
            StatusMessage = $"Loading {System.IO.Path.GetFileName(filePath)}…";
            try
            {
                // method in Task.Run. The old ParseVcfFile() used Task.Run+GetResult() internally
                // which deadlocked on the WPF UI thread (DispatcherSynchronizationContext).
                // ParseVcfFileAsync uses ReadLineAsync with ConfigureAwait(false) and is safe
                // to await directly from any context.
                var loaded = await _contactFileWorkflow.LoadAsync(filePath);

                // No longer need to explicitly marshal to UI thread because we didn't use ConfigureAwait(false)
                // However, ReplaceAllContacts expects an IEnumerable, so we can just call it
                ReplaceAllContacts(loaded, isDirty: false);

                CurrentFilePath = filePath;
                ContactCount = Contacts.Count;
                StatusMessage = $"Loaded {ContactCount} contact(s) from {System.IO.Path.GetFileName(filePath)}.";
                IsSourceLoaded = true;
                OnPropertyChanged(nameof(SourceStatusText));
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error loading file: {ex.Message}";
                LogMessages.LoadVcfFailed(_logger, ex, filePath);
            }
            finally
            {
                IsBusy = false;
            }
        }

        /// <summary>Save the current Contacts collection back to the VCF file.</summary>
        [RelayCommand(CanExecute = nameof(CanSaveFile))]
        public async Task SaveFileAsync()
        {
            if (string.IsNullOrWhiteSpace(CurrentFilePath)) return;

            IsBusy = true;
            StatusMessage = "Saving…";
            try
            {
                var targetPath = CurrentFilePath;
                await _contactFileWorkflow.SaveAsync(targetPath, Contacts);

                IsDirty = false;
                StatusMessage = $"Saved {ContactCount} contact(s).";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error saving file: {ex.Message}";
                LogMessages.SaveVcfFailed(_logger, ex, CurrentFilePath);
            }
            finally
            {
                IsBusy = false;
            }
        }

        private bool CanSaveFile() => HasContacts && !string.IsNullOrWhiteSpace(CurrentFilePath);

        /// <summary>Add a new blank contact to the collection.</summary>
        [RelayCommand]
        public void AddContact(Contact contact)
        {
            ExecuteOnUI(() =>
            {
                Contacts.Insert(0, contact);
                SelectedContact = contact;
            });
        }

        /// <summary>Delete a contact from the collection.</summary>
        [RelayCommand]
        public void DeleteContact(Contact contact)
        {
            ExecuteOnUI(() =>
            {
                Contacts.Remove(contact);
                if (SelectedContact == contact)
                    SelectedContact = Contacts.FirstOrDefault();
            });
        }

        /// <summary>Delete multiple contacts from the collection.</summary>
        [RelayCommand]
        public void DeleteContacts(System.Collections.Generic.IEnumerable<Contact> contactsToDelete)
        {
            ExecuteOnUI(() =>
            {
                foreach (var c in contactsToDelete.ToList())
                    Contacts.Remove(c);
                SelectedContact = Contacts.FirstOrDefault();
            });
        }

        private static void ExecuteOnUI(Action action)
        {
            var dispatcher = System.Windows.Application.Current?.Dispatcher
                ?? System.Windows.Threading.Dispatcher.CurrentDispatcher;

            if (dispatcher.CheckAccess())
                action();
            else
                dispatcher.Invoke(action);
        }

        /// <summary>Apply search text and refresh the collection view filter.</summary>
        partial void OnSearchTextChanged(string value)
        {
            _normalizedSearchText = TextNormalizer.Normalize(value ?? string.Empty);
            DebouncedRefreshFilter();
        }

        private void DebouncedRefreshFilter()
        {
            if (ContactsView == null) return;
            var dispatcher = System.Windows.Application.Current?.Dispatcher
                ?? Dispatcher.CurrentDispatcher;

            void ArmTimer()
            {
                if (_filterDebounceTimer == null) return;
                _filterDebounceTimer.Stop();
                _filterDebounceTimer.Start();
            }

            if (dispatcher.CheckAccess())
            {
                ArmTimer();
                return;
            }

            dispatcher.BeginInvoke((Action)ArmTimer, DispatcherPriority.Background);
        }
        // Phone / Android integration

        /// <summary>
        /// Raised when a phone operation fails with a user-visible message.
        /// The view forwards this through the themed dialog service while business logic
        /// remains independent from WPF window types.
        /// </summary>
        public event Action<string, string>? PhoneErrorOccurred; // (title, message)

        /// <summary>Connect a phone client and start fetching contacts from it.</summary>
        public async Task ConnectPhoneAsync(PhoneContactsClient client)
        {
            PhoneClient = client;
            ActiveSource = ContactSource.AndroidPhone;

            await RefreshFromPhoneAsync();
        }

        /// <summary>
        /// Moved from ContactsView.xaml.cs RefreshFromPhone() into the ViewModel
        /// so the logic is testable and not tied to the WPF dispatcher.
        /// </summary>
        public async Task RefreshFromPhoneAsync()
        {
            var client = PhoneClient;
            if (client == null) return;

            var nextCts = new CancellationTokenSource();
            var previousCts = Interlocked.Exchange(ref _fetchCts, nextCts);
            previousCts?.Cancel();

            var token = nextCts.Token;
            var generation = Interlocked.Increment(ref _phoneFetchGeneration);
            var dispatcher = System.Windows.Application.Current?.Dispatcher
                ?? System.Windows.Threading.Dispatcher.CurrentDispatcher;

            IsBusy = true;
            StatusMessage = "Fetching contacts from phone...";

            List<Contact> allContacts;
            try
            {
                allContacts = await client.FetchAllContactsAsync(
                    new Progress<(int current, int total)>(p =>
                    {
                        if (!IsCurrentPhoneFetch(client, generation, token)) return;
                        _ = dispatcher.BeginInvoke(() =>
                        {
                            if (IsCurrentPhoneFetch(client, generation, token))
                                StatusMessage = $"Fetching... {p.current} contacts";
                        });
                    }),
                    token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                await FinishPhoneFetchAsync(dispatcher, client, generation).ConfigureAwait(false);
                ReleaseFetchCancellation(nextCts);
                return;
            }
            catch (PhoneConnectionException ex)
            {
                if (IsCurrentPhoneFetch(client, generation, token))
                {
                    _ = dispatcher.BeginInvoke(() =>
                    {
                        if (!IsCurrentPhoneFetch(client, generation, token)) return;
                        IsBusy = false;
                        StatusMessage = $"Phone error: {ex.Message}";
                        RaisePhoneError("Connection Error", ex.Message);
                        if (ex.IsSessionExpired) DisconnectPhone();
                    });
                }
                ReleaseFetchCancellation(nextCts);
                return;
            }
            catch (Exception ex)
            {
                if (IsCurrentPhoneFetch(client, generation, token))
                {
                    _ = dispatcher.BeginInvoke(() =>
                    {
                        if (!IsCurrentPhoneFetch(client, generation, token)) return;
                        IsBusy = false;
                        StatusMessage = $"Error: {ex.Message}";
                    });
                }
                LogMessages.RefreshContactsFailed(_logger, ex);
                ReleaseFetchCancellation(nextCts);
                return;
            }

            if (!IsCurrentPhoneFetch(client, generation, token))
            {
                ReleaseFetchCancellation(nextCts);
                return;
            }

            await dispatcher.InvokeAsync(() =>
            {
                if (!IsCurrentPhoneFetch(client, generation, token)) return;
                ReplaceAllContacts(allContacts, isDirty: false);
                StatusMessage = $"Loaded {ContactCount} contact(s) from phone.";
                IsSourceLoaded = true;
                IsBusy = false;
            });

            await FetchMissingDetailsAsync(client, generation, token).ConfigureAwait(false);
            ReleaseFetchCancellation(nextCts);
        }

        /// <summary>
        /// Moved from ContactsView.xaml.cs AddPhoneContact().
        /// </summary>
        public async Task AddPhoneContactAsync(Contact contact)
        {
            if (PhoneClient == null) return;
            IsBusy = true;
            StatusMessage = "Creating contact on phone…";
            try
            {
                var created = await PhoneClient.CreateContactAsync(contact);
                if (created != null) ExecuteOnUI(() => Contacts.Insert(0, created));
                StatusMessage = "Contact created on phone.";
            }
            catch (Exception ex)
            {
                HandlePhoneError(ex, "create contact");
            }
            finally { IsBusy = false; }
        }

        /// <summary>
        /// Moved from ContactsView.xaml.cs EditPhoneContact().
        /// </summary>
        public async Task UpdatePhoneContactAsync(Contact contact)
        {
            if (PhoneClient == null || contact.IsReadOnly) return;
            IsBusy = true;
            StatusMessage = $"Updating {contact.FullName} on phone\u2026";

            // Increment the generation counter HERE — synchronously on the UI thread,
            // BEFORE the first await below yields control back to the dispatcher.
            // Any FetchMissingDetailsAsync BeginInvoke queued during the initial load
            // will check IsCurrentPhoneFetch() when it eventually fires and see a
            // mismatched generation, so it exits without touching the collection.
            // Moving this inside ExecuteOnUI (after the await) was too late: the
            // BeginInvoke could fire during the network wait and call ReplaceAll(),
            // creating duplicate rows and orphaned references.
            Interlocked.Increment(ref _phoneFetchGeneration);

            try
            {
                var updated = await PhoneClient.UpdateContactAsync(contact);
                ExecuteOnUI(() =>
                {
                    if (updated != null)
                    {
                        // Apply server-side normalisations (etag, canonical name casing)
                        // back onto the same object already in the collection.
                        contact.UpdateFrom(updated);
                    }
                    // Re-sort / re-filter so the contact moves to its correct position.
                    RefreshView();
                });
                StatusMessage = "Contact updated on phone.";
            }
            catch (Exception ex)
            {
                HandlePhoneError(ex, "update contact");
            }
            finally { IsBusy = false; }
        }

        /// <summary>
        /// Moved from ContactsView.xaml.cs DeletePhoneContacts().
        /// </summary>
        public async Task DeletePhoneContactsAsync(IEnumerable<Contact> contactsToDelete)
        {
            if (PhoneClient == null) return;
            IsBusy = true;
            StatusMessage = "Deleting contacts from phone…";
            int deletedCount = 0;
            try
            {
                var failed = new List<string>();
                foreach (var contact in contactsToDelete.ToList())
                {
                    if (string.IsNullOrEmpty(contact.AndroidId)) continue;
                    if (contact.IsReadOnly)
                    {
                        RaisePhoneError("Read-Only Contact",
                            "This contact is managed by another app and cannot be deleted.");
                        continue;
                    }
                    try
                    {
                        await PhoneClient.DeleteContactAsync(contact.AndroidId!);
                        ExecuteOnUI(() => Contacts.Remove(contact));
                        deletedCount++;
                    }
                    catch (Exception ex)
                    {
                        failed.Add($"{contact.FullName ?? contact.AndroidId}: {ex.Message}");
                    }
                }

                if (failed.Count > 0)
                {
                    StatusMessage = $"Deleted {deletedCount} contact(s). {failed.Count} failed.";
                    RaisePhoneError(
                        "Partial delete",
                        $"Some contacts could not be deleted.\n\n" + string.Join("\n", failed.Take(10)) +
                        (failed.Count > 10 ? "\n…" : string.Empty));
                }
                else
                {
                    StatusMessage = $"Deleted {deletedCount} contact(s) from phone.";
                }
            }
            catch (Exception ex)
            {
                HandlePhoneError(ex, "delete contact");
            }
            finally { IsBusy = false; }
        }

        /// <summary>
        /// Centralized phone error handler that raises a presentation-neutral event.
        /// The view decides how to present the error through the application dialog service.
        /// </summary>
        public void HandlePhoneError(Exception ex, string action)
        {
            string title = "Phone Error";
            string message = ex.Message;

            if (ex is PhoneConnectionException pce)
            {
                if (pce.IsSessionExpired)
                {
                    message = "Connection lost. Please reconnect.";
                    DisconnectPhone();
                }
                else if (pce.IsReadOnly)
                {
                    title = "Read-Only Contact";
                    message = "This contact is managed by another app and cannot be modified.";
                }
                else if (pce.IsPermissionDenied)
                {
                    title = "Permission Denied";
                    message = "The companion app does not have permission to modify contacts.";
                }
            }

            StatusMessage = $"Error during {action}.";
            LogMessages.PhoneOperationFailed(_logger, action, message);
            RaisePhoneError(title, $"Failed to {action}: {message}");
        }

        private void RaisePhoneError(string title, string message) =>
            PhoneErrorOccurred?.Invoke(title, message);

        public async Task LoadContactDetailsAsync(Contact? contact)
        {
            var client = PhoneClient;
            if (ActiveSource != ContactSource.AndroidPhone || client is null || contact is null) return;
            if (string.IsNullOrWhiteSpace(contact.AndroidId) || contact.PhoneNumbers.Count > 0) return;

            StatusMessage = $"Loading details for {contact.FullName}…";
            try
            {
                var fullContact = await client.FetchContactDetailAsync(contact.AndroidId).ConfigureAwait(false);
                if (fullContact is null) return;
                ExecuteOnUI(() => contact.UpdateFrom(fullContact));
                StatusMessage = $"Details loaded for {contact.FullName}.";
            }
            catch (Exception ex)
            {
                StatusMessage = "Failed to load contact details.";
                LogMessages.SelectedContactFetchFailed(_logger, ex);
            }
        }

        /// <summary>Disconnect from the phone and switch back to local-VCF mode.</summary>
        public void DisconnectPhone()
        {
            Interlocked.Increment(ref _phoneFetchGeneration);
            _fetchCts?.Cancel();
            _fetchCts = null;
            IsBusy = false;
            PhoneClient?.Disconnect();
            PhoneClient = null;
            ActiveSource = ContactSource.LocalVcf;
            IsSourceLoaded = !string.IsNullOrWhiteSpace(CurrentFilePath);
            Contacts.Clear();
            StatusMessage = "Disconnected from phone.";
        }

        /// <summary>
        /// Toggle sort on <paramref name="columnName"/>: ascending on first click,
        /// descending on second click, ascending again on a new column.
        /// Called by the code-behind <c>ColumnHeader_Click</c> handler.
        /// </summary>
        public void ApplySort(string columnName)
        {
            if (_sortColumn == columnName)
                _sortDirection = _sortDirection == ListSortDirection.Ascending
                    ? ListSortDirection.Descending
                    : ListSortDirection.Ascending;
            else
            {
                _sortColumn = columnName;
                _sortDirection = ListSortDirection.Ascending;
            }

            if (ContactsView == null) return;
            ContactsView.SortDescriptions.Clear();
            ContactsView.SortDescriptions.Add(new SortDescription(_sortColumn, _sortDirection));
            ContactsView.Refresh();
        }

        /// <summary>Apply the current sort without toggling direction. Used during initialisation.</summary>
        public void RefreshSort()
        {
            if (ContactsView == null) return;
            ContactsView.SortDescriptions.Clear();
            ContactsView.SortDescriptions.Add(new SortDescription(_sortColumn, _sortDirection));
        }

        public async Task AddPhoneNumberAsync(Contact contact, PhoneNumber phone)
        {
            contact.PhoneNumbers.Add(phone);
            await SyncContactToPhoneIfNeeded(contact, "add phone number");
        }

        /// <summary>
        /// Sync an in-place edit of an existing phone number back to the Android device.
        /// The caller (dialog) has already mutated the PhoneNumber object.
        /// </summary>
        public async Task PhoneNumberEditedAsync(Contact contact)
        {
            await SyncContactToPhoneIfNeeded(contact, "edit phone number");
        }

        /// <summary>
        /// Remove <paramref name="phone"/> from the contact and, if the contact lives on the
        /// Android device, sync the deletion to the phone immediately so the number cannot
        /// reappear on the next refresh.
        /// </summary>
        public async Task DeletePhoneNumberAsync(Contact contact, PhoneNumber phone)
        {
            contact.PhoneNumbers.Remove(phone);
            await SyncContactToPhoneIfNeeded(contact, "delete phone number");
        }

        private async Task SyncContactToPhoneIfNeeded(Contact contact, string action)
        {
            if (ActiveSource != ContactSource.AndroidPhone
                || PhoneClient == null
                || string.IsNullOrEmpty(contact.AndroidId)
                || contact.IsReadOnly) return;

            IsBusy = true;
            StatusMessage = $"Updating {contact.FullName} on phone…";
            try
            {
                await PhoneClient.UpdateContactAsync(contact);
                StatusMessage = $"Phone updated for {contact.FullName}.";
            }
            catch (Exception ex)
            {
                HandlePhoneError(ex, action);
            }
            finally { IsBusy = false; }
        }

        partial void OnCurrentFilePathChanged(string? value)
            => OnPropertyChanged(nameof(SourceStatusText));

        partial void OnContactCountChanged(int value)
        {
            OnPropertyChanged(nameof(HasContacts));
            OnPropertyChanged(nameof(HasNoContacts));
            OnPropertyChanged(nameof(HasNoSearchResults));
        }

        partial void OnIsSourceLoadedChanged(bool value)
        {
            OnPropertyChanged(nameof(HasNoSource));
            OnPropertyChanged(nameof(HasNoContacts));
            OnPropertyChanged(nameof(HasNoSearchResults));
        }

        internal void UpdateVisibleContactCount()
        {
            if (ContactsView is null)
            {
                VisibleContactCount = Contacts.Count;
                return;
            }

            VisibleContactCount = ContactsView.Cast<object>().Count();
        }

        /// <summary>
        /// Refreshes the ICollectionView filter and updates the visible contact count.
        /// Call after any in-place mutation that does not change the collection itself
        /// (e.g. after a contact's fields are updated via the edit dialog).
        /// </summary>
        public void RefreshView()
        {
            ContactsView?.Refresh();
            UpdateVisibleContactCount();
        }

        private bool FilterContact(object item)
        {
            if (item is not Contact contact) return false;
            if (string.IsNullOrWhiteSpace(SearchText)) return true;

            var search = SearchText.Trim();

            bool Matches(string? value)
            {
                if (string.IsNullOrEmpty(value)) return false;
                return value.Contains(search, StringComparison.OrdinalIgnoreCase);
            }

            return SearchFilterIndex switch
            {
                1 => // Name only
                    Matches(contact.FullName) || Matches(contact.FirstName) || Matches(contact.LastName),
                2 => // Phone only
                    contact.PhoneNumbers.Any(p => Matches(p.Number)),
                3 => // Organisation only
                    Matches(contact.Organization),
                _ => // 0 = All fields (default)
                    Matches(contact.FullName)
                    || Matches(contact.FirstName)
                    || Matches(contact.LastName)
                    || Matches(contact.Organization)
                    || Matches(contact.Title)
                    || Matches(contact.Email)
                    || contact.PhoneNumbers.Any(p => Matches(p.Number))
            };
        }
        // Private helpers

        private async Task FetchMissingDetailsAsync(
            PhoneContactsClient client, long generation, CancellationToken token)
        {
            var dispatcher = System.Windows.Application.Current?.Dispatcher;
            if (dispatcher == null || !IsCurrentPhoneFetch(client, generation, token)) return;

            var stubs = await dispatcher.InvokeAsync(() =>
                IsCurrentPhoneFetch(client, generation, token)
                    ? Contacts
                        .Where(c => !string.IsNullOrEmpty(c.AndroidId) && c.PhoneNumbers.Count == 0)
                        .ToList()
                    : new List<Contact>());

            if (stubs.Count == 0 || !IsCurrentPhoneFetch(client, generation, token)) return;

            try
            {
                var details = await client.FetchContactDetailsInParallelAsync(
                    stubs,
                    maxConcurrency: 8,
                    progress: new Progress<(int current, int total)>(p =>
                    {
                        if (!IsCurrentPhoneFetch(client, generation, token)) return;
                        _ = dispatcher.BeginInvoke(() =>
                        {
                            if (IsCurrentPhoneFetch(client, generation, token))
                                StatusMessage = $"Loading details... {p.current}/{p.total}";
                        });
                    }),
                    cancellationToken: token).ConfigureAwait(false);

                if (!IsCurrentPhoneFetch(client, generation, token)) return;

                _ = dispatcher.BeginInvoke(() =>
                {
                    if (!IsCurrentPhoneFetch(client, generation, token)) return;

                    // Enrich contacts IN-PLACE via UpdateFrom() instead of replacing
                    // object references with ReplaceAll(enriched).
                    //
                    // The old approach (ReplaceAll) swapped each stub for a brand-new
                    // Contact object from the server. Any code that held a reference to
                    // the old object (e.g. UpdatePhoneContactAsync's contact variable,
                    // the selected item, or WPF container bindings) was left pointing at
                    // a stale orphan — causing duplicate rows and reverted edits.
                    //
                    // UpdateFrom() writes the detail fields onto the SAME object already
                    // in the collection. Object references never change, so:
                    //   - No duplicate rows (WPF sees the same item, just with new values)
                    //   - No orphaned references (UpdatePhoneContactAsync.contact is still live)
                    //   - A single CollectionView.Refresh() re-sorts/re-filters correctly
                    var detailMap = details
                        .Where(d => !string.IsNullOrEmpty(d.AndroidId))
                        .ToDictionary(d => d.AndroidId!);

                    foreach (var c in Contacts)
                    {
                        if (!string.IsNullOrEmpty(c.AndroidId) &&
                            detailMap.TryGetValue(c.AndroidId!, out var detail))
                        {
                            c.UpdateFrom(detail);
                        }
                    }

                    ContactsView?.Refresh();
                    ContactCount = Contacts.Count;
                    StatusMessage = $"Loaded {ContactCount} contact(s) from phone.";
                });
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                // A newer refresh or disconnect owns the UI state now.
            }
            catch (Exception ex)
            {
                LogMessages.RefreshContactsFailed(_logger, ex);
            }
        }

        private bool OwnsPhoneFetch(PhoneContactsClient client, long generation)
            => !_disposed &&
               generation == Volatile.Read(ref _phoneFetchGeneration) &&
               ReferenceEquals(PhoneClient, client);

        private bool IsCurrentPhoneFetch(
            PhoneContactsClient client, long generation, CancellationToken token)
            => !token.IsCancellationRequested && OwnsPhoneFetch(client, generation);

        private async Task FinishPhoneFetchAsync(
            Dispatcher dispatcher, PhoneContactsClient client, long generation)
        {
            if (!OwnsPhoneFetch(client, generation)) return;
            await dispatcher.InvokeAsync(() =>
            {
                if (OwnsPhoneFetch(client, generation))
                    IsBusy = false;
            });
        }

        private void ReleaseFetchCancellation(CancellationTokenSource ownedSource)
        {
            Interlocked.CompareExchange(ref _fetchCts, null, ownedSource);
            ownedSource.Dispose();
        }
        // IDisposable

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            Interlocked.Increment(ref _phoneFetchGeneration);
            _fetchCts?.Cancel();
            _fetchCts = null;

            if (_filterDebounceTimer != null)
            {
                _filterDebounceTimer.Stop();
                _filterDebounceTimer = null;
            }

            GC.SuppressFinalize(this);
        }
    }
}

