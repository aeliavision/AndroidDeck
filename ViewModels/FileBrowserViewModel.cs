using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Data;
using Microsoft.Extensions.Logging;
using VcfEditor.Core;
using VcfEditor.Features.Files;
using VcfEditor.Features.PhoneSession;
using VcfEditor.Helpers;
using VcfEditor.Models;
using VcfEditor.Models.DTOs;
using VcfEditor.Services;

namespace VcfEditor.ViewModels
{
    /// <summary>
    ///
    /// Responsibilities:
    ///   - Navigate the phone's directory tree on-demand (no pre-indexing)
    ///   - Track the breadcrumb path so the user can navigate back
    ///   - Report transfer progress with cancellation support
    /// </summary>
    public sealed class FileBrowserViewModel : INotifyPropertyChanged, IAsyncInitializable, IDisposable
    {
        private static readonly ILogger Logger = AppLoggerFactory.CreateLogger(nameof(FileBrowserViewModel));

        public enum ConflictResolution
        {
            Replace,
            KeepBoth,
            Skip
        }

        // ── Dependencies ─────────────────────────────────────────────────────────

        private readonly PhoneApiClient _client;
        private readonly IFileTransferWorkflow _fileTransferWorkflow;
        private readonly ILocalUploadPlanner _localUploadPlanner;
        private readonly SemaphoreSlim _initializationGate = new(1, 1);
        private CancellationTokenSource? _transferCts;
        private bool _isInitialized;
        private bool _disposed;
        private Func<Task>? _lastTransferOperation;

        // ── Observable properties ─────────────────────────────────────────────────

        private ObservableCollection<FileEntryDto> _items = new();
        public ObservableCollection<FileEntryDto> Items
        {
            get => _items;
            private set
            {
                _items = value;
                ItemsView = CollectionViewSource.GetDefaultView(_items);
                ItemsView.Filter = MatchesSearch;
                OnPropertyChanged(nameof(Items));
            }
        }

        private ICollectionView _itemsView = CollectionViewSource.GetDefaultView(Array.Empty<FileEntryDto>());
        public ICollectionView ItemsView
        {
            get => _itemsView;
            private set { _itemsView = value; OnPropertyChanged(nameof(ItemsView)); OnPropertyChanged(nameof(HasVisibleItems)); }
        }

        private string _searchText = string.Empty;
        public string SearchText
        {
            get => _searchText;
            set
            {
                if (_searchText == value) return;
                _searchText = value;
                OnPropertyChanged(nameof(SearchText));
                ItemsView.Refresh();
                OnPropertyChanged(nameof(HasVisibleItems));
            }
        }

        public bool HasVisibleItems => !ItemsView.IsEmpty;

        public ObservableCollection<TransferOperationItem> TransferOutcomes { get; } = new();
        public bool HasTransferOutcomes => TransferOutcomes.Count > 0;
        public bool CanRetryTransfer => _lastTransferOperation is not null && !IsTransferring;

        private ObservableCollection<BreadcrumbItem> _breadcrumbs = new();
        public ObservableCollection<BreadcrumbItem> Breadcrumbs
        {
            get => _breadcrumbs;
            private set { _breadcrumbs = value; OnPropertyChanged(nameof(Breadcrumbs)); }
        }

        private string _currentPath = string.Empty;
        public string CurrentPath
        {
            get => _currentPath;
            private set { _currentPath = value; OnPropertyChanged(nameof(CurrentPath)); }
        }

        private bool _isGridView;
        public bool IsGridView
        {
            get => _isGridView;
            set { _isGridView = value; OnPropertyChanged(nameof(IsGridView)); }
        }

        private FileEntryDto? _selectedItem;
        public FileEntryDto? SelectedItem
        {
            get => _selectedItem;
            set { _selectedItem = value; OnPropertyChanged(nameof(SelectedItem)); }
        }

        private bool _isBusy;
        public bool IsBusy
        {
            get => _isBusy;
            private set { _isBusy = value; OnPropertyChanged(nameof(IsBusy)); OnPropertyChanged(nameof(IsIdle)); }
        }

        public bool IsIdle => !_isBusy;

