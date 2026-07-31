using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using VcfEditor.Core;
using VcfEditor.Helpers;
using VcfEditor.Navigation;
using VcfEditor.Services;

namespace VcfEditor.ViewModels;

public sealed partial class ShellWindowViewModel : ObservableObject, IDisposable
{
    public const double ExpandedSidebarWidth = 248;
    public const double CompactSidebarWidth = 72;
    public const double OverlaySidebarWidth = 296;

    private readonly INavigationService _navigationService;
    private readonly IPageFactory _pageFactory;
    private readonly IContactMetrics _contactMetrics;
    private readonly ILogger<ShellWindowViewModel> _logger;
    private readonly DashboardViewModel _dashboardViewModel;
    private readonly Dictionary<ShellDestination, ShellNavigationItemViewModel> _itemsByDestination;
    private readonly Dictionary<string, ShellNavigationItemViewModel> _itemsByKey;
    private CancellationTokenSource? _navigationCancellation;
    private bool _synchronizingSelection;
    private bool _disposed;
    private double _windowWidth = ShellLayoutPolicy.ExpandedMinimumWidth;

    private ShellNavigationItemViewModel? _selectedNavigationItem;
    private object? _currentContent;
    private string _currentTitle = "Dashboard";
    private ShellDestination _currentDestination = ShellDestination.Dashboard;
    private ShellLayoutMode _layoutMode = ShellLayoutMode.Expanded;
    private ShellLayoutMode _preferredDesktopSidebarMode = ShellLayoutMode.Expanded;
    private bool _isOverlayOpen;
    private bool _isPhoneConnected;
    private ShellPhoneConnectionState _phoneConnectionState = ShellPhoneConnectionState.Disconnected;
    private string? _phoneDeviceName;
    private string? _phoneErrorMessage;
    private string? _capabilityError;
    private bool _supportsFiles;
    private bool _supportsGallery;
    private bool _supportsBackup;
    private bool _requiresAllFilesAccess;
    private bool _requiresMediaPermissions;
    private int _dashboardContactCount;
    private int _dashboardPhotoCount;
    private int _dashboardGroupCount;
    private string _navigationAnnouncement = "Dashboard selected.";
    private bool _isSearchOpen;

    public ShellWindowViewModel(
        IShellNavigationRegistry navigationRegistry,
        INavigationService navigationService,
        IPageFactory pageFactory,
        IContactMetrics contactMetrics,
        DashboardViewModel dashboardViewModel,
        SettingsViewModel settingsViewModel,
        ILogger<ShellWindowViewModel> logger)
    {
        ArgumentNullException.ThrowIfNull(navigationRegistry);
        ArgumentNullException.ThrowIfNull(navigationService);
        ArgumentNullException.ThrowIfNull(pageFactory);
        ArgumentNullException.ThrowIfNull(contactMetrics);
        ArgumentNullException.ThrowIfNull(dashboardViewModel);
        ArgumentNullException.ThrowIfNull(logger);

        _navigationService = navigationService;
        _pageFactory = pageFactory;
        _contactMetrics = contactMetrics;
        _dashboardViewModel = dashboardViewModel;
        _logger = logger;

        var items = navigationRegistry.Definitions
            .OrderBy(definition => definition.GroupOrder)
            .ThenBy(definition => definition.ItemOrder)
            .Select(definition => new ShellNavigationItemViewModel(definition))
            .ToList();

        NavigationItems = new ObservableCollection<ShellNavigationItemViewModel>(items);
        _itemsByDestination = items.ToDictionary(item => item.Destination);
        _itemsByKey = items.ToDictionary(item => item.Key, StringComparer.OrdinalIgnoreCase);

        RecentActivity = new ObservableCollection<string>();
        AddRecentActivity("App opened");

        _contactMetrics.PropertyChanged += OnContactMetricsPropertyChanged;
        _pageFactory.MetricsChanged += OnPageFactoryMetricsChanged;
        _navigationService.Navigated += OnNavigated;
        settingsViewModel.CompactSidebarChanged += ApplyCompactSidebarPreference;

        DashboardContactCount = _contactMetrics.ContactCount;
        RefreshNavigationAvailability();
        RefreshNavigationBadges();
    }


    public event EventHandler? RetryConnectionRequested;

