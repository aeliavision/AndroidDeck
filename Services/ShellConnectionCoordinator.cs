using System;
using System.ComponentModel;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Extensions.Logging;
using VcfEditor.Core;
using VcfEditor.Helpers;
using VcfEditor.Navigation;
using VcfEditor.ViewModels;

namespace VcfEditor.Services;

public sealed class ShellConnectionCoordinator : IShellConnectionCoordinator
{
    private readonly ShellWindowViewModel _viewModel;
    private readonly DashboardViewModel _dashboardViewModel;
    private readonly IPageFactory _pageFactory;
    private readonly ILogger<ShellConnectionCoordinator> _logger;
    private readonly IDialogService _dialogService;
    private readonly Views.ContactsView _contactsView;
    private CancellationTokenSource? _phoneBannerCancellation;
    private CancellationTokenSource? _capabilityCancellation;
    private PhoneApiClient? _phoneClient;
    private PropertyChangedEventHandler? _phoneClientPropertyChangedHandler;
    private int _reconnectAttempt;
    private bool _started;
    private bool _disposed;

    public ShellConnectionCoordinator(
        ShellWindowViewModel viewModel,
        DashboardViewModel dashboardViewModel,
        IPageFactory pageFactory,
        ILogger<ShellConnectionCoordinator> logger,
        IDialogService dialogService)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        ArgumentNullException.ThrowIfNull(dashboardViewModel);
        ArgumentNullException.ThrowIfNull(pageFactory);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(dialogService);

