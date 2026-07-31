using System.Security.Cryptography;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using VcfEditor.Core.Security;

namespace VcfEditor.Core.Tests;

public sealed class WindowsDpapiSecretStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"androiddeck-secrets-{Guid.NewGuid():N}");

    public WindowsDpapiSecretStoreTests()
    {
        Directory.CreateDirectory(_directory);
    }

    [Fact]
    public void SetSecretPersistsProtectedPayloadAndRoundTripsPlaintext()
    {
        var path = Path.Combine(_directory, "secrets.json");
        var store = new WindowsDpapiSecretStore(
            path,
            NullLogger<WindowsDpapiSecretStore>.Instance,
            protect: bytes => bytes.Select(value => (byte)(value ^ 0xA5)).Prepend((byte)0x7F).ToArray(),
            unprotect: bytes => bytes.Skip(1).Select(value => (byte)(value ^ 0xA5)).ToArray());
        var secret = RandomNumberGenerator.GetBytes(32);
        var plaintextBase64 = Convert.ToBase64String(secret);

        store.SetSecret("seed-1", secret);
        var restored = store.GetSecret("seed-1");

        restored.Should().Equal(secret);
        File.ReadAllText(path).Should().NotContain(plaintextBase64);
        File.Exists(path + ".tmp").Should().BeFalse();
        CryptographicOperations.ZeroMemory(secret);
        CryptographicOperations.ZeroMemory(restored!);
    }

    [Fact]
    public void RemoveSecretDeletesStoredSecret()
    {
        var path = Path.Combine(_directory, "secrets.json");
        var store = new WindowsDpapiSecretStore(
            path,
            NullLogger<WindowsDpapiSecretStore>.Instance,
            protect: bytes => bytes.ToArray(),
            unprotect: bytes => bytes.ToArray());
        var secret = new byte[] { 1, 2, 3, 4 };
        store.SetSecret("seed-1", secret);

        store.RemoveSecret("seed-1");

        store.GetSecret("seed-1").Should().BeNull();
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
    }
}