    public ObservableCollection<ShellNavigationItemViewModel> NavigationItems { get; }
    public ObservableCollection<string> RecentActivity { get; }

    public ShellNavigationItemViewModel? SelectedNavigationItem
    {
        get => _selectedNavigationItem;
        set
        {
            if (!SetProperty(ref _selectedNavigationItem, value) || value is null || _synchronizingSelection)
                return;

            _ = NavigateToAsync(value.Destination);
        }
    }

    public object? CurrentContent
    {
        get => _currentContent;
        private set => SetProperty(ref _currentContent, value);
    }

    public string CurrentTitle
    {
        get => _currentTitle;
        private set => SetProperty(ref _currentTitle, value);
    }

    /// <summary>Per-destination subtitle shown in the top header bar below the page title.</summary>
    public string CurrentSubtitle => CurrentDestination switch
    {
        ShellDestination.Dashboard => "Your phone, activity, and most-used actions in one place.",
        ShellDestination.Contacts => "Manage, import, and export your Android contacts.",
        ShellDestination.FileBrowser => "Browse and transfer files from your Android device.",
        ShellDestination.Gallery => "View and download photos and videos from your device.",
        ShellDestination.Backup => "Back up and restore your Android contacts and data.",
        ShellDestination.Settings => "Configure AndroidDeck preferences and connection settings.",
        _ => string.Empty
    };

    public bool HasCurrentSubtitle => !string.IsNullOrEmpty(CurrentSubtitle);

    public ShellDestination CurrentDestination
    {
        get => _currentDestination;
        private set => SetProperty(ref _currentDestination, value);
    }

    public ShellLayoutMode LayoutMode
    {
        get => _layoutMode;
        private set
        {
            if (!SetProperty(ref _layoutMode, value))
                return;

            OnPropertyChanged(nameof(IsDesktopSidebarVisible));
            OnPropertyChanged(nameof(IsOverlayMode));
            OnPropertyChanged(nameof(IsOverlayVisible));
            OnPropertyChanged(nameof(AreNavigationLabelsVisible));
            OnPropertyChanged(nameof(DesktopSidebarWidth));
            OnPropertyChanged(nameof(IsSidebarToggleVisible));
            OnPropertyChanged(nameof(SidebarToggleAutomationName));
        }
    }

    public ShellLayoutMode PreferredDesktopSidebarMode
    {
        get => _preferredDesktopSidebarMode;
        private set => SetProperty(ref _preferredDesktopSidebarMode, value);
    }

    public bool IsOverlayOpen
    {
        get => _isOverlayOpen;
        private set
        {
            if (!SetProperty(ref _isOverlayOpen, value))
                return;

            OnPropertyChanged(nameof(IsOverlayVisible));
            OnPropertyChanged(nameof(SidebarToggleAutomationName));
        }
    }

    public bool IsDesktopSidebarVisible => LayoutMode != ShellLayoutMode.Overlay;
    public bool IsOverlayMode => LayoutMode == ShellLayoutMode.Overlay;
    public bool IsOverlayVisible => IsOverlayMode && IsOverlayOpen;
    public bool AreNavigationLabelsVisible => LayoutMode != ShellLayoutMode.Compact;
    public bool IsSidebarToggleVisible => IsOverlayMode || _windowWidth >= ShellLayoutPolicy.ExpandedMinimumWidth;

    public double DesktopSidebarWidth => LayoutMode switch
    {
        ShellLayoutMode.Expanded => ExpandedSidebarWidth,
        ShellLayoutMode.Compact => CompactSidebarWidth,
        _ => 0
    };

    public string SidebarToggleAutomationName => LayoutMode switch
    {
        ShellLayoutMode.Expanded => "Collapse navigation sidebar",
        ShellLayoutMode.Compact when _windowWidth >= ShellLayoutPolicy.ExpandedMinimumWidth =>
            "Expand navigation sidebar",
        ShellLayoutMode.Compact => "Navigation is compact at this window width",
        _ when IsOverlayOpen => "Close navigation menu",
        _ => "Open navigation menu"
    };

