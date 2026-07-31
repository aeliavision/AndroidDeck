using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using VcfEditor.Models;

namespace VcfEditor.Core.Settings
{
    public interface IAppSettingsStore
    {
        string? GetPinnedCertSha256(string endpointKey);
        void SetPinnedCertSha256(string endpointKey, string? sha256);
        IReadOnlyList<PairedDeviceRecord> GetPairedDevices();
        void RevokePairedDevice(string endpointKey);

        byte[]? GetBackupSeed(string seedId);
        void SetBackupSeed(string seedId, ReadOnlySpan<byte> seed);
        void RemoveBackupSeed(string seedId);

        bool GetConfirmOnDelete();
        void SetConfirmOnDelete(bool value);
        bool GetConfirmOnExit();
        void SetConfirmOnExit(bool value);
        AppTheme GetTheme();
        void SetTheme(AppTheme value);
        bool GetCompactSidebar();
        void SetCompactSidebar(bool value);
        Task SaveDesktopPreferencesAsync(
            bool confirmOnDelete,
            bool confirmOnExit,
            AppTheme theme,
            bool compactSidebar,
            CancellationToken cancellationToken = default);
    }

    internal sealed class NullAppSettingsStore : IAppSettingsStore
    {
        public static NullAppSettingsStore Instance { get; } = new();
        private NullAppSettingsStore() { }
        public string? GetPinnedCertSha256(string endpointKey) => null;
        public void SetPinnedCertSha256(string endpointKey, string? sha256) { }
        public IReadOnlyList<PairedDeviceRecord> GetPairedDevices() => Array.Empty<PairedDeviceRecord>();
        public void RevokePairedDevice(string endpointKey) { }
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
        public Task SaveDesktopPreferencesAsync(bool confirmOnDelete, bool confirmOnExit, AppTheme theme, bool compactSidebar, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }
}
