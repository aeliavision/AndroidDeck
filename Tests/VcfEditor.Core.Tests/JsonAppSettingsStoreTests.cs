using System.Security.Cryptography;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using VcfEditor.Core.Security;
using VcfEditor.Core.Settings;

namespace VcfEditor.Core.Tests;

public sealed class JsonAppSettingsStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"androiddeck-settings-{Guid.NewGuid():N}");

    public JsonAppSettingsStoreTests()
    {
        Directory.CreateDirectory(_directory);
    }

    [Fact]
    public void SettingsRoundTripPersistsPinsAndPreferencesWithoutTempFiles()
    {
        var path = Path.Combine(_directory, "settings.json");
        var secrets = new InMemorySecretStore();
        var writer = new JsonAppSettingsStore(path, secrets, NullLogger<JsonAppSettingsStore>.Instance);

        writer.SetPinnedCertSha256("phone:8732", "AA:BB:CC");
        writer.SetConfirmOnDelete(false);
        writer.SetConfirmOnExit(true);

        var reader = new JsonAppSettingsStore(path, secrets, NullLogger<JsonAppSettingsStore>.Instance);

        reader.GetPinnedCertSha256("phone:8732").Should().Be("AA:BB:CC");
        reader.GetConfirmOnDelete().Should().BeFalse();
        reader.GetConfirmOnExit().Should().BeTrue();
        File.Exists(path + ".tmp").Should().BeFalse();
    }

    [Fact]
    public void LoadingLegacySettingsMigratesPlaintextSeedIntoSecretStore()
    {
        var path = Path.Combine(_directory, "settings.json");
        var seed = RandomNumberGenerator.GetBytes(32);
        var seedBase64 = Convert.ToBase64String(seed);
        File.WriteAllText(path, $$"""
            {
              "PinnedCertSha256ByEndpoint": {},
              "BackupSeedById": { "seed-1": "{{seedBase64}}" },
              "ConfirmOnDelete": true,
              "ConfirmOnExit": false
            }
            """);
        var secrets = new InMemorySecretStore();
        var store = new JsonAppSettingsStore(path, secrets, NullLogger<JsonAppSettingsStore>.Instance);

        var migrated = store.GetBackupSeed("seed-1");

        migrated.Should().Equal(seed);
        File.ReadAllText(path).Should().NotContain(seedBase64).And.NotContain("BackupSeedById");
        CryptographicOperations.ZeroMemory(seed);
        CryptographicOperations.ZeroMemory(migrated!);
    }

    [Fact]
    public void LoadingSettingsWithNullDeviceMapsUsesEmptyCollections()
    {
        var path = Path.Combine(_directory, "settings.json");
        File.WriteAllText(path, """
            {
              "PinnedCertSha256ByEndpoint": null,
              "PairedDeviceLastUsedUtc": null
            }
            """);
        var store = new JsonAppSettingsStore(
            path,
            new InMemorySecretStore(),
            NullLogger<JsonAppSettingsStore>.Instance);

        store.GetPinnedCertSha256("phone:8732").Should().BeNull();
        store.GetPairedDevices().Should().BeEmpty();
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
    }

    private sealed class InMemorySecretStore : ISecretStore
    {
        private readonly Dictionary<string, byte[]> _values = new(StringComparer.OrdinalIgnoreCase);

        public byte[]? GetSecret(string key) =>
            _values.TryGetValue(key, out var value) ? value.ToArray() : null;

        public void SetSecret(string key, ReadOnlySpan<byte> secret) =>
            _values[key] = secret.ToArray();

        public void RemoveSecret(string key) => _values.Remove(key);
    }
}