    public bool IsPhoneConnected
    {
        get => _isPhoneConnected;
        private set
        {
            if (!SetProperty(ref _isPhoneConnected, value))
                return;

            OnPropertyChanged(nameof(IsFileBrowserAvailable));
            OnPropertyChanged(nameof(IsGalleryAvailable));
            OnPropertyChanged(nameof(IsBackupAvailable));
            OnPropertyChanged(nameof(PhoneStatusTitle));
            OnPropertyChanged(nameof(PhoneStatusDetail));
            OnPropertyChanged(nameof(PhoneStatusAutomationText));
            OnPropertyChanged(nameof(CanRetryConnection));
        }
    }

    public ShellPhoneConnectionState PhoneConnectionState
    {
        get => _phoneConnectionState;
        private set
        {
            if (!SetProperty(ref _phoneConnectionState, value))
                return;

            OnPropertyChanged(nameof(IsPhoneBusy));
            OnPropertyChanged(nameof(IsPhoneInError));
            OnPropertyChanged(nameof(PhoneStatusTitle));
            OnPropertyChanged(nameof(PhoneStatusDetail));
            OnPropertyChanged(nameof(PhoneStatusAutomationText));
            OnPropertyChanged(nameof(CanRetryConnection));
        }
    }

    public bool IsPhoneBusy => PhoneConnectionState is ShellPhoneConnectionState.Connecting
        or ShellPhoneConnectionState.Verifying
        or ShellPhoneConnectionState.Reconnecting;

    public bool IsPhoneInError => PhoneConnectionState == ShellPhoneConnectionState.Error;

    public string PhoneStatusTitle => PhoneConnectionState switch
    {
        ShellPhoneConnectionState.Connected => "Connected",
        ShellPhoneConnectionState.Verifying => "Verifying…",
        ShellPhoneConnectionState.Connecting => "Connecting…",
        ShellPhoneConnectionState.Reconnecting => "Reconnecting…",
        ShellPhoneConnectionState.Error => "Connection error",
        _ => "Not connected"
    };

    public string PhoneStatusDetail => PhoneConnectionState == ShellPhoneConnectionState.Error
        ? string.IsNullOrWhiteSpace(_phoneErrorMessage) ? "Connection failed" : _phoneErrorMessage
        : _phoneDeviceName ?? string.Empty;

    public string PhoneStatusAutomationText => string.IsNullOrWhiteSpace(PhoneStatusDetail)
        ? PhoneStatusTitle
        : $"{PhoneStatusTitle}. {PhoneStatusDetail}";

    public bool CanRetryConnection => !IsPhoneBusy && (IsPhoneInError || !string.IsNullOrWhiteSpace(CapabilityError));

    public bool SupportsFiles
    {
        get => _supportsFiles;
        private set
        {
            if (!SetProperty(ref _supportsFiles, value))
                return;
            OnPropertyChanged(nameof(IsFileBrowserAvailable));
        }
    }

    public bool SupportsGallery
    {
        get => _supportsGallery;
        private set
        {
            if (!SetProperty(ref _supportsGallery, value))
                return;
            OnPropertyChanged(nameof(IsGalleryAvailable));
        }
    }

    public bool SupportsBackup
    {
        get => _supportsBackup;
        private set
        {
            if (!SetProperty(ref _supportsBackup, value))
                return;
            OnPropertyChanged(nameof(IsBackupAvailable));
        }
    }

    public bool RequiresAllFilesAccess
    {
        get => _requiresAllFilesAccess;
        private set => SetProperty(ref _requiresAllFilesAccess, value);
    }

    public bool RequiresMediaPermissions
    {
        get => _requiresMediaPermissions;
        private set => SetProperty(ref _requiresMediaPermissions, value);
    }

    public string? CapabilityError
    {
        get => _capabilityError;
        private set
        {
            if (!SetProperty(ref _capabilityError, value))
                return;
            OnPropertyChanged(nameof(CanRetryConnection));
        }
    }

    public bool IsFileBrowserAvailable => IsPhoneConnected && SupportsFiles;
    public bool IsGalleryAvailable => IsPhoneConnected && SupportsGallery;
    public bool IsBackupAvailable => IsPhoneConnected && SupportsBackup;

    public int DashboardContactCount
    {
        get => _dashboardContactCount;
        private set
        {
            if (!SetProperty(ref _dashboardContactCount, value))
                return;
            RefreshNavigationBadges();
        }
    }

