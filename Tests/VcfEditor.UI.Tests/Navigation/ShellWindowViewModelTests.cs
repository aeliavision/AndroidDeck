using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using VcfEditor.Core;
using VcfEditor.Models;
using VcfEditor.Models.DTOs;
using VcfEditor.Navigation;
using VcfEditor.Services;
using VcfEditor.ViewModels;
using VcfEditor.Views;
using Xunit;

namespace VcfEditor.UI.Tests.Navigation;

public sealed class ShellWindowViewModelTests
{
    [Theory]
    [InlineData(899, ShellLayoutMode.Overlay)]
    [InlineData(900, ShellLayoutMode.Compact)]
    [InlineData(1199, ShellLayoutMode.Compact)]
    [InlineData(1200, ShellLayoutMode.Expanded)]
    public void UpdateWindowWidthAppliesResponsiveLayout(double width, ShellLayoutMode expected)
    {
        using var fixture = new Fixture();

        fixture.ViewModel.UpdateWindowWidth(width);

        fixture.ViewModel.LayoutMode.Should().Be(expected);
    }

    [Fact]
    public async Task DisabledNavigationAnnouncesReasonAndKeepsCurrentPage()
    {
        using var fixture = new Fixture();
        await fixture.ViewModel.InitializeAsync();

        await fixture.ViewModel.NavigateToAsync(ShellDestination.FileBrowser);

        fixture.Navigation.Current.Should().Be(ShellDestination.Dashboard);
        fixture.ViewModel.NavigationAnnouncement.Should().Be("Connect a phone to browse files.");
    }

    [Fact]
    public async Task SuccessfulNavigationClosesOverlayAndPublishesContent()
    {
        using var fixture = new Fixture();
        fixture.ViewModel.UpdateWindowWidth(800);
        fixture.ViewModel.ToggleSidebarCommand.Execute(null);
        fixture.ViewModel.IsOverlayOpen.Should().BeTrue();

        await fixture.ViewModel.NavigateToAsync(ShellDestination.Contacts);

        fixture.ViewModel.CurrentDestination.Should().Be(ShellDestination.Contacts);
        fixture.ViewModel.CurrentTitle.Should().Be("Contacts");
        fixture.ViewModel.CurrentContent.Should().BeSameAs(fixture.Pages.ContactsPage);
        fixture.ViewModel.IsOverlayOpen.Should().BeFalse();
    }

    private sealed class Fixture : IDisposable
    {
        public Fixture()
        {
            Metrics = new FakeContactMetrics();
            Pages = new FakePageFactory();
            Navigation = new NavigationService(Pages);
            var dashboardViewModel = new DashboardViewModel(Metrics, new NullDialogService());
            ViewModel = new ShellWindowViewModel(
                new ShellNavigationRegistry(),
                Navigation,
                Pages,
                Metrics,
                dashboardViewModel,
                (SettingsViewModel)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(SettingsViewModel)),
                NullLogger<ShellWindowViewModel>.Instance);
        }

        public FakeContactMetrics Metrics { get; }
        public FakePageFactory Pages { get; }
        public NavigationService Navigation { get; }
        public ShellWindowViewModel ViewModel { get; }

        public void Dispose()
        {
            ViewModel.Dispose();
            Pages.Dispose();
        }
    }

#pragma warning disable CA1822
    private sealed class NullDialogService : IDialogService
    {
        public void ShowInformation(string message, string title) { }
        public void ShowWarning(string message, string title) { }
        public void ShowError(string message, string title) { }
        public bool Confirm(string message, string title) => false;
        public PhoneConnectionResult? ShowConnectPhoneDialog() => null;
        public string? ShowOpenVcfDialog() => null;
        public string? ShowSaveVcfDialog(string? currentPath = null) => null;
        public Contact? ShowCreateContactDialog(ContactSource source) => null;
        public bool ShowEditContactDialog(Contact contact) => false;
        public PhoneNumber? ShowCreatePhoneNumberDialog() => null;
        public bool ShowEditPhoneNumberDialog(PhoneNumber phoneNumber) => false;
        public string? ShowDownloadFolderDialog() => null;
        public string[] ShowUploadFilesDialog() => Array.Empty<string>();
        public ConflictChoice ShowConflictDialog(string title, string message) => ConflictChoice.Skip;
        public string? ShowNewFolderDialog() => null;
        public string? ShowRenameDialog(string currentName) => null;
        public string? ShowMoveDialog(string defaultDestinationPath) => null;
        public GalleryMetadataResult? ShowGalleryMetadataDialog(
            string title,
            bool? favoriteInitial = null,
            string? descriptionInitial = null) => null;
        public string? ShowSaveBackupArchiveDialog() => null;
        public string? ShowOpenBackupArchiveDialog() => null;
        public string? ShowEncryptBackupPasswordDialog() => null;
        public string? ShowDecryptBackupPasswordDialog() => null;
        public bool ShowBackupSummaryDialog(string title, List<(string Label, string Value)> rows) => false;
        public bool ShowRestorePreviewDialog(
            string title,
            string message,
            List<(string Label, string Value)> rows,
            string primaryActionText = "Start restore") => false;
        public void ShowBackupCompletionDialog(
            string title,
            string message,
            List<(string Label, string Value)> rows) { }
        public string? ShowSaveDatabaseDialog(string defaultName) => null;
        public string? ShowOpenDatabaseDialog() => null;
        public Task<string?> ShowTransferConflictDialogAsync(string sourceName, string destinationPath) => Task.FromResult<string?>(null);
    }
#pragma warning restore CA1822

    private sealed class FakeContactMetrics : IContactMetrics
    {
        public event PropertyChangedEventHandler? PropertyChanged
        {
            add { }
            remove { }
        }

        public int ContactCount => 0;
        public bool IsSourceLoaded => false;
    }

    private sealed class FakePageFactory : IPageFactory
    {
        private readonly object _dashboardPage = new();
        public object ContactsPage { get; } = new();

        public event EventHandler? MetricsChanged
        {
            add { }
            remove { }
        }

        public DashboardView DashboardView => null!;
        public ContactsView ContactsView => null!;
        public int GalleryItemCount => 0;
        public bool IsGalleryMetricsLoaded => false;
        public int BackupHistoryCount => 0;

        public object? GetPage(ShellDestination destination) => destination switch
        {
            ShellDestination.Dashboard => _dashboardPage,
            ShellDestination.Contacts => ContactsPage,
            ShellDestination.Settings => new object(),
            _ => null
        };

        public void SetPhoneClient(PhoneApiClient? client)
        {
        }

        public void UpdateCapabilities(ShellCapabilitySnapshot capabilities)
        {
        }

        public Task InitializePageAsync(
            ShellDestination destination,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }

        public void Dispose()
        {
        }
    }
}
