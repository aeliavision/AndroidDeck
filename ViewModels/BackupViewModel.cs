using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using VcfEditor.Core;
using VcfEditor.Features.Backup;
using VcfEditor.Helpers;
using VcfEditor.Services;
using VcfEditor.Views;
using Microsoft.Extensions.Logging;

namespace VcfEditor.ViewModels
{
    /// <summary>
    /// Binds to BackupView.xaml. Uses BackupApi for all device communication.
    /// </summary>
    public class BackupViewModel : INotifyPropertyChanged, IAsyncInitializable, IDisposable
    {
        private static readonly ILogger Logger = AppLoggerFactory.CreateLogger(nameof(BackupViewModel));

        private readonly IBackupWorkflow _backupWorkflow;
        private readonly IRestoreWorkflow _restoreWorkflow;
        private readonly IBackupHistoryService _backupHistoryService;
        private readonly IBackupArchiveService _backupArchiveService;
        private readonly IDialogService _dialogService;
        private readonly SemaphoreSlim _initializationGate = new(1, 1);

        // ── INotifyPropertyChanged ────────────────────────────────────────────────
        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([System.Runtime.CompilerServices.CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        // ── Observable state ──────────────────────────────────────────────────

        private bool _isInitialized;
        private bool _lastHistoryRefreshSucceeded;
        public bool IsInitialized
        {
            get => _isInitialized;
            private set
            {
                if (_isInitialized == value) return;
                _isInitialized = value;
                OnPropertyChanged();
            }
        }

        private ObservableCollection<BackupHistoryEntry> _history = new();
        public ObservableCollection<BackupHistoryEntry> History
        {
            get => _history;
            private set
            {
                _history = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasHistory));
            }
        }
        public bool HasHistory => History.Count > 0;

