using VcfEditor.Helpers;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using VcfEditor.Core.Security;
using VcfEditor.Models;

namespace VcfEditor.Core.Settings
{
    public sealed class JsonAppSettingsStore : IAppSettingsStore
    {
        private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

        private readonly object _gate = new();
        private readonly string _settingsPath;
        private readonly ISecretStore _secretStore;
        private readonly ILogger<JsonAppSettingsStore> _logger;
        private SettingsModel? _settings;

        public JsonAppSettingsStore(
            ISecretStore secretStore,
            ILogger<JsonAppSettingsStore> logger)
            : this(GetDefaultPath(), secretStore, logger)
        {
        }

        internal JsonAppSettingsStore(
            string settingsPath,
            ISecretStore secretStore,
            ILogger<JsonAppSettingsStore> logger)
        {
            if (string.IsNullOrWhiteSpace(settingsPath))
                throw new ArgumentException("A settings file path is required.", nameof(settingsPath));

            _settingsPath = settingsPath;
            ArgumentNullException.ThrowIfNull(secretStore);
            _secretStore = secretStore;
            ArgumentNullException.ThrowIfNull(logger);
            _logger = logger;

            var directory = Path.GetDirectoryName(_settingsPath);
            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);
        }

        public string? GetPinnedCertSha256(string endpointKey)
        {
            if (string.IsNullOrWhiteSpace(endpointKey)) return null;

            lock (_gate)
            {
                EnsureLoaded();
                return _settings!.PinnedCertSha256ByEndpoint.TryGetValue(endpointKey, out var value)
                    ? value
                    : null;
            }
        }

        public void SetPinnedCertSha256(string endpointKey, string? sha256)
        {
            if (string.IsNullOrWhiteSpace(endpointKey)) return;

            lock (_gate)
            {
                EnsureLoaded();
                if (string.IsNullOrWhiteSpace(sha256))
                {
                    _settings!.PinnedCertSha256ByEndpoint.Remove(endpointKey);
                    _settings.PairedDeviceLastUsedUtc.Remove(endpointKey);
                }
                else
                {
                    _settings!.PinnedCertSha256ByEndpoint[endpointKey] = sha256;
                    _settings.PairedDeviceLastUsedUtc[endpointKey] = DateTimeOffset.UtcNow;
                }
                SaveUnsafe();
            }
        }

        public IReadOnlyList<PairedDeviceRecord> GetPairedDevices()
        {
            lock (_gate)
            {
                EnsureLoaded();
                return _settings!.PinnedCertSha256ByEndpoint.Keys
                    .OrderBy(key => key, StringComparer.OrdinalIgnoreCase)
                    .Select(key => new PairedDeviceRecord(
                        key,
                        _settings.PairedDeviceLastUsedUtc.TryGetValue(key, out var lastUsed) ? lastUsed : null,
                        null))
                    .ToArray();
            }
        }

        public void RevokePairedDevice(string endpointKey)
            => SetPinnedCertSha256(endpointKey, null);

        public byte[]? GetBackupSeed(string seedId)
        {
            if (string.IsNullOrWhiteSpace(seedId)) return null;

            lock (_gate)
            {
                EnsureLoaded();
                return _secretStore.GetSecret(seedId);
            }
        }

        public void SetBackupSeed(string seedId, ReadOnlySpan<byte> seed)
        {
            if (string.IsNullOrWhiteSpace(seedId))
                throw new ArgumentException("A seed identifier is required.", nameof(seedId));

            lock (_gate)
            {
                EnsureLoaded();
                _secretStore.SetSecret(seedId, seed);
            }
        }

        public void RemoveBackupSeed(string seedId)
        {
            if (string.IsNullOrWhiteSpace(seedId)) return;

            lock (_gate)
            {
                EnsureLoaded();
                _secretStore.RemoveSecret(seedId);
            }
        }

        public bool GetConfirmOnDelete()
        {
            lock (_gate)
            {
                EnsureLoaded();
                return _settings!.ConfirmOnDelete;
            }
        }

        public void SetConfirmOnDelete(bool value)
        {
            lock (_gate)
            {
                EnsureLoaded();
                _settings!.ConfirmOnDelete = value;
                SaveUnsafe();
            }
        }

        public bool GetConfirmOnExit()
        {
            lock (_gate)
            {
                EnsureLoaded();
                return _settings!.ConfirmOnExit;
            }
        }

        public void SetConfirmOnExit(bool value)
        {
            lock (_gate)
            {
                EnsureLoaded();
                _settings!.ConfirmOnExit = value;
                SaveUnsafe();
            }
        }

        public AppTheme GetTheme()
        {
            lock (_gate)
            {
                EnsureLoaded();
                return _settings!.Theme;
            }
        }

        public void SetTheme(AppTheme value)
        {
            lock (_gate)
            {
                EnsureLoaded();
                _settings!.Theme = value;
                SaveUnsafe();
            }
        }

        public bool GetCompactSidebar()
        {
            lock (_gate)
            {
                EnsureLoaded();
                return _settings!.CompactSidebar;
            }
        }

