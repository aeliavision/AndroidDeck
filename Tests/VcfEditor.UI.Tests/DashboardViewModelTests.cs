using System.ComponentModel;
using FluentAssertions;
using VcfEditor.Core;
using VcfEditor.Models;
using VcfEditor.Services;
using VcfEditor.ViewModels;

namespace VcfEditor.UI.Tests;

public sealed class DashboardViewModelTests
{
    [Fact]
    public void MetricsRemainUnavailableUntilTheirSourceIsLoaded()
    {
        var metrics = new FakeContactMetrics();
        var viewModel = new DashboardViewModel(metrics, new StubDialogService());

        viewModel.Metrics[0].Value.Should().Be("—");
        viewModel.UpdatePhotoMetric(0, isLoaded: false);
        viewModel.Metrics[1].Value.Should().Be("—");

        metrics.Set(0, isLoaded: true);
        viewModel.UpdatePhotoMetric(0, isLoaded: true);

        viewModel.Metrics[0].Value.Should().Be("0");
        viewModel.Metrics[1].Value.Should().Be("0");
    }

    [Fact]
    public void ActivityIncludesARealTimestamp()
    {
        var viewModel = new DashboardViewModel(new FakeContactMetrics(), new StubDialogService());
        var timestamp = new DateTimeOffset(2026, 7, 29, 8, 0, 0, TimeSpan.FromHours(3));

        viewModel.AddActivity("Contacts refreshed", timestamp);

        viewModel.RecentActivity.Should().ContainSingle();
        viewModel.RecentActivity[0].OccurredAt.Should().Be(timestamp);
        viewModel.IsRecentActivityEmpty.Should().BeFalse();
    }

    private sealed class FakeContactMetrics : IContactMetrics
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        public int ContactCount { get; private set; }
        public bool IsSourceLoaded { get; private set; }
        public void Set(int count, bool isLoaded)
        {
            ContactCount = count;
            IsSourceLoaded = isLoaded;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ContactCount)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSourceLoaded)));
        }
    }

    private sealed class StubDialogService : IDialogService
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
    }
}