        private string _statusMessage = string.Empty;
        public string StatusMessage
        {
            get => _statusMessage;
            private set { _statusMessage = value; OnPropertyChanged(nameof(StatusMessage)); }
        }

        private double _transferProgress;
        public double TransferProgress
        {
            get => _transferProgress;
            private set { _transferProgress = value; OnPropertyChanged(nameof(TransferProgress)); }
        }

        private bool _isTransferring;
        public bool IsTransferring
        {
            get => _isTransferring;
            private set
            {
                _isTransferring = value;
                OnPropertyChanged(nameof(IsTransferring));
                OnPropertyChanged(nameof(CanRetryTransfer));
            }
        }

        private string? _errorMessage;
        public string? ErrorMessage
        {
            get => _errorMessage;
            private set { _errorMessage = value; OnPropertyChanged(nameof(ErrorMessage)); OnPropertyChanged(nameof(HasError)); }
        }

        public bool HasError => !string.IsNullOrEmpty(_errorMessage);

        public bool IsInitialized
        {
            get => _isInitialized;
            private set
            {
                if (_isInitialized == value) return;
                _isInitialized = value;
                OnPropertyChanged(nameof(IsInitialized));
            }
        }

        /// <summary>
        /// True when the Android companion app does not have All Files Access permission.
        /// The view shows a "Grant permission on your phone" prompt instead of the file list.
        /// </summary>
        private bool _requiresAllFilesAccess;
        public bool RequiresAllFilesAccess
        {
            get => _requiresAllFilesAccess;
            private set
            {
                if (_requiresAllFilesAccess == value) return;
                _requiresAllFilesAccess = value;
                OnPropertyChanged(nameof(RequiresAllFilesAccess));
            }
        }

        // ── Construction ──────────────────────────────────────────────────────────

        // Context is optional — unit tests supply null to skip capability checks.
        private readonly PhoneSessionContext? _sessionContext;

        public FileBrowserViewModel(
            PhoneApiClient client,
            IFileTransferWorkflow fileTransferWorkflow,
            ILocalUploadPlanner localUploadPlanner,
            PhoneSessionContext? sessionContext = null)
        {
            ArgumentNullException.ThrowIfNull(client);
            ArgumentNullException.ThrowIfNull(fileTransferWorkflow);
            ArgumentNullException.ThrowIfNull(localUploadPlanner);
            _client = client;
            _fileTransferWorkflow = fileTransferWorkflow;
            _localUploadPlanner = localUploadPlanner;
            _sessionContext = sessionContext;
        }

        // ── Navigation ────────────────────────────────────────────────────────────

        /// <summary>Load the root directory exactly once for the current phone session.</summary>
        public async Task InitializeAsync(CancellationToken cancellationToken = default)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (IsInitialized) return;

            // When phone is connected but Files permission is not granted, don't attempt
            // a directory load (the server will return 403/404). Show a permission prompt instead.
            var caps = _sessionContext?.Capabilities;
            if (caps is not null && caps.IsPhoneConnected && !caps.SupportsFiles)
            {
                RequiresAllFilesAccess = caps.RequiresAllFilesAccess;
                IsInitialized = true;
                return;
            }

            RequiresAllFilesAccess = false;

            await _initializationGate.WaitAsync(cancellationToken);
            try
            {
                if (IsInitialized) return;

                await NavigateToAsync("/sdcard", cancellationToken);
                if (!HasError && !string.IsNullOrWhiteSpace(CurrentPath))
                    IsInitialized = true;
            }
            finally
            {
                _initializationGate.Release();
            }
        }

        public Task InitialiseAsync() => InitializeAsync();

        /// <summary>Navigate into [path].</summary>
        public async Task NavigateToAsync(string path, CancellationToken cancellationToken = default)
        {
            await RunBusyAsync(async token =>
            {
                StatusMessage = $"Loading {path}…";
                var listing = await _fileTransferWorkflow.ListDirectoryAsync(path, token)
                    .ConfigureAwait(false);

                var dispatcher = Application.Current?.Dispatcher;
                if (dispatcher == null) return;

                await dispatcher.InvokeAsync(() =>
                {
                    CurrentPath = listing.Path ?? path;
                    Items = new ObservableCollection<FileEntryDto>(listing.Items);
                    SelectedItem = null;
                    OnPropertyChanged(nameof(HasVisibleItems));
                    UpdateBreadcrumbs(CurrentPath);
                    StatusMessage = $"{listing.Items.Count} item(s)";
                    ErrorMessage = null;
                });
            }, cancellationToken);
        }