    public int DashboardPhotoCount
    {
        get => _dashboardPhotoCount;
        private set => SetProperty(ref _dashboardPhotoCount, value);
    }

    public int DashboardGroupCount
    {
        get => _dashboardGroupCount;
        private set => SetProperty(ref _dashboardGroupCount, value);
    }

    public string NavigationAnnouncement
    {
        get => _navigationAnnouncement;
        private set => SetProperty(ref _navigationAnnouncement, value);
    }
    public bool IsSearchOpen
    {
        get => _isSearchOpen;
        private set => SetProperty(ref _isSearchOpen, value);
    }

    public Task InitializeAsync() => NavigateToAsync(ShellDestination.Dashboard);

    public void UpdateWindowWidth(double width)
    {
        if (!double.IsFinite(width) || width < 0)
            return;

        _windowWidth = width;
        LayoutMode = ShellLayoutPolicy.GetEffectiveMode(width, PreferredDesktopSidebarMode);
        if (!IsOverlayMode)
            IsOverlayOpen = false;
        OnPropertyChanged(nameof(IsSidebarToggleVisible));
        OnPropertyChanged(nameof(SidebarToggleAutomationName));
    }

    public void UpdatePhoneConnection(
        bool isConnected,
        ShellPhoneConnectionState state,
        string? deviceName,
        string? errorMessage)
    {
        IsPhoneConnected = isConnected;
        PhoneConnectionState = state;
        _phoneDeviceName = deviceName;
        _phoneErrorMessage = errorMessage;
        OnPropertyChanged(nameof(PhoneStatusDetail));
        OnPropertyChanged(nameof(PhoneStatusAutomationText));
        RefreshNavigationAvailability();
    }

    public void ApplyCapabilities(CapabilityState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        SupportsFiles = state.SupportsFiles;
        SupportsGallery = state.SupportsGallery;
        SupportsBackup = state.SupportsBackup;
        RequiresAllFilesAccess = state.RequiresAllFilesAccess;
        RequiresMediaPermissions = state.RequiresMediaPermissions;
        CapabilityError = null;
        RefreshNavigationAvailability();
    }

    public void ResetCapabilities(string? error = null)
    {
        SupportsFiles = false;
        SupportsGallery = false;
        SupportsBackup = false;
        RequiresAllFilesAccess = false;
        RequiresMediaPermissions = false;
        CapabilityError = error;
        RefreshNavigationAvailability();
    }

    public void AddRecentActivity(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return;

        while (RecentActivity.Count >= 10)
            RecentActivity.RemoveAt(RecentActivity.Count - 1);

        RecentActivity.Insert(0, message);
    }

    [RelayCommand]
    private void ToggleSidebar()
    {
        if (IsOverlayMode)
        {
            IsOverlayOpen = !IsOverlayOpen;
            return;
        }

        if (_windowWidth < ShellLayoutPolicy.ExpandedMinimumWidth)
            return;

        PreferredDesktopSidebarMode = PreferredDesktopSidebarMode == ShellLayoutMode.Expanded
            ? ShellLayoutMode.Compact
            : ShellLayoutMode.Expanded;
        LayoutMode = ShellLayoutPolicy.GetEffectiveMode(_windowWidth, PreferredDesktopSidebarMode);
    }

    private void ApplyCompactSidebarPreference(bool compact)
    {
        PreferredDesktopSidebarMode = compact ? ShellLayoutMode.Compact : ShellLayoutMode.Expanded;
        LayoutMode = ShellLayoutPolicy.GetEffectiveMode(_windowWidth, PreferredDesktopSidebarMode);
    }

    [RelayCommand]
    private void CloseOverlay()
    {
        if (IsOverlayOpen)
            IsOverlayOpen = false;
    }

