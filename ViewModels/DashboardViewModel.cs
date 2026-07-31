using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VcfEditor.Core;
using VcfEditor.Navigation;
using VcfEditor.Services;

namespace VcfEditor.ViewModels;

public sealed partial class DashboardViewModel : ObservableObject, IDisposable
{
    private readonly IContactMetrics _contactMetrics;
    private readonly IDialogService _dialogService;
    private readonly DashboardMetricItem _contactsMetric;
    private readonly DashboardMetricItem _photosMetric;
    private readonly DashboardMetricItem _groupsMetric;
    private bool _disposed;
    private long _storageTotalBytes;
    private long _storageFreeBytes;
    private bool _isStorageLoaded;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PrimaryConnectionActionText))]
    [NotifyCanExecuteChangedFor(nameof(RunPrimaryConnectionActionCommand))]
    private bool _isPhoneConnected;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPhoneBusy))]
    [NotifyPropertyChangedFor(nameof(IsPhoneInError))]
    [NotifyPropertyChangedFor(nameof(PhoneStatusTitle))]
    [NotifyPropertyChangedFor(nameof(PrimaryConnectionActionText))]
    [NotifyCanExecuteChangedFor(nameof(RunPrimaryConnectionActionCommand))]
    private ShellPhoneConnectionState _phoneConnectionState = ShellPhoneConnectionState.Disconnected;

    [ObservableProperty]
    private string _phoneStatusDetail = "Connect the Android companion app to browse files, photos, and backups.";

    [ObservableProperty]
    private bool _supportsFiles;

    [ObservableProperty]
    private bool _supportsGallery;

    [ObservableProperty]
    private bool _supportsBackup;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsRecentActivityEmpty))]
    private bool _hasRecentActivity;

    public DashboardViewModel(IContactMetrics contactMetrics, IDialogService dialogService)
    {
        ArgumentNullException.ThrowIfNull(contactMetrics);
        ArgumentNullException.ThrowIfNull(dialogService);
        _contactMetrics = contactMetrics;
        _dialogService = dialogService;

        _contactsMetric = new DashboardMetricItem("Contacts", "—", "Contact count is not loaded yet.");
        _photosMetric = new DashboardMetricItem("Photos", "—", "Photo count is not loaded yet.");
        _groupsMetric = new DashboardMetricItem("Groups", "—", "Group count is not loaded yet.");
        Metrics = new ObservableCollection<DashboardMetricItem>
        {
            _contactsMetric,
            _photosMetric,
            _groupsMetric
        };
        RecentActivity = new ObservableCollection<DashboardActivityItem>();
        RecentActivity.CollectionChanged += (_, _) => HasRecentActivity = RecentActivity.Count > 0;

        _contactMetrics.PropertyChanged += OnContactMetricsChanged;
        RefreshContactMetric();
    }

    public event Action<PhoneContactsClient, PhoneApiClient>? PhoneConnected;
    public event Action? RefreshContactsRequested;
    public event Action? PhoneDisconnected;
    public event EventHandler? RetryPhoneConnectionRequested;
    public event Action<ShellDestination>? NavigationRequested;

    public ObservableCollection<DashboardMetricItem> Metrics { get; }
    public ObservableCollection<DashboardActivityItem> RecentActivity { get; }

    // Named accessors used by the stat-card row in DashboardView.xaml.
    public DashboardMetricItem ContactsMetric => _contactsMetric;
    public DashboardMetricItem PhotosMetric => _photosMetric;
    public DashboardMetricItem GroupsMetric => _groupsMetric;

    // ── Storage summary ─────────────────────────────────────────────
    /// <summary>Value shown in the 4th (Storage) stat card. "64.0 GB" or "—" when not loaded.</summary>
    public string StorageStatCardValue => _isStorageLoaded && _storageTotalBytes > 0
        ? FormatBytes(_storageTotalBytes) : "—";

    public string StorageAutomationDescription => _isStorageLoaded && _storageTotalBytes > 0
        ? $"Total device storage: {FormatBytes(_storageTotalBytes)}."
        : "Device storage information is not yet loaded.";

    public string StorageTotalText => _isStorageLoaded && _storageTotalBytes > 0
        ? FormatBytes(_storageTotalBytes) : "—";

    public string StorageUsedText => _isStorageLoaded && _storageTotalBytes > 0
        ? FormatBytes(_storageTotalBytes - _storageFreeBytes) : "—";

    public string StorageFreeText => _isStorageLoaded && _storageFreeBytes > 0
        ? FormatBytes(_storageFreeBytes) : "—";

    public double StorageUsedPercent => _storageTotalBytes > 0
        ? ((double)(_storageTotalBytes - _storageFreeBytes) / _storageTotalBytes * 100.0)
        : 0.0;

    public double StorageFreePercent => _storageTotalBytes > 0
        ? ((double)_storageFreeBytes / _storageTotalBytes * 100.0)
        : 0.0;

    public bool IsStorageLoaded => _isStorageLoaded;

    public bool IsPhoneBusy => PhoneConnectionState is ShellPhoneConnectionState.Connecting
        or ShellPhoneConnectionState.Verifying
        or ShellPhoneConnectionState.Reconnecting;
    public bool IsPhoneInError => PhoneConnectionState == ShellPhoneConnectionState.Error;
    public bool IsRecentActivityEmpty => !HasRecentActivity;

    public string PhoneStatusTitle => PhoneConnectionState switch
    {
        ShellPhoneConnectionState.Connected => "Phone connected",
        ShellPhoneConnectionState.Verifying => "Verifying phone",
        ShellPhoneConnectionState.Connecting => "Connecting to phone",
        ShellPhoneConnectionState.Reconnecting => "Reconnecting to phone",
        ShellPhoneConnectionState.Error => "Connection needs attention",
        _ => "No phone connected"
    };

    public string PrimaryConnectionActionText => IsPhoneConnected
        ? "Refresh contacts"
        : IsPhoneInError ? "Retry connection" : "Connect phone";

    public void UpdatePhoneConnection(
        bool isConnected,
        ShellPhoneConnectionState state,
        string? deviceName,
        string? errorMessage)
    {
        IsPhoneConnected = isConnected;
        PhoneConnectionState = state;
        PhoneStatusDetail = state == ShellPhoneConnectionState.Error
            ? string.IsNullOrWhiteSpace(errorMessage) ? "The phone could not be reached. Check the local network and retry." : errorMessage
            : !string.IsNullOrWhiteSpace(deviceName)
                ? deviceName
                : isConnected
                    ? "Connected securely over the local network."
                    : "Connect the Android companion app to browse files, photos, and backups.";
    }

    public void ApplyCapabilities(CapabilityState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        SupportsFiles = state.SupportsFiles;
        SupportsGallery = state.SupportsGallery;
        SupportsBackup = state.SupportsBackup;
    }

    public void ResetCapabilities()
    {
        SupportsFiles = false;
        SupportsGallery = false;
        SupportsBackup = false;
        UpdatePhotoMetric(null, false);
        UpdateGroupMetric(null, false);
    }

    /// <summary>Called by the coordinator when storage data arrives from the phone status payload.</summary>
    public void UpdateStorage(long totalBytes, long freeBytes)
    {
        _storageTotalBytes = totalBytes;
        _storageFreeBytes = freeBytes;
        _isStorageLoaded = totalBytes > 0;
        NotifyStorageChanged();
    }

    /// <summary>Called by the coordinator when the phone disconnects or capabilities are reset.</summary>
    public void ResetStorage()
    {
        _storageTotalBytes = 0;
        _storageFreeBytes = 0;
        _isStorageLoaded = false;
        NotifyStorageChanged();
    }

    private void NotifyStorageChanged()
    {
        OnPropertyChanged(nameof(StorageStatCardValue));
        OnPropertyChanged(nameof(StorageAutomationDescription));
        OnPropertyChanged(nameof(StorageTotalText));
        OnPropertyChanged(nameof(StorageUsedText));
        OnPropertyChanged(nameof(StorageFreeText));
        OnPropertyChanged(nameof(StorageUsedPercent));
        OnPropertyChanged(nameof(StorageFreePercent));
        OnPropertyChanged(nameof(IsStorageLoaded));
    }

    public void UpdatePhotoMetric(int? count, bool isLoaded)
        => UpdateMetric(_photosMetric, count, isLoaded, "photos");

    public void UpdateGroupMetric(int? count, bool isLoaded)
        => UpdateMetric(_groupsMetric, count, isLoaded, "groups");

    public void AddActivity(string message, DateTimeOffset? occurredAt = null)
    {
        if (string.IsNullOrWhiteSpace(message))
            return;

        while (RecentActivity.Count >= 10)
            RecentActivity.RemoveAt(RecentActivity.Count - 1);

        RecentActivity.Insert(0, new DashboardActivityItem(message.Trim(), occurredAt ?? DateTimeOffset.Now));
    }

    [RelayCommand(CanExecute = nameof(CanRunPrimaryConnectionAction))]
    private void RunPrimaryConnectionAction()
    {
        if (IsPhoneConnected)
        {
            RefreshContactsRequested?.Invoke();
            return;
        }

        if (IsPhoneInError)
        {
            RetryPhoneConnectionRequested?.Invoke(this, EventArgs.Empty);
            return;
        }

        var result = _dialogService.ShowConnectPhoneDialog();
        if (result is not null)
            PhoneConnected?.Invoke(result.ContactsClient, result.ApiClient);
    }

    private bool CanRunPrimaryConnectionAction() => !IsPhoneBusy;

    [RelayCommand]
    private void DisconnectPhone()
    {
        if (IsPhoneConnected)
            PhoneDisconnected?.Invoke();
    }

    [RelayCommand]
    private void Navigate(string destination)
    {
        if (Enum.TryParse<ShellDestination>(destination, ignoreCase: true, out var parsed))
            NavigationRequested?.Invoke(parsed);
    }
    [RelayCommand]
    private void ShowDeviceDetails()
        => AddActivity("Viewing device details…");

    private static string FormatBytes(long bytes)
    {
        if (bytes <= 0) return "0 B";
        string[] suffixes = ["B", "KB", "MB", "GB", "TB"];
        int i = 0;
        double value = bytes;
        while (value >= 1024 && i < suffixes.Length - 1)
        {
            value /= 1024;
            i++;
        }
        return $"{value:F1} {suffixes[i]}";
    }

    private void OnContactMetricsChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(IContactMetrics.ContactCount) or nameof(IContactMetrics.IsSourceLoaded))
            RefreshContactMetric();
    }

    private void RefreshContactMetric()
        => UpdateMetric(_contactsMetric, _contactMetrics.ContactCount, _contactMetrics.IsSourceLoaded, "contacts");

    private static void UpdateMetric(DashboardMetricItem metric, int? count, bool isLoaded, string unit)
    {
        metric.Value = isLoaded && count.HasValue
            ? count.Value.ToString("N0", CultureInfo.CurrentCulture)
            : "—";
        metric.AutomationDescription = isLoaded && count.HasValue
            ? $"{count.Value.ToString("N0", CultureInfo.CurrentCulture)} {unit} loaded."
            : $"{unit} are not loaded yet.";
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _contactMetrics.PropertyChanged -= OnContactMetricsChanged;
        GC.SuppressFinalize(this);
    }
}

public sealed partial class DashboardMetricItem : ObservableObject
{
    [ObservableProperty]
    private string _value;

    [ObservableProperty]
    private string _automationDescription;

    public DashboardMetricItem(string label, string value, string automationDescription)
    {
        Label = label;
        _value = value;
        _automationDescription = automationDescription;
    }

    public string Label { get; }
}

public sealed record DashboardActivityItem(string Message, DateTimeOffset OccurredAt)
{
    public string TimestampText => OccurredAt.ToLocalTime().ToString("g", CultureInfo.CurrentCulture);
    public string AutomationText => $"{Message}. {TimestampText}.";
}