        public void SetCompactSidebar(bool value)
        {
            lock (_gate)
            {
                EnsureLoaded();
                _settings!.CompactSidebar = value;
                SaveUnsafe();
            }
        }

        public Task SaveDesktopPreferencesAsync(
            bool confirmOnDelete,
            bool confirmOnExit,
            AppTheme theme,
            bool compactSidebar,
            CancellationToken cancellationToken = default)
            => Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                lock (_gate)
                {
                    EnsureLoaded();
                    _settings!.ConfirmOnDelete = confirmOnDelete;
                    _settings.ConfirmOnExit = confirmOnExit;
                    _settings.Theme = theme;
                    _settings.CompactSidebar = compactSidebar;
                    SaveUnsafe();
                }
            }, cancellationToken);

        private static string GetDefaultPath()
        {
            var directory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "VcfEditor");
            return Path.Combine(directory, Constants.SettingsFileName);
        }

        private void EnsureLoaded()
        {
            if (_settings is not null) return;

            if (!File.Exists(_settingsPath))
            {
                _settings = new SettingsModel();
                return;
            }

            try
            {
                var json = File.ReadAllText(_settingsPath);
                var deserialized = JsonSerializer.Deserialize<SettingsModel>(json, JsonOptions) ?? new SettingsModel();
                deserialized.PinnedCertSha256ByEndpoint = new Dictionary<string, string>(
                    deserialized.PinnedCertSha256ByEndpoint ?? new Dictionary<string, string>(),
                    StringComparer.OrdinalIgnoreCase);
                deserialized.PairedDeviceLastUsedUtc = new Dictionary<string, DateTimeOffset>(
                    deserialized.PairedDeviceLastUsedUtc ?? new Dictionary<string, DateTimeOffset>(),
                    StringComparer.OrdinalIgnoreCase);
                _settings = deserialized;
                MigrateLegacySeedsUnsafe();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
            {
                var corruptPath = BuildCorruptPath(_settingsPath);
                TryMoveCorruptFile(_settingsPath, corruptPath);
                LogMessages.SettingsFileCorrupt(_logger, ex, corruptPath);
                _settings = new SettingsModel();
            }
        }

        private void MigrateLegacySeedsUnsafe()
        {
            var legacySeeds = _settings!.BackupSeedById;
            if (legacySeeds is null || legacySeeds.Count == 0)
            {
                _settings.BackupSeedById = null;
                return;
            }

            var migratedCount = 0;
            foreach (var pair in legacySeeds)
            {
                byte[]? seed = null;
                try
                {
                    seed = Convert.FromBase64String(pair.Value);
                    _secretStore.SetSecret(pair.Key, seed);
                    migratedCount++;
                }
                catch (FormatException ex)
                {
                    LogMessages.LegacySeedMalformed(_logger, ex, pair.Key);
                }
                finally
                {
                    if (seed is not null)
                        CryptographicOperations.ZeroMemory(seed);
                }
            }

            _settings.BackupSeedById = null;
            SaveUnsafe();
            LogMessages.LegacySeedsMigrated(_logger, migratedCount);
        }

        private void SaveUnsafe()
        {
            var json = JsonSerializer.Serialize(_settings, JsonOptions);
            AtomicWrite(_settingsPath, json);
        }

        private static void AtomicWrite(string path, string content)
        {
            var tempPath = path + ".tmp";
            File.WriteAllText(tempPath, content, Encoding.UTF8);
            try
            {
                if (File.Exists(path))
                {
                    try
                    {
                        File.Replace(tempPath, path, path + ".bak", ignoreMetadataErrors: true);
                    }
                    catch (PlatformNotSupportedException)
                    {
                        File.Move(tempPath, path, overwrite: true);
                    }
                    catch (IOException)
                    {
                        File.Move(tempPath, path, overwrite: true);
                    }
                }
                else
                {
                    File.Move(tempPath, path);
                }
            }
            finally
            {
                if (File.Exists(tempPath))
                    File.Delete(tempPath);
            }
        }

        private static string BuildCorruptPath(string path)
        {
            var directory = Path.GetDirectoryName(path) ?? string.Empty;
            var name = Path.GetFileNameWithoutExtension(path);
            var extension = Path.GetExtension(path);
            return Path.Combine(directory, $"{name}.corrupt-{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}{extension}");
        }

        private static void TryMoveCorruptFile(string source, string destination)
        {
            try
            {
                File.Move(source, destination);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Recovery still proceeds with defaults; the original file is left in place.
            }
        }

        private sealed class SettingsModel
        {
            public Dictionary<string, string> PinnedCertSha256ByEndpoint { get; set; } =
                new(StringComparer.OrdinalIgnoreCase);

            [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
            public Dictionary<string, string>? BackupSeedById { get; set; }

            public bool ConfirmOnDelete { get; set; } = true;
            public bool ConfirmOnExit { get; set; }
            public AppTheme Theme { get; set; } = AppTheme.System;
            public bool CompactSidebar { get; set; }
            public Dictionary<string, DateTimeOffset> PairedDeviceLastUsedUtc { get; set; } =
                new(StringComparer.OrdinalIgnoreCase);
        }
    }
}