        private ObservableCollection<RestoreItemOutcome> _restoreItemOutcomes = new();
        public ObservableCollection<RestoreItemOutcome> RestoreItemOutcomes
        {
            get => _restoreItemOutcomes;
            private set
            {
                _restoreItemOutcomes = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasRestoreItemOutcomes));
            }
        }
        public bool HasRestoreItemOutcomes => RestoreItemOutcomes.Count > 0;

        private static string FormatBytes(long bytes) => bytes switch
        {
            >= 1_073_741_824 => $"{bytes / 1_073_741_824.0:F1} GB",
            >= 1_048_576 => $"{bytes / 1_048_576.0:F1} MB",
            >= 1_024 => $"{bytes / 1_024.0:F1} KB",
            _ => $"{bytes} B"
        };

        private double _progress;
        public  double  Progress
        {
            get => _progress;
            private set { _progress = value; OnPropertyChanged(); }
        }

        public string ProgressPercentText => $"{(int)Math.Round(Progress * 100)}%";

        private string _operationTitle = "";
        public string OperationTitle
        {
            get => _operationTitle;
            private set { _operationTitle = value; OnPropertyChanged(); }
        }

        private string _operationDetail = "";
        public string OperationDetail
        {
            get => _operationDetail;
            private set { _operationDetail = value; OnPropertyChanged(); }
        }

        private int _operationStage;
        public int OperationStage
        {
            get => _operationStage;
            private set
            {
                if (_operationStage == value) return;
                _operationStage = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsStage1Active));
                OnPropertyChanged(nameof(IsStage2Active));
                OnPropertyChanged(nameof(IsStage3Active));
                OnPropertyChanged(nameof(IsStage1Complete));
                OnPropertyChanged(nameof(IsStage2Complete));
                OnPropertyChanged(nameof(IsStage3Complete));
            }
        }

        public bool IsStage1Active => IsBusy && OperationStage == 1;
        public bool IsStage2Active => IsBusy && OperationStage == 2;
        public bool IsStage3Active => IsBusy && OperationStage == 3;
        public bool IsStage1Complete => IsBusy && OperationStage > 1;
        public bool IsStage2Complete => IsBusy && OperationStage > 2;
        public bool IsStage3Complete => IsBusy && OperationStage > 3;

        private void UpdateRestoreItemOutcomes(RestoreStatusResponse status)
        {
            if (status.ItemResults is not { Count: > 0 }) return;
            RestoreItemOutcomes = new ObservableCollection<RestoreItemOutcome>(status.ItemResults);
        }

        private void SetStage(int stage, string title, string detail)
        {
            OperationStage = stage;
            OperationTitle = title;
            OperationDetail = detail;
        }

        private void ResetOperationUi()
        {
            OperationStage = 0;
            OperationTitle = string.Empty;
            OperationDetail = string.Empty;
            OnPropertyChanged(nameof(ProgressPercentText));
        }

        private string _statusMessage = "Ready";
        public  string  StatusMessage
        {
            get => _statusMessage;
            private set { _statusMessage = value; OnPropertyChanged(); }
        }

        private bool _isBusy;
        public  bool  IsBusy
        {
            get => _isBusy;
            private set
            {
                _isBusy = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsIdle));
                OnPropertyChanged(nameof(IsStage1Active));
                OnPropertyChanged(nameof(IsStage2Active));
                OnPropertyChanged(nameof(IsStage3Active));
                OnPropertyChanged(nameof(IsStage1Complete));
                OnPropertyChanged(nameof(IsStage2Complete));
                OnPropertyChanged(nameof(IsStage3Complete));
                NotifyCommandStateChanged();
            }
        }

        private async Task DownloadHistoryBackupAsync(BackupHistoryEntry? entry)
        {
            if (entry == null) return;

            var destPath = _dialogService.ShowSaveBackupArchiveDialog();
            if (string.IsNullOrWhiteSpace(destPath)) return;

            _cts = new CancellationTokenSource();
            IsBusy = true;
            Progress = 0;
            OnPropertyChanged(nameof(ProgressPercentText));

            try
            {
                SetStage(2, "Downloading backup", "Fetching status…");
                StatusMessage = "Fetching backup status…";
                var status = await _backupWorkflow.GetStatusAsync(entry.BackupId, _cts.Token);

                SetStage(2, "Downloading backup", "Downloading archive…");
                StatusMessage = "Downloading archive…";
                await _backupWorkflow.DownloadAsync(
                    entry.BackupId,
                    destPath,
                    status.ArchiveSize,
                    onProgress: new Progress<double>(p => { Progress = p; OnPropertyChanged(nameof(ProgressPercentText)); }),
                    ct: _cts.Token);

                SetStage(3, "Download complete", Path.GetFileName(destPath));
                StatusMessage = $"Download complete — saved to {Path.GetFileName(destPath)}";
                Progress = 1.0;
                OnPropertyChanged(nameof(ProgressPercentText));
            }
            catch (OperationCanceledException)
            {
                StatusMessage = "Download cancelled.";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Download failed: {ex.Message}";
                LogMessages.BackupHistoryDownloadFailed(Logger, ex);
            }
            finally
            {
                IsBusy = false;
                ResetOperationUi();
                _cts?.Dispose();
                _cts = null;
            }
        }

        private async Task RestoreHistoryBackupAsync(BackupHistoryEntry? entry)
        {
            if (entry == null) return;

            var previewRows = new System.Collections.Generic.List<(string Label, string Value)>
            {
                ("Backup ID", entry.BackupId),
                ("Created", entry.CreatedAtDisplay),
                ("Types", entry.TypesDisplay),
                ("Items", entry.ItemCount.ToString(CultureInfo.InvariantCulture)),
                ("Archive size", entry.ArchiveSizeDisplay)
            };
            if (!_dialogService.ShowRestorePreviewDialog(
                "Confirm Restore",
                "This will download the selected backup from the phone and restore it back onto the device.",
                previewRows,
                primaryActionText: "Start restore"))
                return;

            var tempPath = Path.Combine(
                Path.GetTempPath(),
                $"AndroidDeck_{entry.BackupId}_{Guid.NewGuid():N}.deckbak");

            _cts = new CancellationTokenSource();
            RestoreItemOutcomes = new ObservableCollection<RestoreItemOutcome>();
            IsBusy = true;
            Progress = 0;
            OnPropertyChanged(nameof(ProgressPercentText));

            try
            {
                SetStage(1, "Preparing restore", "Fetching backup status…");
                StatusMessage = "Fetching backup status…";
                var status = await _backupWorkflow.GetStatusAsync(entry.BackupId, _cts.Token);

                SetStage(1, "Preparing restore", "Downloading archive…");
                StatusMessage = "Downloading archive…";
                await _backupWorkflow.DownloadAsync(
                    entry.BackupId,
                    tempPath,
                    status.ArchiveSize,
                    onProgress: new Progress<double>(p => { Progress = p * 0.5; OnPropertyChanged(nameof(ProgressPercentText)); }),
                    ct: _cts.Token);

                SetStage(2, "Uploading archive", "Uploading backup archive to device…");
                StatusMessage = "Uploading backup archive to device…";
                var started = await _restoreWorkflow.StartAsync(
                    tempPath,
                    onProgress: new Progress<double>(p => { Progress = 0.5 + p * 0.25; OnPropertyChanged(nameof(ProgressPercentText)); }),
                    ct: _cts.Token);

                var finalStatus = await _restoreWorkflow.WaitUntilCompletedAsync(
                    started.RestoreId,
                    progress: new Progress<RestoreStatusResponse>(status =>
                    {
                        Progress = 0.75 + status.Progress * 0.25;
                        OnPropertyChanged(nameof(ProgressPercentText));
                        SetStage(3, "Restoring", $"{status.Phase} — restored={status.RestoredItems} skipped={status.SkippedItems} failed={status.FailedItems}");
                        StatusMessage = $"Restoring: {status.Phase} — restored={status.RestoredItems} skipped={status.SkippedItems} failed={status.FailedItems}";
                        UpdateRestoreItemOutcomes(status);
                    }),
                    cancellationToken: _cts.Token);
                UpdateRestoreItemOutcomes(finalStatus);
                SetStage(3, "Restore complete", $"Restored {finalStatus.RestoredItems} · Skipped {finalStatus.SkippedItems} · Failed {finalStatus.FailedItems}");
                LastRestoreSummary = $"Restored {finalStatus.RestoredItems}; skipped/conflicts {finalStatus.SkippedItems}; failed {finalStatus.FailedItems}.";
                StatusMessage = $"Restore complete — {finalStatus.RestoredItems} items restored, {finalStatus.SkippedItems} skipped, {finalStatus.FailedItems} failed.";
                Progress = 1.0;
                OnPropertyChanged(nameof(ProgressPercentText));

                var doneRows = new System.Collections.Generic.List<(string Label, string Value)>
                {
                    ("Backup ID", entry.BackupId),
                    ("Restored", finalStatus.RestoredItems.ToString(CultureInfo.InvariantCulture)),
                    ("Skipped", finalStatus.SkippedItems.ToString(CultureInfo.InvariantCulture)),
                    ("Failed", finalStatus.FailedItems.ToString(CultureInfo.InvariantCulture))
                };
                _dialogService.ShowBackupCompletionDialog(
                    "Restore complete",
                    "The restore finished. Review the results below.",
                    doneRows);
            }
            catch (OperationCanceledException)
            {
                StatusMessage = "Restore cancelled.";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Restore failed: {ex.Message}";
                LogMessages.BackupHistoryRestoreFailed(Logger, ex);
            }
            finally
            {
                _backupArchiveService.TryDeleteTemporaryFile(tempPath);

                IsBusy = false;
                ResetOperationUi();
                _cts?.Dispose();
                _cts = null;
            }
        }

        public bool IsIdle => !IsBusy;

        // ── Backup type toggles ───────────────────────────────────────────────

        private bool _backupContacts = true;
        public bool BackupContacts
        {
            get => _backupContacts;
            set { _backupContacts = value; OnPropertyChanged(); NotifyScopeSummary(); }
        }

        private bool _backupGallery = true;
        public bool BackupGallery
        {
            get => _backupGallery;
            set { _backupGallery = value; OnPropertyChanged(); NotifyScopeSummary(); }
        }

        private bool _backupFiles = true;
        public bool BackupFiles
        {
            get => _backupFiles;
            set { _backupFiles = value; OnPropertyChanged(); NotifyScopeSummary(); }
        }

        private bool _encryptOnDevice = true;
        public bool EncryptOnDevice
        {
            get => _encryptOnDevice;
            set
            {
                _encryptOnDevice = value;
                if (!_encryptOnDevice && EncryptLocally)
                    EncryptLocally = false;
                OnPropertyChanged();
                OnPropertyChanged(nameof(EncryptionStatus));
            }
        }

        private bool _encryptLocally;
        public bool EncryptLocally
        {
            get => _encryptLocally;
            set
            {
                _encryptLocally = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(EncryptionStatus));
            }
        }

        public string ScopeSummary
        {
            get
            {
                var selected = new System.Collections.Generic.List<string>();
                if (BackupContacts) selected.Add("Contacts");
                if (BackupGallery) selected.Add("Gallery");
                if (BackupFiles) selected.Add("Files");
                return selected.Count == 0 ? "Nothing selected" : string.Join(", ", selected);
            }
        }

        public string EncryptionStatus => EncryptLocally
            ? "Encrypted on the phone and again on this PC"
            : EncryptOnDevice ? "Encrypted on the phone" : "Encryption disabled";
        [SuppressMessage("Performance", "CA1822:Mark members as static", Justification = "WPF binds these properties through the view-model instance.")]
        public string PermissionSummary => "Required phone permissions are checked before the operation starts.";
        [SuppressMessage("Performance", "CA1822:Mark members as static", Justification = "WPF binds these properties through the view-model instance.")]
        public string EstimatedSizeText => "Estimated size is calculated by the phone during preparation.";
        [SuppressMessage("Performance", "CA1822:Mark members as static", Justification = "WPF binds these properties through the view-model instance.")]
        public string DestinationSummary => "You choose the destination file when Create backup starts.";
        [SuppressMessage("Performance", "CA1822:Mark members as static", Justification = "WPF binds these properties through the view-model instance.")]
        public string RestoreWarning => "Restore can replace or skip existing data. Review the archive summary before continuing.";

        private string _lastRestoreSummary = "No restore has run in this session.";
        public string LastRestoreSummary
        {
            get => _lastRestoreSummary;
            private set { _lastRestoreSummary = value; OnPropertyChanged(); }
        }

        private void NotifyScopeSummary()
            => OnPropertyChanged(nameof(ScopeSummary));

        private ObservableCollection<string> _defaultFilePaths = new();
        public ObservableCollection<string> DefaultFilePaths
        {
            get => _defaultFilePaths;
            private set
            {
                _defaultFilePaths = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(BackupFilesLabel));
                NotifyScopeSummary();
            }
        }

        public string BackupFilesLabel
        {
            get
            {
                if (DefaultFilePaths.Count == 0)
                    return "Files";
                var names = DefaultFilePaths
                    .Select(p => p.Replace('\\', '/').TrimEnd('/').Split('/').LastOrDefault() ?? p)
                    .Distinct()
                    .ToList();
                return names.Count > 0
                    ? $"Files ({string.Join(" + ", names)})"
                    : "Files";
            }
        }

        // ── Active operation tracking ─────────────────────────────────────────

        private CancellationTokenSource? _cts;
        private bool _disposed;

        // ── Commands ──────────────────────────────────────────────────────────

        public IAsyncRelayCommand CreateBackupCommand { get; }
        public IAsyncRelayCommand RestoreBackupCommand { get; }
        public IAsyncRelayCommand RefreshHistoryCommand { get; }
        public IRelayCommand CancelCommand { get; }

        public IAsyncRelayCommand<BackupHistoryEntry?> DownloadHistoryBackupCommand { get; }
        public IAsyncRelayCommand<BackupHistoryEntry?> RestoreHistoryBackupCommand { get; }

        public BackupViewModel(
            IBackupWorkflow backupWorkflow,
            IRestoreWorkflow restoreWorkflow,
            IBackupHistoryService backupHistoryService,
            IBackupArchiveService backupArchiveService,
            IDialogService dialogService)
        {
            ArgumentNullException.ThrowIfNull(backupWorkflow);
            ArgumentNullException.ThrowIfNull(restoreWorkflow);
            ArgumentNullException.ThrowIfNull(backupHistoryService);
            ArgumentNullException.ThrowIfNull(backupArchiveService);
            ArgumentNullException.ThrowIfNull(dialogService);
            _backupWorkflow = backupWorkflow;
            _restoreWorkflow = restoreWorkflow;
            _backupHistoryService = backupHistoryService;
            _backupArchiveService = backupArchiveService;
            _dialogService = dialogService;

            CreateBackupCommand = new AsyncRelayCommand(CreateBackupAsync, () => IsIdle);
            RestoreBackupCommand = new AsyncRelayCommand(RestoreBackupAsync, () => IsIdle);
            RefreshHistoryCommand = new AsyncRelayCommand(
                () => RefreshHistoryAsync(),
                () => IsIdle);
            CancelCommand = new RelayCommand(CancelActiveOperation, () => IsBusy);

            DownloadHistoryBackupCommand = new AsyncRelayCommand<BackupHistoryEntry?>(
                DownloadHistoryBackupAsync,
                entry => IsIdle && entry is not null);
            RestoreHistoryBackupCommand = new AsyncRelayCommand<BackupHistoryEntry?>(
                RestoreHistoryBackupAsync,
                entry => IsIdle && entry is not null);
        }

        private void CancelActiveOperation() => _cts?.Cancel();

        private void NotifyCommandStateChanged()
        {
            CreateBackupCommand.NotifyCanExecuteChanged();
            RestoreBackupCommand.NotifyCanExecuteChanged();
            RefreshHistoryCommand.NotifyCanExecuteChanged();
            CancelCommand.NotifyCanExecuteChanged();
            DownloadHistoryBackupCommand.NotifyCanExecuteChanged();
            RestoreHistoryBackupCommand.NotifyCanExecuteChanged();
        }

        private bool _supportsIncremental;
        public bool SupportsIncremental
        {
            get => _supportsIncremental;
            private set { _supportsIncremental = value; OnPropertyChanged(); OnPropertyChanged(nameof(ShowIncremental)); }
        }

        public bool ShowIncremental => SupportsIncremental;

        private bool _incremental;
        public bool Incremental
        {
            get => _incremental;
            set { _incremental = value; OnPropertyChanged(); }
        }

        public async Task InitializeAsync(CancellationToken cancellationToken = default)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (IsInitialized) return;

            await _initializationGate.WaitAsync(cancellationToken);
            try
            {
                if (IsInitialized) return;
                await LoadManifestAsync(cancellationToken);
                await RefreshHistoryAsync(cancellationToken);
                IsInitialized = _lastHistoryRefreshSucceeded;
            }
            finally
            {
                _initializationGate.Release();
            }
        }

        public Task InitialiseAsync() => InitializeAsync();

        private async Task LoadManifestAsync(CancellationToken cancellationToken)
        {
            try
            {
                var manifest = await _backupWorkflow.GetManifestAsync(cancellationToken).ConfigureAwait(false);
                var paths = manifest.DefaultPaths ?? new System.Collections.Generic.List<string>();
                var dispatcher = System.Windows.Application.Current?.Dispatcher;
                if (dispatcher == null) return;
                await dispatcher.InvokeAsync(() =>
                {
                    DefaultFilePaths = new ObservableCollection<string>(paths);
                    SupportsIncremental = manifest.SupportsIncremental;
                    if (!SupportsIncremental) Incremental = false;
                });
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                LogMessages.BackupManifestLoadFailed(Logger, ex);
                // Manifest is optional; fall back to hardcoded label.
                if (StatusMessage == "Ready")
                    StatusMessage = "Backup manifest unavailable (using defaults).";
            }
        }

        // ── Create backup ─────────────────────────────────────────────────────

        private async Task CreateBackupAsync()
        {
            var types = new System.Collections.Generic.List<string>();
            if (BackupContacts) types.Add("contacts");
            if (BackupGallery)  types.Add("gallery");
            if (BackupFiles)    types.Add("files");

            if (types.Count == 0)
            {
                StatusMessage = "Select at least one backup type.";
                return;
            }

            var destPath = _dialogService.ShowSaveBackupArchiveDialog();
            if (string.IsNullOrWhiteSpace(destPath)) return;

            string? password = null;
            if (EncryptLocally)
            {
                password = _dialogService.ShowEncryptBackupPasswordDialog();
                if (string.IsNullOrEmpty(password)) return;
            }

            var summaryRows = new System.Collections.Generic.List<(string Label, string Value)>
            {
                ("Types", string.Join(", ", types)),
                ("Encrypt on device", EncryptOnDevice ? "Yes" : "No"),
                ("Encrypt locally", EncryptLocally ? "Yes" : "No"),
                ("Incremental", SupportsIncremental && Incremental ? "Yes" : "No"),
                ("Destination", destPath)
            };

            if (!_dialogService.ShowBackupSummaryDialog("Confirm Backup", summaryRows))
                return;

            string? temporaryDownloadPath = null;
            _cts = new CancellationTokenSource();
            IsBusy = true;
            Progress = 0;
            OnPropertyChanged(nameof(ProgressPercentText));

            try
            {
                // 1. Kick off backup on device
                SetStage(1, "Creating backup", "Starting backup on device…");
                StatusMessage = "Starting backup on device…";
                var created = await _backupWorkflow.CreateAsync(
                    types,
                    encrypt: EncryptOnDevice,
                    incremental: SupportsIncremental && Incremental,
                    ct: _cts.Token);
                var backupId = created.BackupId;

                // 2. Poll for completion through the focused workflow service.
                var finalStatus = await _backupWorkflow.WaitUntilReadyAsync(
                    backupId,
                    progress: new Progress<BackupStatusResponse>(status =>
                    {
                        Progress = status.Progress;
                        OnPropertyChanged(nameof(ProgressPercentText));
                        SetStage(1, "Creating backup", $"{status.Phase} — {status.ProcessedItems}/{status.ItemCount}");
                        StatusMessage = $"Phase: {status.Phase} — {status.CurrentItem} " +
                                        $"({status.ProcessedItems}/{status.ItemCount})";
                    }),
                    cancellationToken: _cts.Token);

                // 3. Download archive
                SetStage(2, "Downloading archive", "Preparing download…");
                StatusMessage = "Downloading archive…";
                if (Logger.IsEnabled(LogLevel.Information))
                {
                    LogMessages.BackupDownloadStarting(
                        Logger,
                        backupId,
                        finalStatus.ArchiveSize,
                        string.Join(',', types));
                }

                if (!EncryptLocally)
                {
                    await _backupWorkflow.DownloadAsync(
                        backupId, destPath, finalStatus.ArchiveSize,
                        onProgress: new Progress<double>(p => { Progress = p; OnPropertyChanged(nameof(ProgressPercentText)); }),
                        ct: _cts.Token);
                }
                else
                {
                    temporaryDownloadPath = destPath + ".partial";
                    await _backupWorkflow.DownloadAsync(
                        backupId, temporaryDownloadPath, finalStatus.ArchiveSize,
                        onProgress: new Progress<double>(p => { Progress = p * 0.7; OnPropertyChanged(nameof(ProgressPercentText)); }),
                        ct: _cts.Token);

                    SetStage(3, "Finalizing", "Encrypting locally…");
                    StatusMessage = "Encrypting locally…";
                    await _backupArchiveService.EncryptAsync(temporaryDownloadPath, destPath, password!, new Progress<double>(p => { Progress = 0.7 + p * 0.3; OnPropertyChanged(nameof(ProgressPercentText)); }), _cts.Token);
                }

                SetStage(3, "Backup complete", Path.GetFileName(destPath));
                StatusMessage = $"Backup complete — saved to {Path.GetFileName(destPath)}";
                Progress = 1.0;
                OnPropertyChanged(nameof(ProgressPercentText));
                await RefreshHistoryAsync();

                try
                {
                    var archiveBytes = _backupArchiveService.GetFileSize(destPath);

                    var completionRows = new System.Collections.Generic.List<(string Label, string Value)>
                    {
                        ("Saved as", Path.GetFileName(destPath)),
                        ("Location", destPath),
                        ("Types", string.Join(", ", types)),
                        ("Device archive size", FormatBytes(finalStatus.ArchiveSize)),
                        ("Downloaded file size", archiveBytes > 0 ? FormatBytes(archiveBytes) : string.Empty)
                    };

                    _dialogService.ShowBackupCompletionDialog(
                        "Backup complete",
                        "Your backup has been saved to your computer.",
                        completionRows);
                }
                catch (Exception ex)
                {
                    LogMessages.BackupDialogFailed(Logger, ex);
                }
            }
            catch (TaskCanceledException ex) when (_cts?.IsCancellationRequested == false)
            {
                StatusMessage = $"Backup failed: request timed out ({ex.Message})";
                LogMessages.BackupCreateTimedOut(Logger, ex);
            }
            catch (OperationCanceledException)
            {
                StatusMessage = "Backup cancelled.";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Backup failed: {ex.Message}";
                LogMessages.BackupCreateFailed(Logger, ex);
            }
            finally
            {
                _backupArchiveService.TryDeleteTemporaryFile(temporaryDownloadPath);
                IsBusy = false;
                ResetOperationUi();
                _cts?.Dispose();
                _cts = null;
            }
        }

        // ── Restore backup ────────────────────────────────────────────────────

        private async Task RestoreBackupAsync()
        {
            var archivePath = _dialogService.ShowOpenBackupArchiveDialog();
            if (string.IsNullOrWhiteSpace(archivePath)) return;

            try
            {
                var previewRows = _backupArchiveService.BuildPreviewRows(archivePath);
                if (!_dialogService.ShowRestorePreviewDialog(
                    "Confirm Restore",
                    "This will upload the selected backup archive and restore it on your phone.",
                    previewRows,
                    primaryActionText: "Start restore"))
                    return;
            }
            catch (Exception ex)
            {
                LogMessages.BackupPreviewFailed(Logger, ex, archivePath);
                _dialogService.ShowError(
                    $"The backup archive could not be inspected: {ex.Message}",
                    "Restore preview failed");
                return;
            }

            string? tempDecrypted = null;
            _cts = new CancellationTokenSource();
            RestoreItemOutcomes = new ObservableCollection<RestoreItemOutcome>();
            IsBusy = true;
            Progress = 0;
            OnPropertyChanged(nameof(ProgressPercentText));

            try
            {
                SetStage(1, "Preparing restore", "Preparing archive…");

                // If this is a locally-encrypted archive (premium), decrypt to temp before upload.
                if (_backupArchiveService.IsLocallyEncrypted(archivePath))
                {
                    tempDecrypted = Path.Combine(Path.GetTempPath(), "AndroidDeck_Decrypted_" + Guid.NewGuid().ToString("N") + ".deckbak");
                    SetStage(1, "Preparing restore", "Decrypting locally…");
                    StatusMessage = "Decrypting locally…";
                    var decryptPassword = _dialogService.ShowDecryptBackupPasswordDialog();
                    if (string.IsNullOrEmpty(decryptPassword)) return;
                    await _backupArchiveService.DecryptAsync(archivePath, tempDecrypted, decryptPassword,
                        new Progress<double>(p => { Progress = p * 0.5; OnPropertyChanged(nameof(ProgressPercentText)); }), _cts.Token);
                    archivePath = tempDecrypted;
                }

                // 1. Upload archive to device
                SetStage(2, "Uploading archive", "Uploading backup archive to device…");
                StatusMessage = "Uploading backup archive to device…";
                var started = await _restoreWorkflow.StartAsync(
                    archivePath,
                    onProgress: new Progress<double>(p => { Progress = p * 0.5; OnPropertyChanged(nameof(ProgressPercentText)); }),   // upload = 0–50%
                    ct: _cts.Token);

                // 2. Poll restore progress through the focused workflow service.
                var finalStatus = await _restoreWorkflow.WaitUntilCompletedAsync(
                    started.RestoreId,
                    progress: new Progress<RestoreStatusResponse>(status =>
                    {
                        Progress = 0.5 + status.Progress * 0.5;
                        OnPropertyChanged(nameof(ProgressPercentText));
                        SetStage(3, "Restoring", $"{status.Phase} — restored={status.RestoredItems} skipped={status.SkippedItems} failed={status.FailedItems}");
                        StatusMessage = $"Restoring: {status.Phase} — " +
                                        $"restored={status.RestoredItems} " +
                                        $"skipped={status.SkippedItems} " +
                                        $"failed={status.FailedItems}";
                        UpdateRestoreItemOutcomes(status);
                    }),
                    cancellationToken: _cts.Token);
                StatusMessage = $"Restore complete — {finalStatus.RestoredItems} items restored, " +
                                $"{finalStatus.SkippedItems} skipped, {finalStatus.FailedItems} failed.";
                Progress = 1.0;
                OnPropertyChanged(nameof(ProgressPercentText));
                UpdateRestoreItemOutcomes(finalStatus);
                SetStage(3, "Restore complete", $"Restored {finalStatus.RestoredItems} · Skipped {finalStatus.SkippedItems} · Failed {finalStatus.FailedItems}");
                LastRestoreSummary = $"Restored {finalStatus.RestoredItems}; skipped/conflicts {finalStatus.SkippedItems}; failed {finalStatus.FailedItems}.";

                try
                {
                    var doneRows = new System.Collections.Generic.List<(string Label, string Value)>
                    {
                        ("Restored", finalStatus.RestoredItems.ToString(CultureInfo.InvariantCulture)),
                        ("Skipped", finalStatus.SkippedItems.ToString(CultureInfo.InvariantCulture)),
                        ("Failed", finalStatus.FailedItems.ToString(CultureInfo.InvariantCulture))
                    };
                    _dialogService.ShowBackupCompletionDialog(
                        "Restore complete",
                        "The restore finished. Review the results below.",
                        doneRows);
                }
                catch (Exception ex)
                {
                    LogMessages.BackupDialogFailed(Logger, ex);
                }
            }
            catch (OperationCanceledException)
            {
                StatusMessage = "Restore cancelled.";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Restore failed: {ex.Message}";
                LogMessages.BackupRestoreFailed(Logger, ex);
            }
            finally
            {
                _backupArchiveService.TryDeleteTemporaryFile(tempDecrypted);
                IsBusy = false;
                ResetOperationUi();
                _cts?.Dispose();
                _cts = null;
            }
        }

        // ── History ───────────────────────────────────────────────────────────

        public async Task RefreshHistoryAsync(CancellationToken cancellationToken = default)
        {
            _lastHistoryRefreshSucceeded = false;
            try
            {
                var entries = await _backupHistoryService.GetHistoryAsync(cancellationToken).ConfigureAwait(false);
                var ordered = entries.OrderByDescending(entry => entry.CreatedAt).ToList();
                var dispatcher = System.Windows.Application.Current?.Dispatcher;
                if (dispatcher is null)
                {
                    History = new ObservableCollection<BackupHistoryEntry>(ordered);
                    _lastHistoryRefreshSucceeded = true;
                    return;
                }

                await dispatcher.InvokeAsync(() =>
                    History = new ObservableCollection<BackupHistoryEntry>(ordered));
                _lastHistoryRefreshSucceeded = true;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                LogMessages.BackupHistoryRefreshFailed(Logger, ex);
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            _cts?.Cancel();
            _cts?.Dispose();
            _cts = null;
            _initializationGate.Dispose();

            GC.SuppressFinalize(this);
        }
    }
}