        /// <summary>Navigate up to the parent directory.</summary>
        public async Task NavigateUpAsync()
        {
            var parent = Path.GetDirectoryName(CurrentPath.Replace('/', Path.DirectorySeparatorChar));
            if (parent == null) return;
            await NavigateToAsync(parent.Replace(Path.DirectorySeparatorChar, '/')).ConfigureAwait(false);
        }

        /// <summary>Refresh the current directory.</summary>
        public Task RefreshAsync() => NavigateToAsync(CurrentPath);

        /// <summary>
        /// Download [remoteEntry] to [localFolder].
        /// Shows progress and supports cancellation.
        /// </summary>
        public async Task DownloadAsync(FileEntryDto remoteEntry, string localFolder)
        {
            if (remoteEntry.IsDirectory)
            {
                ErrorMessage = "Directory download is not supported. Select individual files.";
                return;
            }

            var localPath = Path.Combine(localFolder, remoteEntry.Name ?? "download");
            _lastTransferOperation = () => DownloadAsync(remoteEntry, localFolder);
            await RunTransferAsync(async token =>
            {
                StatusMessage = $"Downloading {remoteEntry.Name}…";
                TransferProgress = 0;

                await _fileTransferWorkflow.DownloadAsync(
                    remotePath: remoteEntry.Path!,
                    localPath: localPath,
                    progress: new Progress<(long received, long total)>(p =>
                    {
                        Application.Current?.Dispatcher?.BeginInvoke(() =>
                        {
                            if (p.total > 0)
                                TransferProgress = (double)p.received / p.total * 100;
                            StatusMessage = $"Downloading… {FormatBytes(p.received)} / {FormatBytes(p.total)}";
                        });
                    }),
                    cancellationToken: token).ConfigureAwait(false);

                StatusMessage = $"Downloaded '{remoteEntry.Name}' → {localFolder}";
                TransferProgress = 100;
                AddTransferOutcome(remoteEntry.Name ?? "download", "Phone → PC", "Completed", localFolder);
            });
        }

        /// <summary>
        /// Upload one or more local files to the current phone directory.
        /// Supports drag-and-drop (pass the dropped file paths).
        /// </summary>
        public async Task UploadFilesAsync(string[] localPaths)
        {
            await UploadFilesWithConflictsAsync(localPaths, (_, __) => Task.FromResult(ConflictResolution.Replace))
                .ConfigureAwait(false);
        }

        /// <summary>
        /// Extracts all files from folders and maintains relative structure.
        /// </summary>
        public async Task UploadRecursiveAsync(string[] localPaths)
        {
            var retryPaths = localPaths.ToArray();
            _lastTransferOperation = () => UploadRecursiveAsync(retryPaths);
            await RunTransferAsync(async token =>
            {
                StatusMessage = "Preparing local files…";
                var filesToUpload = await _localUploadPlanner.BuildAsync(
                    localPaths,
                    CurrentPath,
                    token).ConfigureAwait(false);
                var total = filesToUpload.Count;
                for (int i = 0; i < total; i++)
                {
                    token.ThrowIfCancellationRequested();
                    var item = filesToUpload[i];
                    var local = item.LocalPath;
                    var remote = item.RemotePath;

                    StatusMessage = $"Uploading {Path.GetFileName(local)} ({i + 1}/{total})…";

                    // Create parent directories on phone if needed
                    var parentDir = Path.GetDirectoryName(remote.Replace('/', Path.DirectorySeparatorChar))?
                        .Replace(Path.DirectorySeparatorChar, '/');
                    if (!string.IsNullOrEmpty(parentDir))
                    {
                         await _fileTransferWorkflow.CreateDirectoryAsync(parentDir, token).ConfigureAwait(false);
                    }

                    await _fileTransferWorkflow.UploadToPathAsync(
                        localPath: local,
                        destinationPath: remote,
                        progress: new Progress<(long sent, long total2)>(p =>
                        {
                            Application.Current?.Dispatcher?.BeginInvoke(() =>
                            {
                                if (p.total2 > 0)
                                    TransferProgress = (i + (double)p.sent / p.total2) / total * 100;
                            });
                        }),
                        cancellationToken: token).ConfigureAwait(false);
                    AddTransferOutcome(Path.GetFileName(local), "PC → Phone", "Completed", remote);
                }

                await RefreshAsync().ConfigureAwait(false);
                StatusMessage = $"Uploaded {total} item(s)";
            });
        }