    [RelayCommand]
    private void RetryConnection()
    {
        if (CanRetryConnection)
            RetryConnectionRequested?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void GlobalSearch()
    {
        // Toggles the search overlay open/closed.
        IsSearchOpen = !IsSearchOpen;
    }

    [RelayCommand]
    private async Task NavigateByKeyAsync(string key)
    {
        if (string.IsNullOrWhiteSpace(key) || !_itemsByKey.TryGetValue(key, out var item))
            return;

        await NavigateToAsync(item.Destination);
    }

    public async Task NavigateToAsync(ShellDestination destination)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (!_itemsByDestination.TryGetValue(destination, out var item))
            throw new ArgumentOutOfRangeException(nameof(destination));

        if (!item.IsVisible || !item.IsEnabled)
        {
            NavigationAnnouncement = item.DisabledReason ?? $"{item.Label} is unavailable.";
            RestoreActiveSelection();
            return;
        }

        _navigationCancellation?.Cancel();
        _navigationCancellation?.Dispose();
        _navigationCancellation = new CancellationTokenSource();
        var cancellationToken = _navigationCancellation.Token;

        try
        {
            await _navigationService.NavigateAsync(destination, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            LogMessages.ShellNavigationFailed(_logger, ex, item.Key);
            NavigationAnnouncement = $"Could not open {item.Label}. Try again.";
            RestoreActiveSelection();
        }
    }

    private void OnNavigated(object? sender, NavigationChangedEventArgs e)
    {
        if (!_itemsByDestination.TryGetValue(e.Current, out var item))
            return;

        CurrentDestination = e.Current;
        CurrentTitle = item.Label;
        CurrentContent = e.Page;
        OnPropertyChanged(nameof(CurrentSubtitle));
        OnPropertyChanged(nameof(HasCurrentSubtitle));
        SynchronizeSelection(item);
        IsOverlayOpen = false;
        NavigationAnnouncement = e.IsReselection
            ? $"{item.Label} remains selected."
            : $"{item.Label} selected.";
        RefreshNavigationBadges();
    }

    private void SynchronizeSelection(ShellNavigationItemViewModel item)
    {
        _synchronizingSelection = true;
        try
        {
            SelectedNavigationItem = item;
        }
        finally
        {
            _synchronizingSelection = false;
        }
    }

    private void RestoreActiveSelection()
    {
        if (_itemsByDestination.TryGetValue(CurrentDestination, out var activeItem))
            SynchronizeSelection(activeItem);
    }

    private void RefreshNavigationAvailability()
    {
        var snapshot = new ShellCapabilitySnapshot(
            IsPhoneConnected,
            SupportsFiles,
            SupportsGallery,
            SupportsBackup,
            RequiresAllFilesAccess,
            RequiresMediaPermissions,
            CapabilityError);

        foreach (var item in NavigationItems)
        {
            var availability = ShellNavigationPolicy.Evaluate(item.Definition, snapshot);
            item.IsVisible = availability.IsVisible;
            item.IsEnabled = availability.IsEnabled;
            item.DisabledReason = availability.DisabledReason;
        }

        if (_itemsByDestination.TryGetValue(CurrentDestination, out var current) && !current.IsEnabled)
            _ = NavigateToAsync(ShellDestination.Dashboard);
    }

    private void RefreshNavigationBadges()
    {
        _itemsByDestination[ShellDestination.Contacts].BadgeText = FormatBadge(DashboardContactCount);
        _itemsByDestination[ShellDestination.Gallery].BadgeText = FormatBadge(_pageFactory.GalleryItemCount);
        _itemsByDestination[ShellDestination.Backup].BadgeText = FormatBadge(_pageFactory.BackupHistoryCount);
        DashboardPhotoCount = _pageFactory.GalleryItemCount;
        _dashboardViewModel.UpdatePhotoMetric(_pageFactory.GalleryItemCount, _pageFactory.IsGalleryMetricsLoaded);
    }

    private static string? FormatBadge(int value)
        => value > 0 ? value.ToString(CultureInfo.CurrentCulture) : null;

    private void OnContactMetricsPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ContactsViewModel.ContactCount))
            DashboardContactCount = _contactMetrics.ContactCount;
    }

    private void OnPageFactoryMetricsChanged(object? sender, EventArgs e)
        => RefreshNavigationBadges();

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _contactMetrics.PropertyChanged -= OnContactMetricsPropertyChanged;
        _pageFactory.MetricsChanged -= OnPageFactoryMetricsChanged;
        _navigationService.Navigated -= OnNavigated;
        _navigationCancellation?.Cancel();
        _navigationCancellation?.Dispose();
        _navigationCancellation = null;
        GC.SuppressFinalize(this);
    }
}
