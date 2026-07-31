using FluentAssertions;
using VcfEditor.Core.Settings;
using VcfEditor.Models;
using VcfEditor.Services;
using VcfEditor.Services.Settings;
using VcfEditor.ViewModels;

namespace VcfEditor.UI.Tests;

public sealed class SettingsViewModelTests
{
    [Fact]
    public async Task SaveCommandPersistsPreferencesAndAppliesTheSelectedTheme()
    {
        var store = new FakeSettingsStore();
        var themes = new RecordingThemeService();
        using var viewModel = new SettingsViewModel(
            store,
            themes,
            new ImmediateNotificationService(),
            new StubDiagnosticExportService());

        viewModel.SelectedTheme = AppTheme.Dark;
        viewModel.ConfirmOnDelete = false;
        viewModel.CompactSidebar = true;

        await viewModel.SaveCommand.ExecuteAsync(null);

        store.SavedPreferences.Should().NotBeNull();
        store.SavedPreferences!.Value.ConfirmOnDelete.Should().BeFalse();
        store.SavedPreferences.Value.ConfirmOnExit.Should().BeFalse();
        store.SavedPreferences.Value.Theme.Should().Be(AppTheme.Dark);
        store.SavedPreferences.Value.CompactSidebar.Should().BeTrue();
        themes.AppliedTheme.Should().Be(AppTheme.Dark);
        viewModel.IsDirty.Should().BeFalse();
        viewModel.HasSaveError.Should().BeFalse();
    }

    [Fact]
    public void RevokeDeviceCommandRemovesTheDeviceAndUpdatesTheStore()
    {
        var device = new PairedDeviceRecord("phone:8732", DateTimeOffset.UtcNow, null);
        var store = new FakeSettingsStore(device);
        using var viewModel = new SettingsViewModel(
            store,
            new RecordingThemeService(),
            new ImmediateNotificationService(),
            new StubDiagnosticExportService());

        viewModel.RevokeDeviceCommand.Execute(device);

        store.RevokedEndpoint.Should().Be("phone:8732");
        viewModel.PairedDevices.Should().BeEmpty();
        viewModel.HasPairedDevices.Should().BeFalse();
    }

    private sealed class FakeSettingsStore : IAppSettingsStore
    {
        private readonly IReadOnlyList<PairedDeviceRecord> _devices;

        public FakeSettingsStore(params PairedDeviceRecord[] devices)
        {
            _devices = devices;
        }

        public (bool ConfirmOnDelete, bool ConfirmOnExit, AppTheme Theme, bool CompactSidebar)? SavedPreferences { get; private set; }
        public string? RevokedEndpoint { get; private set; }

        public string? GetPinnedCertSha256(string endpointKey) => null;
        public void SetPinnedCertSha256(string endpointKey, string? sha256) { }
        public IReadOnlyList<PairedDeviceRecord> GetPairedDevices() => _devices;
        public void RevokePairedDevice(string endpointKey) => RevokedEndpoint = endpointKey;
        public byte[]? GetBackupSeed(string seedId) => null;
        public void SetBackupSeed(string seedId, ReadOnlySpan<byte> seed) { }
        public void RemoveBackupSeed(string seedId) { }
        public bool GetConfirmOnDelete() => true;
        public void SetConfirmOnDelete(bool value) { }
        public bool GetConfirmOnExit() => false;
        public void SetConfirmOnExit(bool value) { }
        public AppTheme GetTheme() => AppTheme.System;
        public void SetTheme(AppTheme value) { }
        public bool GetCompactSidebar() => false;
        public void SetCompactSidebar(bool value) { }

        public Task SaveDesktopPreferencesAsync(
            bool confirmOnDelete,
            bool confirmOnExit,
            AppTheme theme,
            bool compactSidebar,
            CancellationToken cancellationToken = default)
        {
            SavedPreferences = (confirmOnDelete, confirmOnExit, theme, compactSidebar);
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingThemeService : IThemeService
    {
        public AppTheme CurrentTheme => AppliedTheme ?? AppTheme.System;
        public AppTheme? AppliedTheme { get; private set; }
        public void Apply(AppTheme theme) => AppliedTheme = theme;
    }

    private sealed class ImmediateNotificationService : IUserNotificationService
    {
        public Task WaitForDismissalAsync(TimeSpan duration, CancellationToken cancellationToken)
            => Task.CompletedTask;
    }

    private sealed class StubDiagnosticExportService : IDiagnosticExportService
    {
        public Task<string> ExportDiagnosticsAsync(CancellationToken cancellationToken = default)
            => Task.FromResult("diagnostics.txt");
    }
}