        public async Task UploadFilesWithConflictsAsync(
            string[] localPaths,
            Func<string, string, Task<ConflictResolution>> onConflict)
        {
            var retryPaths = localPaths.ToArray();
            _lastTransferOperation = () => UploadFilesWithConflictsAsync(retryPaths, onConflict);
            await RunTransferAsync(async token =>
            {
                if (localPaths.Length == 0) return;

                StatusMessage = "Loading destination…";
                var destListing = await _fileTransferWorkflow.ListDirectoryAsync(CurrentPath, token)
                    .ConfigureAwait(false);
                var existingNames = new System.Collections.Generic.HashSet<string>(
                    destListing.Items
                        .Select(i => i.Name)
                        .Where(n => !string.IsNullOrWhiteSpace(n))
                        .Select(n => n!),
                    StringComparer.OrdinalIgnoreCase);

                var total = localPaths.Length;
                for (int i = 0; i < total; i++)
                {
                    token.ThrowIfCancellationRequested();
                    var localPath = localPaths[i];
                    var baseName = Path.GetFileName(localPath);
                    var targetName = baseName;
                    var destPath = CurrentPath.TrimEnd('/') + "/" + targetName;

                    if (existingNames.Contains(targetName))
                    {
                        var resolution = await onConflict(baseName, destPath).ConfigureAwait(false);
                        if (resolution == ConflictResolution.Skip)
                            continue;
                        if (resolution == ConflictResolution.KeepBoth)
                        {
                            targetName = GenerateUniqueName(existingNames, baseName, isDirectory: false);
                            destPath = CurrentPath.TrimEnd('/') + "/" + targetName;
                        }
                        // Replace => keep destPath as-is (server upload overwrites)
                    }

                    StatusMessage = $"Uploading {targetName} ({i + 1}/{total})…";
                    TransferProgress = 0;

                    await _fileTransferWorkflow.UploadToPathAsync(
                        localPath: localPath,
                        destinationPath: destPath,
                        progress: new Progress<(long sent, long total2)>(p =>
                        {
                            Application.Current?.Dispatcher?.BeginInvoke(() =>
                            {
                                if (p.total2 > 0)
                                    TransferProgress = (double)p.sent / p.total2 * 100;
                                StatusMessage = $"Uploading {targetName}… {FormatBytes(p.sent)} / {FormatBytes(p.total2)}";
                            });
                        }),
                        cancellationToken: token).ConfigureAwait(false);

                    existingNames.Add(targetName);
                    AddTransferOutcome(targetName, "PC → Phone", "Completed", destPath);
                }

                StatusMessage = $"Uploaded {total} file(s) to {CurrentPath}";
                TransferProgress = 100;
                await RefreshAsync().ConfigureAwait(false);
            });
        }

        // ── Delete ────────────────────────────────────────────────────────────────

        public async Task DeleteAsync(FileEntryDto entry, bool recursive = false)
        {
            await RunBusyAsync(async token =>
            {
                StatusMessage = $"Deleting {entry.Name}…";
                await _fileTransferWorkflow.DeleteAsync(entry.Path!, recursive, token).ConfigureAwait(false);
                await RefreshAsync().ConfigureAwait(false);
                StatusMessage = $"Deleted '{entry.Name}'";
            });
        }

        // ── Mkdir ─────────────────────────────────────────────────────────────────

        public async Task MkdirAsync(string folderName)
        {
            var newPath = CurrentPath.TrimEnd('/') + "/" + folderName;
            await RunBusyAsync(async token =>
            {
                await _fileTransferWorkflow.CreateDirectoryAsync(newPath, token).ConfigureAwait(false);
                await RefreshAsync().ConfigureAwait(false);
                StatusMessage = $"Created folder '{folderName}'";
            });
        }

