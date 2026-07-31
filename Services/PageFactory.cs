using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using VcfEditor.Core;
using VcfEditor.Features.PhoneSession;
using VcfEditor.Navigation;
using VcfEditor.ViewModels;
using VcfEditor.Views;

namespace VcfEditor.Services;

public sealed class PageFactory : IPageFactory
{
    private readonly IPhoneSessionScopeFactory _phoneSessionScopeFactory;
    private readonly Dictionary<ShellDestination, object> _pages;
    private readonly Dictionary<ShellDestination, IAsyncInitializable> _initializablePages = [];
    private PhoneSessionScope? _phoneSessionScope;
    private INotifyCollectionChanged? _galleryItemsSource;
    private INotifyCollectionChanged? _backupHistorySource;
    private bool _disposed;

    public PageFactory(
        IPhoneSessionScopeFactory phoneSessionScopeFactory,
        DashboardView dashboardView,
        ContactsView contactsView,
        SettingsView settingsView)
    {
        ArgumentNullException.ThrowIfNull(phoneSessionScopeFactory);
        ArgumentNullException.ThrowIfNull(dashboardView);
        ArgumentNullException.ThrowIfNull(contactsView);
        ArgumentNullException.ThrowIfNull(settingsView);

        _phoneSessionScopeFactory = phoneSessionScopeFactory;
        DashboardView = dashboardView;
        ContactsView = contactsView;
        _pages = new Dictionary<ShellDestination, object>
        {
            [ShellDestination.Dashboard] = dashboardView,
            [ShellDestination.Contacts] = contactsView,
            [ShellDestination.Settings] = settingsView
        };
    }

    public event EventHandler? MetricsChanged;

    public DashboardView DashboardView { get; }
    public ContactsView ContactsView { get; }
    public int GalleryItemCount => _phoneSessionScope?.GalleryViewModel.MediaItems.Count ?? 0;
    public bool IsGalleryMetricsLoaded => _phoneSessionScope?.GalleryViewModel.IsInitialized == true;
    public int BackupHistoryCount => _phoneSessionScope?.BackupViewModel.History.Count ?? 0;

    public object? GetPage(ShellDestination destination)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _pages.GetValueOrDefault(destination);
    }

    public void SetPhoneClient(PhoneApiClient? client)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ClearPhonePages();

        if (client is null)
        {
            MetricsChanged?.Invoke(this, EventArgs.Empty);
            return;
        }

        var scope = _phoneSessionScopeFactory.Create(client);
        _phoneSessionScope = scope;

        scope.GalleryViewModel.PropertyChanged += OnGalleryViewModelPropertyChanged;
        scope.BackupViewModel.PropertyChanged += OnBackupViewModelPropertyChanged;
        AttachGalleryMetricsSource();
        AttachBackupMetricsSource();

        RegisterPhonePage(
            ShellDestination.FileBrowser,
            scope.FileBrowserView,
            scope.FileBrowserViewModel);
        RegisterPhonePage(
            ShellDestination.Gallery,
            scope.GalleryView,
            scope.GalleryViewModel);
        RegisterPhonePage(
            ShellDestination.Backup,
            scope.BackupView,
            scope.BackupViewModel);

        MetricsChanged?.Invoke(this, EventArgs.Empty);
    }

    public void UpdateCapabilities(ShellCapabilitySnapshot capabilities)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(capabilities);
        _phoneSessionScope?.Context.UpdateCapabilities(capabilities);
    }

    public async Task InitializePageAsync(
        ShellDestination destination,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        cancellationToken.ThrowIfCancellationRequested();

        if (_initializablePages.TryGetValue(destination, out var initializable))
            await initializable.InitializeAsync(cancellationToken);

        cancellationToken.ThrowIfCancellationRequested();
        MetricsChanged?.Invoke(this, EventArgs.Empty);
    }

    private void RegisterPhonePage(
        ShellDestination destination,
        object page,
        IAsyncInitializable initializable)
    {
        _pages[destination] = page;
        _initializablePages[destination] = initializable;
    }

    private void OnMetricsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        => MetricsChanged?.Invoke(this, EventArgs.Empty);

    private void OnGalleryViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(GalleryViewModel.MediaItems))
            AttachGalleryMetricsSource();

        MetricsChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnBackupViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(BackupViewModel.History))
            AttachBackupMetricsSource();

        MetricsChanged?.Invoke(this, EventArgs.Empty);
    }

    private void AttachGalleryMetricsSource()
    {
        if (_galleryItemsSource is not null)
            _galleryItemsSource.CollectionChanged -= OnMetricsCollectionChanged;

        _galleryItemsSource = _phoneSessionScope?.GalleryViewModel.MediaItems;
        if (_galleryItemsSource is not null)
            _galleryItemsSource.CollectionChanged += OnMetricsCollectionChanged;
    }

    private void AttachBackupMetricsSource()
    {
        if (_backupHistorySource is not null)
            _backupHistorySource.CollectionChanged -= OnMetricsCollectionChanged;

        _backupHistorySource = _phoneSessionScope?.BackupViewModel.History;
        if (_backupHistorySource is not null)
            _backupHistorySource.CollectionChanged += OnMetricsCollectionChanged;
    }

    private void ClearPhonePages()
    {
        if (_galleryItemsSource is not null)
            _galleryItemsSource.CollectionChanged -= OnMetricsCollectionChanged;
        if (_backupHistorySource is not null)
            _backupHistorySource.CollectionChanged -= OnMetricsCollectionChanged;
        if (_phoneSessionScope is not null)
        {
            _phoneSessionScope.GalleryViewModel.PropertyChanged -= OnGalleryViewModelPropertyChanged;
            _phoneSessionScope.BackupViewModel.PropertyChanged -= OnBackupViewModelPropertyChanged;
        }

        _galleryItemsSource = null;
        _backupHistorySource = null;

        _phoneSessionScope?.Dispose();
        _phoneSessionScope = null;

        _pages.Remove(ShellDestination.FileBrowser);
        _pages.Remove(ShellDestination.Gallery);
        _pages.Remove(ShellDestination.Backup);
        _initializablePages.Remove(ShellDestination.FileBrowser);
        _initializablePages.Remove(ShellDestination.Gallery);
        _initializablePages.Remove(ShellDestination.Backup);
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        ClearPhonePages();
        GC.SuppressFinalize(this);
    }
}
