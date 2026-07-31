using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media.Imaging;
using Microsoft.Extensions.Logging;
using VcfEditor.Core;
using VcfEditor.Helpers;
using VcfEditor.Features.Gallery;
using VcfEditor.Models.DTOs;
using VcfEditor.Services;

namespace VcfEditor.ViewModels
{
    /// <summary>
    ///
    /// Responsibilities:
    ///   - List albums on first load
    ///   - Transfer progress with cancellation
    /// </summary>
    public sealed class GalleryViewModel : INotifyPropertyChanged, IAsyncInitializable, IDisposable
    {
        private static readonly ILogger Logger = AppLoggerFactory.CreateLogger(nameof(GalleryViewModel));

        // ── Dependencies ──────────────────────────────────────────────────────────

        private readonly PhoneApiClient _client;
        private readonly IGalleryTransferWorkflow _galleryTransferWorkflow;
        private readonly SemaphoreSlim _initializationGate = new(1, 1);
        private CancellationTokenSource? _loadCts;
        private CancellationTokenSource? _transferCts;
        private string? _lastLoadedAlbumId;
        private bool _suppressAlbumAutoLoad;
        private bool _isInitialized;
        private bool _disposed;
        private const int IncrementalPageSize = 60;
        private int? _nextMediaPage;

        // ── Observable properties ──────────────────────────────────────────────────

        private ObservableCollection<AlbumDto> _albums = new();
        public ObservableCollection<AlbumDto> Albums
        {
            get => _albums;
            private set
            {
                _albums = value;
                OnPropertyChanged(nameof(Albums));
                OnPropertyChanged(nameof(HasAlbums));
            }
        }

        private BulkObservableCollection<GalleryMediaItem> _mediaItems = new();
        public BulkObservableCollection<GalleryMediaItem> MediaItems
        {
            get => _mediaItems;
            private set
            {
                _mediaItems = value;
                OnPropertyChanged(nameof(MediaItems));
                OnPropertyChanged(nameof(HasMedia));
                OnPropertyChanged(nameof(HasNoMedia));
            }
        }

        private AlbumDto? _selectedAlbum;
        public AlbumDto? SelectedAlbum
        {
            get => _selectedAlbum;
            set
            {
                if (_selectedAlbum == value) return;
                _selectedAlbum = value;
                OnPropertyChanged(nameof(SelectedAlbum));
                OnPropertyChanged(nameof(HasNoMedia));
                if (!_suppressAlbumAutoLoad && _client.IsConnected)
                    _ = LoadMediaAsync(value?.Id);
            }
        }

        private GalleryMediaItem? _previewItem;
        public GalleryMediaItem? PreviewItem
        {
            get => _previewItem;
            set { _previewItem = value; OnPropertyChanged(nameof(PreviewItem)); OnPropertyChanged(nameof(HasPreview)); }
        }

        public bool HasPreview => _previewItem != null;

        private bool _isBusy;
        public bool IsBusy
        {
            get => _isBusy;
            private set
            {
                _isBusy = value;
                OnPropertyChanged(nameof(IsBusy));
                OnPropertyChanged(nameof(HasNoMedia));
            }
        }

        private bool _isTransferring;
        public bool IsTransferring
        {
            get => _isTransferring;
            private set { _isTransferring = value; OnPropertyChanged(nameof(IsTransferring)); }
        }

        private double _transferProgress;
        public double TransferProgress
        {
            get => _transferProgress;
            private set { _transferProgress = value; OnPropertyChanged(nameof(TransferProgress)); }
        }

        private string _statusMessage = string.Empty;
        public string StatusMessage
        {
            get => _statusMessage;
            private set { _statusMessage = value; OnPropertyChanged(nameof(StatusMessage)); }
        }

        private string? _errorMessage;
        public string? ErrorMessage
        {
            get => _errorMessage;
            private set { _errorMessage = value; OnPropertyChanged(nameof(ErrorMessage)); OnPropertyChanged(nameof(HasError)); }
        }

        public bool HasError => !string.IsNullOrEmpty(_errorMessage);

        private int _selectedCount;
        public int SelectedCount
        {
            get => _selectedCount;
            private set
            {
                if (_selectedCount == value) return;
                _selectedCount = value;
                OnPropertyChanged(nameof(SelectedCount));
                OnPropertyChanged(nameof(HasSelection));
            }
        }

        public bool HasSelection => SelectedCount > 0;
        public bool HasAlbums => Albums.Count > 0;
        public bool HasMedia => MediaItems.Count > 0;
        public bool HasNoMedia => !IsBusy && SelectedAlbum is not null && MediaItems.Count == 0;
        public bool HasMoreItems => _nextMediaPage.HasValue;

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