        public async Task RenameAsync(FileEntryDto entry, string newName, bool overwrite = false)
        {
            await RunBusyAsync(async token =>
            {
                StatusMessage = $"Renaming {entry.Name}…";
                await _fileTransferWorkflow.RenameAsync(entry.Path!, newName, overwrite, token).ConfigureAwait(false);
                await RefreshAsync().ConfigureAwait(false);
                StatusMessage = $"Renamed '{entry.Name}'";
            });
        }

        public async Task MoveAsync(FileEntryDto entry, string destinationPath, bool overwrite = false)
        {
            await RunBusyAsync(async token =>
            {
                StatusMessage = $"Moving {entry.Name}…";
                await _fileTransferWorkflow.MoveAsync(entry.Path!, destinationPath, overwrite, token).ConfigureAwait(false);
                await RefreshAsync().ConfigureAwait(false);
                StatusMessage = $"Moved '{entry.Name}'";
            });
        }

        public async Task MoveManyAsync(
            System.Collections.Generic.IReadOnlyList<FileEntryDto> entries,
            string destinationDirectory,
            Func<FileEntryDto, string, Task<ConflictResolution>> onConflict)
        {
            if (entries.Count == 0) return;

            await RunBusyAsync(async token =>
            {
                var destDir = destinationDirectory.TrimEnd('/');
                StatusMessage = $"Loading destination…";
                var destListing = await _fileTransferWorkflow.ListDirectoryAsync(destDir, token).ConfigureAwait(false);
                var existingNames = new System.Collections.Generic.HashSet<string>(
                    destListing.Items
                        .Select(i => i.Name)
                        .Where(n => !string.IsNullOrWhiteSpace(n))
                        .Select(n => n!),
                    StringComparer.OrdinalIgnoreCase);

                for (int i = 0; i < entries.Count; i++)
                {
                    var entry = entries[i];
                    token.ThrowIfCancellationRequested();

                    var baseName = entry.Name ?? "item";
                    var targetName = baseName;
                    var targetPath = destDir + "/" + targetName;

                    if (existingNames.Contains(targetName))
                    {
                        var resolution = await onConflict(entry, targetPath).ConfigureAwait(false);
                        if (resolution == ConflictResolution.Skip)
                            continue;
                        if (resolution == ConflictResolution.Replace)
                        {
                            StatusMessage = $"Moving {baseName} ({i + 1}/{entries.Count})…";
                            await _fileTransferWorkflow.MoveAsync(entry.Path!, targetPath, overwrite: true, token)
                                .ConfigureAwait(false);
                            continue;
                        }

                        // KeepBoth
                        targetName = GenerateUniqueName(existingNames, baseName, entry.IsDirectory);
                        targetPath = destDir + "/" + targetName;
                    }

                    StatusMessage = $"Moving {baseName} ({i + 1}/{entries.Count})…";
                    await _fileTransferWorkflow.MoveAsync(entry.Path!, targetPath, overwrite: false, token)
                        .ConfigureAwait(false);
                    existingNames.Add(targetName);
                }

                await RefreshAsync().ConfigureAwait(false);
                StatusMessage = $"Moved {entries.Count} item(s)";
            });
        }

        // ── Cancel ────────────────────────────────────────────────────────────────

        public void CancelTransfer()
        {
            _transferCts?.Cancel();
            StatusMessage = "Transfer cancelled.";
        }

        public Task RetryLastTransferAsync()
            => _lastTransferOperation?.Invoke() ?? Task.CompletedTask;

        // ── Private helpers ────────────────────────────────────────────────────────