        _viewModel = viewModel;
        _dashboardViewModel = dashboardViewModel;
        _pageFactory = pageFactory;
        _logger = logger;
        _dialogService = dialogService;
        _contactsView = pageFactory.ContactsView;
    }

    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_started)
            return;
        _started = true;

        _dashboardViewModel.PhoneConnected += OnDashboardPhoneConnected;
        _dashboardViewModel.RefreshContactsRequested += OnRefreshContactsRequested;
        _dashboardViewModel.PhoneDisconnected += OnDashboardPhoneDisconnected;
        _dashboardViewModel.RetryPhoneConnectionRequested += OnRetryPhoneConnectionRequested;
        _dashboardViewModel.NavigationRequested += OnDashboardNavigationRequested;

        _contactsView.PhoneClientChanged += OnPhoneClientChanged;
        _contactsView.RetryPhoneConnectionRequested += OnRetryPhoneConnectionRequested;
        _viewModel.RetryConnectionRequested += OnRetryPhoneConnectionRequested;

        UpdatePhoneConnection(false, ShellPhoneConnectionState.Disconnected, null, null);
    }

    private async void OnDashboardPhoneConnected(
        PhoneContactsClient contactsClient,
        PhoneApiClient apiClient)
    {
        try
        {
            _dashboardViewModel.AddActivity("Connecting to phone…");
            UpdatePhoneConnection(false, ShellPhoneConnectionState.Connecting, apiClient.DeviceName, null);

            await _contactsView.ConnectFromDashboardAsync(contactsClient, apiClient);
            await _viewModel.NavigateToAsync(ShellDestination.Contacts);

            _dashboardViewModel.AddActivity("Phone connected");
            UpdatePhoneConnectionState(apiClient);
        }
        catch (Exception ex)
        {
            _dialogService.ShowError(ex.Message, "Connect failed");
            _dashboardViewModel.AddActivity("Phone connection failed");
            UpdatePhoneConnection(false, ShellPhoneConnectionState.Error, apiClient.DeviceName, ex.Message);
        }
    }

    private void OnRefreshContactsRequested() => _ = RefreshContactsFromDashboardAsync();

    private async Task RefreshContactsFromDashboardAsync()
    {
        try
        {
            _dashboardViewModel.AddActivity("Refreshing contacts…");
            await _contactsView.RefreshFromDashboardAsync();
            _dashboardViewModel.AddActivity("Contacts refreshed");
        }
        catch (Exception ex)
        {
            _dialogService.ShowError(ex.Message, "Refresh failed");
            _dashboardViewModel.AddActivity("Contact refresh failed");
        }
    }

    private void OnDashboardPhoneDisconnected()
    {
        _contactsView.DisconnectFromDashboard();
        _dashboardViewModel.AddActivity("Phone disconnected");
    }

    private void OnRetryPhoneConnectionRequested(object? sender, EventArgs e)
        => _ = RetryPhoneConnectionAsync();

    private void OnDashboardNavigationRequested(ShellDestination destination)
        => _ = _viewModel.NavigateToAsync(destination);

    private void OnPhoneClientChanged(PhoneApiClient? client)
    {
        CancelCapabilityDiscovery();

        if (_phoneClient is not null && _phoneClientPropertyChangedHandler is not null)
            _phoneClient.PropertyChanged -= _phoneClientPropertyChangedHandler;

        _phoneClientPropertyChangedHandler = null;
        _phoneClient = client;
        _pageFactory.SetPhoneClient(client);
        ResetCapabilities();

        if (client is null)
        {
            UpdatePhoneConnection(false, ShellPhoneConnectionState.Disconnected, null, null);
            return;
        }

        _phoneClientPropertyChangedHandler = (_, e) =>
        {
            if (e.PropertyName is not (nameof(PhoneApiClient.IsConnected)
                or nameof(PhoneApiClient.State)
                or nameof(PhoneApiClient.DeviceName)
                or nameof(PhoneApiClient.ErrorMessage)))
            {
                return;
            }

            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher is null)
                return;

            _ = dispatcher.BeginInvoke(new Action(() =>
            {
                if (!ReferenceEquals(_phoneClient, client))
                    return;

                UpdatePhoneConnectionState(client);
                if (client.IsConnected)
                    StartCapabilityDiscovery(client);
                else
                    ResetCapabilities();
            }));
        };

        client.PropertyChanged += _phoneClientPropertyChangedHandler;
        UpdatePhoneConnectionState(client);

        if (client.IsConnected)
            StartCapabilityDiscovery(client);
        else
            ResetCapabilities();
    }

    private void UpdatePhoneConnectionState(PhoneApiClient client)
    {
        UpdatePhoneConnection(
            client.IsConnected,
            MapToShellState(client.State),
            client.DeviceName,
            client.ErrorMessage);
        UpdateContactsPhoneBanner();
    }

    private void UpdatePhoneConnection(
        bool isConnected,
        ShellPhoneConnectionState state,
        string? deviceName,
        string? errorMessage)
    {
        _viewModel.UpdatePhoneConnection(isConnected, state, deviceName, errorMessage);
        _dashboardViewModel.UpdatePhoneConnection(isConnected, state, deviceName, errorMessage);
    }

    private static ShellPhoneConnectionState MapToShellState(PhoneConnectionState state)
    {
        return state switch
        {
            PhoneConnectionState.Disconnected => ShellPhoneConnectionState.Disconnected,
            PhoneConnectionState.Pairing => ShellPhoneConnectionState.Verifying,
            PhoneConnectionState.Connected => ShellPhoneConnectionState.Connected,
            PhoneConnectionState.Error => ShellPhoneConnectionState.Error,
            _ => ShellPhoneConnectionState.Disconnected
        };
    }

    private void StartCapabilityDiscovery(PhoneApiClient client)
    {
        CancelCapabilityDiscovery();
        _capabilityCancellation = new CancellationTokenSource(TimeSpan.FromSeconds(12));
        _ = RefreshCapabilitiesAsync(client, _capabilityCancellation.Token);
    }

    private async Task RefreshCapabilitiesAsync(
        PhoneApiClient client,
        CancellationToken cancellationToken)
    {
        try
        {
            var transport = client.Transport;
            if (transport.BaseUrl is null)
                throw new InvalidOperationException("The phone endpoint is not configured.");

            var fullUrl = transport.BaseUrl + "/api/v1/status";
            const string signaturePath = "/api/v1/status";
            var json = await transport.SendAsync(
                HttpMethod.Get,
                fullUrl,
                signaturePath,
                cancellationToken: cancellationToken);

            if (!ReferenceEquals(_phoneClient, client))
                return;

            var capabilities = CapabilityState.FromStatusJson(json);
            _viewModel.ApplyCapabilities(capabilities);
            _dashboardViewModel.ApplyCapabilities(capabilities);
            ParseAndApplyDeviceCounts(json);
            _pageFactory.UpdateCapabilities(new ShellCapabilitySnapshot(
                true,
                capabilities.SupportsFiles,
                capabilities.SupportsGallery,
                capabilities.SupportsBackup,
                capabilities.RequiresAllFilesAccess,
                capabilities.RequiresMediaPermissions,
                null));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            if (ReferenceEquals(_phoneClient, client) &&
                _capabilityCancellation?.Token == cancellationToken)
            {
                const string error = "Capability check timed out. Select Retry.";
                ResetCapabilities(error);
                _pageFactory.UpdateCapabilities(new ShellCapabilitySnapshot(
                    true, false, false, false, false, false, error));
            }
        }
        catch (Exception ex)
        {
            LogMessages.CapabilityDiscoveryFailed(_logger, ex, client.Transport.BaseUrl);
            if (ReferenceEquals(_phoneClient, client))
            {
                var fallbackState = new VcfEditor.Core.CapabilityState(
                    SupportsFiles: true,
                    SupportsGallery: true,
                    SupportsBackup: true,
                    RequiresAllFilesAccess: false,
                    RequiresMediaPermissions: false);

                _viewModel.ApplyCapabilities(fallbackState);
                _dashboardViewModel.ApplyCapabilities(fallbackState);
                _pageFactory.UpdateCapabilities(new ShellCapabilitySnapshot(
                    true, true, true, true, false, false, null));
            }
        }
    }

    private void ResetCapabilities(string? error = null)
    {
        _viewModel.ResetCapabilities(error);
        _dashboardViewModel.ResetCapabilities();
        _dashboardViewModel.ResetStorage();
    }

    /// <summary>
    /// Extracts optional device-count fields from the status JSON payload and
    /// forwards them to the dashboard. Safe to call even when fields are absent.
    /// Currently handles: storageTotalBytes/storageFreeBytes, mediaCount, groupCount.
    /// </summary>
    private void ParseAndApplyDeviceCounts(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;

            // Storage
            if (root.TryGetProperty("storageTotalBytes", out var totalEl) &&
                root.TryGetProperty("storageFreeBytes", out var freeEl) &&
                totalEl.TryGetInt64(out var total) &&
                freeEl.TryGetInt64(out var free))
            {
                _dashboardViewModel.UpdateStorage(total, free);
            }

            // Photo / media count (companion app v3.0+)
            if (root.TryGetProperty("mediaCount", out var mediaEl) &&
                mediaEl.TryGetInt32(out var mediaCount))
            {
                _dashboardViewModel.UpdatePhotoMetric(mediaCount, isLoaded: true);
            }

            // Contact-group count (companion app v3.0+)
            if (root.TryGetProperty("groupCount", out var groupEl) &&
                groupEl.TryGetInt32(out var groupCount))
            {
                _dashboardViewModel.UpdateGroupMetric(groupCount, isLoaded: true);
            }
        }
        catch (JsonException)
        {
            // Fields absent or malformed — dashboard stays at "—".
        }
    }

    private void UpdateContactsPhoneBanner()
    {
        if (_viewModel.PhoneConnectionState == ShellPhoneConnectionState.Reconnecting)
        {
            _contactsView.ShowPhoneConnectionBanner(
                "Connection interrupted — reconnecting…",
                showRetry: false);
            return;
        }

        if (_viewModel.PhoneConnectionState == ShellPhoneConnectionState.Error)
        {
            _contactsView.ShowPhoneConnectionBanner("Connection lost — Retry?", showRetry: true);
            StartTimedBannerAutoHide();
            return;
        }

        _contactsView.HidePhoneConnectionBanner();
    }

    private void StartTimedBannerAutoHide()
    {
        _phoneBannerCancellation?.Cancel();
        _phoneBannerCancellation?.Dispose();
        _phoneBannerCancellation = new CancellationTokenSource();
        _ = AutoHidePhoneBannerAsync(_phoneBannerCancellation.Token);
    }

    private async Task AutoHidePhoneBannerAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(10), cancellationToken);
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher is not null)
                await dispatcher.InvokeAsync(_contactsView.HidePhoneConnectionBanner);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private async Task RetryPhoneConnectionAsync()
    {
        if (_phoneClient is null)
        {
            _contactsView.ShowPhoneConnectionBanner(
                "Not connected — open Connect Phone to pair.",
                showRetry: false);
            StartTimedBannerAutoHide();
            return;
        }

        _reconnectAttempt++;
        UpdatePhoneConnection(false, ShellPhoneConnectionState.Reconnecting, _phoneClient.DeviceName, null);
        _dashboardViewModel.AddActivity($"Retrying connection (attempt {_reconnectAttempt})…");
        UpdateContactsPhoneBanner();

        var recovered = false;
        try
        {
            using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(6));
            recovered = await _phoneClient.TryRecoverAsync(cancellation.Token);
        }
        catch (OperationCanceledException)
        {
            recovered = false;
        }
        catch (HttpRequestException)
        {
            recovered = false;
        }
        catch (Exception ex)
        {
            LogMessages.SessionRecoveryFailed(_logger, ex);
            recovered = false;
        }

        if (recovered)
        {
            UpdatePhoneConnection(true, ShellPhoneConnectionState.Connected, _phoneClient.DeviceName, null);
            _dashboardViewModel.AddActivity("Reconnected");
            _contactsView.HidePhoneConnectionBanner();
            StartCapabilityDiscovery(_phoneClient);
        }
        else
        {
            UpdatePhoneConnection(false, ShellPhoneConnectionState.Error, _phoneClient.DeviceName, _phoneClient.ErrorMessage);
            _dashboardViewModel.AddActivity("Reconnect failed");
            UpdateContactsPhoneBanner();
        }
    }

    private void CancelCapabilityDiscovery()
    {
        _capabilityCancellation?.Cancel();
        _capabilityCancellation?.Dispose();
        _capabilityCancellation = null;
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;

        if (_started)
        {
            _dashboardViewModel.PhoneConnected -= OnDashboardPhoneConnected;
            _dashboardViewModel.RefreshContactsRequested -= OnRefreshContactsRequested;
            _dashboardViewModel.PhoneDisconnected -= OnDashboardPhoneDisconnected;
            _dashboardViewModel.RetryPhoneConnectionRequested -= OnRetryPhoneConnectionRequested;
            _dashboardViewModel.NavigationRequested -= OnDashboardNavigationRequested;

            _contactsView.PhoneClientChanged -= OnPhoneClientChanged;
            _contactsView.RetryPhoneConnectionRequested -= OnRetryPhoneConnectionRequested;
            _viewModel.RetryConnectionRequested -= OnRetryPhoneConnectionRequested;
        }

        if (_phoneClient is not null && _phoneClientPropertyChangedHandler is not null)
            _phoneClient.PropertyChanged -= _phoneClientPropertyChangedHandler;

        _phoneClientPropertyChangedHandler = null;
        _phoneBannerCancellation?.Cancel();
        _phoneBannerCancellation?.Dispose();
        _phoneBannerCancellation = null;
        CancelCapabilityDiscovery();
        _pageFactory.Dispose();
        _phoneClient?.Dispose();
        _phoneClient = null;

        GC.SuppressFinalize(this);
    }
}