        // ── Construction ───────────────────────────────────────────────────────────

        public GalleryViewModel(PhoneApiClient client, IGalleryTransferWorkflow galleryTransferWorkflow)
        {
            ArgumentNullException.ThrowIfNull(client);
            ArgumentNullException.ThrowIfNull(galleryTransferWorkflow);
            _client = client;
            _galleryTransferWorkflow = galleryTransferWorkflow;
        }

        // ── Initialisation ─────────────────────────────────────────────────────────

        public async Task InitializeAsync(CancellationToken cancellationToken = default)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (IsInitialized) return;

            await _initializationGate.WaitAsync(cancellationToken);
            try
            {
                if (IsInitialized) return;
                await LoadGalleryAsync(cancellationToken);
                if (string.IsNullOrWhiteSpace(ErrorMessage))
                    IsInitialized = true;
            }
            finally
            {
                _initializationGate.Release();
            }
        }

        public Task RefreshAsync(CancellationToken cancellationToken = default)
            => LoadGalleryAsync(cancellationToken);

        public Task InitialiseAsync() => InitializeAsync();

        private async Task LoadGalleryAsync(CancellationToken cancellationToken)
        {
            await RunOnUiAsync(() =>
            {
                IsBusy = true;
                ErrorMessage = null;
                StatusMessage = "Loading albums…";
            }).ConfigureAwait(false);
            try
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                cts.CancelAfter(TimeSpan.FromSeconds(30));
                var albums = await _galleryTransferWorkflow.GetAlbumsAsync(cts.Token).ConfigureAwait(false);
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    Albums = new ObservableCollection<AlbumDto>(albums);
                    StatusMessage = $"{albums.Count} album(s)";
                });

                var firstAlbumId = albums.Count > 0 ? albums[0].Id : null;
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    _suppressAlbumAutoLoad = true;
                    try
                    {
                        SelectedAlbum = albums.Count > 0 ? albums[0] : null;
                    }
                    finally
                    {
                        _suppressAlbumAutoLoad = false;
                    }
                    IsBusy = true;
                });

                await LoadMediaAsync(firstAlbumId, cts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (OperationCanceledException ex)
            {
                await RunOnUiAsync(() => ErrorMessage = "Gallery loading timed out.").ConfigureAwait(false);
                LogMessages.GalleryInitializationFailed(Logger, ex);
            }
            catch (Exception ex)
            {
                await RunOnUiAsync(() => ErrorMessage = ex.Message).ConfigureAwait(false);
                LogMessages.GalleryInitializationFailed(Logger, ex);
            }
            finally
            {
                await RunOnUiAsync(() => IsBusy = false).ConfigureAwait(false);
            }
        }

        // ── Media loading ──────────────────────────────────────────────────────────

        public async Task LoadMediaAsync(string? albumId, CancellationToken cancellationToken = default)
        {
            _loadCts?.Cancel();
            _loadCts?.Dispose();
            _loadCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var token = _loadCts.Token;

            _lastLoadedAlbumId = albumId;
            await RunOnUiAsync(() =>
            {
                IsBusy = true;
                ErrorMessage = null;
                StatusMessage = "Loading media…";
                PreviewItem = null;
            }).ConfigureAwait(false);

            try
            {
                var page = await _galleryTransferWorkflow.GetMediaPageAsync(
                    albumId: albumId,
                    page: 1,
                    pageSize: IncrementalPageSize,
                    cancellationToken: token).ConfigureAwait(false);

                token.ThrowIfCancellationRequested();
                var items = page.Items.Select(media => new GalleryMediaItem(media)).ToList();
                _nextMediaPage = page.NextPage;

                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    MediaItems.ReplaceAll(Array.Empty<GalleryMediaItem>());
                    AppendMediaPage(items);
                    StatusMessage = $"{MediaItems.Count} item(s) loaded";
                    OnPropertyChanged(nameof(HasMedia));
                    OnPropertyChanged(nameof(HasNoMedia));
                    OnPropertyChanged(nameof(HasMoreItems));
                });
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                await RunOnUiAsync(() => ErrorMessage = ex.Message).ConfigureAwait(false);
                LogMessages.GalleryMediaLoadFailed(Logger, ex);
            }
            finally { await RunOnUiAsync(() => IsBusy = false).ConfigureAwait(false); }
        }

        public async Task LoadMoreAsync()
        {
            if (!HasMoreItems || IsBusy || !_nextMediaPage.HasValue) return;

            var token = _loadCts?.Token ?? CancellationToken.None;
            IsBusy = true;
            ErrorMessage = null;
            try
            {
                var page = await _galleryTransferWorkflow.GetMediaPageAsync(
                    albumId: _lastLoadedAlbumId,
                    page: _nextMediaPage.Value,
                    pageSize: IncrementalPageSize,
                    cancellationToken: token).ConfigureAwait(false);
                token.ThrowIfCancellationRequested();

                var items = page.Items.Select(media => new GalleryMediaItem(media)).ToList();
                _nextMediaPage = page.NextPage;
                await RunOnUiAsync(() =>
                {
                    AppendMediaPage(items);
                    StatusMessage = $"{MediaItems.Count} item(s) loaded";
                    OnPropertyChanged(nameof(HasMoreItems));
                }).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                ErrorMessage = ex.Message;
                LogMessages.GalleryMediaLoadFailed(Logger, ex);
            }
            finally
            {
                IsBusy = false;
            }
        }

        private void AppendMediaPage(List<GalleryMediaItem> items)
        {
            foreach (var item in items)
            {
                item.PropertyChanged -= MediaItem_PropertyChanged;
                item.PropertyChanged += MediaItem_PropertyChanged;
                MediaItems.Add(item);
            }
            UpdateSelectedCount(MediaItems);
            OnPropertyChanged(nameof(HasMedia));
            OnPropertyChanged(nameof(HasNoMedia));
            OnPropertyChanged(nameof(HasMoreItems));
        }

        public Task RefreshMissingThumbnailsAsync(CancellationToken cancellationToken = default)
            => LoadThumbnailsForItemsAsync(MediaItems, cancellationToken);

        public async Task LoadThumbnailsForItemsAsync(
            IEnumerable<GalleryMediaItem> items,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(items);
            if (!_client.IsConnected)
                return;

            var missingItems = items
                .Where(item => item.Thumbnail is null && !string.IsNullOrWhiteSpace(item.Media.Id))
                .Distinct()
                .ToList();
            if (missingItems.Count == 0)
                return;

            foreach (var item in missingItems)
                item.LoadError = false;

            using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                _loadCts?.Token ?? CancellationToken.None);
            try
            {
                await LoadThumbnailsAsync(missingItems, linkedCancellation.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (linkedCancellation.IsCancellationRequested)
            {
            }
            catch (Exception ex)
            {
                LogMessages.GalleryThumbnailTaskFailed(Logger, ex);
            }
        }

        private async Task LoadThumbnailsAsync(List<GalleryMediaItem> items, CancellationToken token)
        {
            if (items.Count == 0)
                return;

            // Process four visible items at a time to avoid overwhelming the phone or UI.
            using var gate = new SemaphoreSlim(4, 4);
            var tasks = items.Select(async item =>
            {
                var enteredGate = false;
                try
                {
                    await gate.WaitAsync(token).ConfigureAwait(false);
                    enteredGate = true;
                    token.ThrowIfCancellationRequested();
                    if (item.Thumbnail is not null)
                        return;

                    var jpegBytes = await _galleryTransferWorkflow.GetThumbnailAsync(
                        item.Media.Id ?? string.Empty,
                        item.Media.MediaType ?? "image",
                        maxDim: 256,
                        cancellationToken: token).ConfigureAwait(false);

                    if (jpegBytes is null || jpegBytes.Length == 0)
                    {
                        await RunOnUiAsync(() => item.LoadError = true).ConfigureAwait(false);
                        return;
                    }

                    var bitmap = JpegToBitmapImage(jpegBytes);
                    await RunOnUiAsync(() =>
                    {
                        item.Thumbnail = bitmap;
                        item.LoadError = bitmap is null;
                    }).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                }
                catch (Exception ex)
                {
                    LogMessages.GalleryThumbnailFetchFailed(Logger, ex, item.Media.Id);
                    await RunOnUiAsync(() => item.LoadError = true).ConfigureAwait(false);
                }
                finally
                {
                    if (enteredGate)
                        gate.Release();
                }
            });

            await Task.WhenAll(tasks).ConfigureAwait(false);
        }

        public async Task OpenPreviewAsync(GalleryMediaItem item)
        {
            PreviewItem = item;
            if (item.FullResolution != null) return;

            // Load full-resolution in background
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
                var tempPath = Path.Combine(Path.GetTempPath(), "VcfEditorPreview_" + item.Media.Id);
                await _galleryTransferWorkflow.DownloadAsync(
                    item.Media.Id!,
                    item.Media.MediaType ?? "image",
                    tempPath,
                    cancellationToken: cts.Token).ConfigureAwait(false);

                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.UriSource = new Uri(tempPath);
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.EndInit();
                bmp.Freeze();

                await Application.Current.Dispatcher.InvokeAsync(() => item.FullResolution = bmp);
            }
            catch (Exception ex)
            {
                LogMessages.GalleryPreviewLoadFailed(Logger, ex);
            }
        }

        public void ClosePreview() => PreviewItem = null;

        private void WireSelectionTracking(System.Collections.Generic.IReadOnlyList<GalleryMediaItem> items)
        {
            foreach (var item in items)
            {
                item.PropertyChanged -= MediaItem_PropertyChanged;
                item.PropertyChanged += MediaItem_PropertyChanged;
            }
            UpdateSelectedCount(items);
        }

        private void MediaItem_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName != nameof(GalleryMediaItem.IsSelected)) return;
            UpdateSelectedCount(MediaItems);
        }

        private void UpdateSelectedCount(System.Collections.Generic.IEnumerable<GalleryMediaItem> items)
        {
            SelectedCount = items.Count(i => i.IsSelected);
        }

        public async Task DeleteSelectedAsync(IEnumerable<GalleryMediaItem> selected)
        {
            var items = selected.ToList();
            if (items.Count == 0) return;

            IsBusy = true;
            ErrorMessage = null;
            try
            {
                StatusMessage = $"Deleting {items.Count} item(s)…";
                foreach (var group in items
                             .Where(i => !string.IsNullOrWhiteSpace(i.Media.Id))
                             .GroupBy(i => i.Media.MediaType ?? "image", StringComparer.OrdinalIgnoreCase))
                {
                    var ids = group.Select(i => i.Media.Id!).ToList();
                    var result = await _galleryTransferWorkflow.DeleteAsync(ids, mediaType: group.Key)
                        .ConfigureAwait(false);
                    if (!result.Success)
                        throw new PhoneConnectionException(result.Error ?? "delete_failed");
                }
                StatusMessage = "Delete complete.";
                await LoadMediaAsync(SelectedAlbum?.Id).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                ErrorMessage = ex.Message;
            }
            finally { IsBusy = false; }
        }

        public async Task MoveSelectedAsync(IEnumerable<GalleryMediaItem> selected, string targetRelativePath)
        {
            var items = selected.ToList();
            if (items.Count == 0) return;
            if (string.IsNullOrWhiteSpace(targetRelativePath)) return;

            IsBusy = true;
            ErrorMessage = null;
            try
            {
                StatusMessage = $"Moving {items.Count} item(s)…";
                foreach (var group in items
                             .Where(i => !string.IsNullOrWhiteSpace(i.Media.Id))
                             .GroupBy(i => i.Media.MediaType ?? "image", StringComparer.OrdinalIgnoreCase))
                {
                    var ids = group.Select(i => i.Media.Id!).ToList();
                    var result = await _galleryTransferWorkflow.MoveAsync(ids, targetRelativePath, mediaType: group.Key)
                        .ConfigureAwait(false);
                    if (!result.Success)
                        throw new PhoneConnectionException(result.Error ?? "move_failed");
                }
                StatusMessage = "Move complete.";
                await LoadMediaAsync(SelectedAlbum?.Id).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                ErrorMessage = ex.Message;
            }
            finally { IsBusy = false; }
        }

        public async Task RenameAsync(GalleryMediaItem item, string newName)
        {
            if (string.IsNullOrWhiteSpace(item.Media.Id)) return;
            if (string.IsNullOrWhiteSpace(newName)) return;

            IsBusy = true;
            ErrorMessage = null;
            try
            {
                StatusMessage = "Renaming…";
                var result = await _galleryTransferWorkflow.RenameAsync(item.Media.Id!, newName, mediaType: item.Media.MediaType ?? "image").ConfigureAwait(false);
                if (!result.Success)
                    throw new PhoneConnectionException(result.Error ?? "rename_failed");
                StatusMessage = "Rename complete.";
                await LoadMediaAsync(SelectedAlbum?.Id).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                ErrorMessage = ex.Message;
            }
            finally { IsBusy = false; }
        }

        public async Task UpdateMetadataAsync(GalleryMediaItem item, bool? favorite, string? description)
        {
            if (string.IsNullOrWhiteSpace(item.Media.Id)) return;

            IsBusy = true;
            ErrorMessage = null;
            try
            {
                StatusMessage = "Updating metadata…";
                var result = await _galleryTransferWorkflow.UpdateMetadataAsync(item.Media.Id!, item.Media.MediaType ?? "image", favorite, description).ConfigureAwait(false);
                if (!result.Success)
                    throw new PhoneConnectionException(result.Error ?? "metadata_failed");
                StatusMessage = "Metadata updated.";
                await LoadMediaAsync(SelectedAlbum?.Id).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                ErrorMessage = ex.Message;
            }
            finally { IsBusy = false; }
        }

        public async Task DownloadSelectedAsync(
            IEnumerable<GalleryMediaItem> selected,
            string localFolder)
        {
            var items = selected.ToList();
            if (items.Count == 0) return;

            _transferCts = new CancellationTokenSource();
            var token = _transferCts.Token;
            IsTransferring = true;
            TransferProgress = 0;

            try
            {
                for (int i = 0; i < items.Count; i++)
                {
                    token.ThrowIfCancellationRequested();
                    var item = items[i];
                    var ext = Path.GetExtension(item.Media.Name ?? ".jpg");
                    var localPath = Path.Combine(localFolder, item.Media.Name ?? $"media_{item.Media.Id}{ext}");
                    StatusMessage = $"Downloading {item.Media.Name} ({i + 1}/{items.Count})…";

                    await _galleryTransferWorkflow.DownloadAsync(
                        item.Media.Id!,
                        item.Media.MediaType ?? "image",
                        localPath,
                        progress: new Progress<(long received, long total)>(p =>
                            Application.Current?.Dispatcher?.BeginInvoke(() =>
                            {
                                var fileProgress = p.total > 0 ? (double)p.received / p.total : 0;
                                TransferProgress = (i + fileProgress) / items.Count * 100;
                            })),
                        cancellationToken: token).ConfigureAwait(false);
                }

                StatusMessage = $"Downloaded {items.Count} file(s) to {localFolder}";
                TransferProgress = 100;
            }
            catch (OperationCanceledException)
            {
                StatusMessage = "Download cancelled.";
                TransferProgress = 0;
            }
            catch (Exception ex)
            {
                ErrorMessage = ex.Message;
                TransferProgress = 0;
            }
            finally
            {
                IsTransferring = false;
                _transferCts?.Dispose();
                _transferCts = null;
            }
        }

        public void CancelTransfer() => _transferCts?.Cancel();

        // ── Helpers ────────────────────────────────────────────────────────────────

        private static BitmapImage? JpegToBitmapImage(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0) return null;
            try
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.StreamSource = new MemoryStream(bytes);
                // read into memory before the constructor returns. This prevents
                // Image control from holding onto file handles or MemoryStreams.
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
                bmp.EndInit();

                if (bmp.CanFreeze) bmp.Freeze();
                return bmp;
            }
            catch (Exception ex)
            {
                LogMessages.GalleryJpegDecodeFailed(Logger, ex);
                return null;
            }
        }

        private static Task RunOnUiAsync(Action action)
        {
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher == null || dispatcher.CheckAccess())
            {
                action();
                return Task.CompletedTask;
            }

            return dispatcher.InvokeAsync(action).Task;
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

            _loadCts?.Cancel();
            _loadCts?.Dispose();
            _loadCts = null;
            _transferCts?.Cancel();
            _transferCts?.Dispose();
            _transferCts = null;
            _initializationGate.Dispose();
            GC.SuppressFinalize(this);
        }
    }

    /// <summary>Wraps a <see cref="GalleryMediaDto"/> with observable thumbnail/preview state.</summary>
    public sealed class GalleryMediaItem : INotifyPropertyChanged
    {
        public GalleryMediaDto Media { get; }

        private BitmapImage? _thumbnail;
        public BitmapImage? Thumbnail
        {
            get => _thumbnail;
            set { _thumbnail = value; OnPropertyChanged(nameof(Thumbnail)); OnPropertyChanged(nameof(HasThumbnail)); }
        }

        private BitmapImage? _fullResolution;
        public BitmapImage? FullResolution
        {
            get => _fullResolution;
            set { _fullResolution = value; OnPropertyChanged(nameof(FullResolution)); }
        }

        public bool HasThumbnail => _thumbnail != null;

        private bool _loadError;
        public bool LoadError
        {
            get => _loadError;
            set { _loadError = value; OnPropertyChanged(nameof(LoadError)); }
        }

        private bool _isSelected;
        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected == value) return;
                _isSelected = value;
                OnPropertyChanged(nameof(IsSelected));
            }
        }

        public GalleryMediaItem(GalleryMediaDto media) => Media = media;

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged(string name) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