        private async Task RunBusyAsync(
            Func<CancellationToken, Task> work,
            CancellationToken cancellationToken = default)
        {
            if (IsBusy) return;
            IsBusy = true;
            ErrorMessage = null;
            try
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                cts.CancelAfter(TimeSpan.FromSeconds(30));
                // Do NOT use ConfigureAwait(false) here — the finally block sets WPF-bound
                // properties (IsBusy, ErrorMessage) which must fire PropertyChanged on the UI thread.
                await work(cts.Token);
            }
            catch (OperationCanceledException)
            {
                StatusMessage = "Operation cancelled.";
            }
            catch (Exception ex)
            {
                ErrorMessage = ex.Message;
                LogMessages.FileBrowserFailed(Logger, ex);
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async Task RunTransferAsync(Func<CancellationToken, Task> work)
        {
            if (IsTransferring) return;
            _transferCts = new CancellationTokenSource();
            IsTransferring = true;
            ErrorMessage = null;
            TransferProgress = 0;
            TransferOutcomes.Clear();
            OnPropertyChanged(nameof(HasTransferOutcomes));
            try
            {
                // Do NOT use ConfigureAwait(false) — finally/catch sets WPF-bound properties
                // (IsTransferring, ErrorMessage, TransferProgress) on the UI thread.
                await work(_transferCts.Token);
            }
            catch (OperationCanceledException)
            {
                StatusMessage = "Transfer cancelled.";
                TransferProgress = 0;
                AddTransferOutcome("Current transfer", "Transfer", "Cancelled");
            }
            catch (Exception ex)
            {
                ErrorMessage = ex.Message;
                TransferProgress = 0;
                AddTransferOutcome("Current transfer", "Transfer", "Failed", ex.Message);
                LogMessages.FileTransferFailed(Logger, ex);
            }
            finally
            {
                IsTransferring = false;
                _transferCts?.Dispose();
                _transferCts = null;
            }
        }

        private bool MatchesSearch(object item)
        {
            if (item is not FileEntryDto entry) return false;
            if (string.IsNullOrWhiteSpace(SearchText)) return true;
            var query = SearchText.Trim();
            return (entry.Name?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false)
                || (entry.MimeType?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false);
        }

        private void AddTransferOutcome(string name, string direction, string status, string? message = null)
        {
            void Add()
            {
                TransferOutcomes.Add(new TransferOperationItem(name, direction, status, message));
                OnPropertyChanged(nameof(HasTransferOutcomes));
            }

            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher is null || dispatcher.CheckAccess()) Add();
            else dispatcher.BeginInvoke(Add);
        }

        private void UpdateBreadcrumbs(string path)
        {
            var parts = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
            var crumbs = new ObservableCollection<BreadcrumbItem>();
            var accumulated = string.Empty;
            foreach (var part in parts)
            {
                accumulated += "/" + part;
                crumbs.Add(new BreadcrumbItem(part, accumulated));
            }
            Breadcrumbs = crumbs;
        }

        private static string FormatBytes(long bytes) => bytes switch
        {
            < 1024 => $"{bytes} B",
            < 1024 * 1024 => $"{bytes / 1024.0:F1} KB",
            < 1024L * 1024 * 1024 => $"{bytes / (1024.0 * 1024):F1} MB",
            _ => $"{bytes / (1024.0 * 1024 * 1024):F2} GB"
        };

        private static string GenerateUniqueName(
            System.Collections.Generic.HashSet<string> existingNames,
            string baseName,
            bool isDirectory)
        {
            var name = baseName;
            var ext = string.Empty;

            if (!isDirectory)
            {
                ext = Path.GetExtension(baseName);
                name = Path.GetFileNameWithoutExtension(baseName);
            }

            for (int i = 1; i < 10_000; i++)
            {
                var candidate = isDirectory
                    ? $"{name} ({i})"
                    : $"{name} ({i}){ext}";
                if (!existingNames.Contains(candidate))
                    return candidate;
            }

            // Fallback, should never happen.
            return isDirectory ? $"{name} ({Guid.NewGuid():N})" : $"{name} ({Guid.NewGuid():N}){ext}";
        }

        // ── INotifyPropertyChanged ─────────────────────────────────────────────────

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged(string name) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        // ── IDisposable ────────────────────────────────────────────────────────────

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            _transferCts?.Cancel();
            _transferCts?.Dispose();
            _transferCts = null;
            _initializationGate.Dispose();
            GC.SuppressFinalize(this);
        }
    }

    /// <summary>A single item in the path breadcrumb trail.</summary>
    public record BreadcrumbItem(string Label, string Path);
}
