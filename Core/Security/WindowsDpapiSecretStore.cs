using VcfEditor.Helpers;
using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace VcfEditor.Core.Security
{
    public sealed class WindowsDpapiSecretStore : ISecretStore
    {
        private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("AndroidDeck.BackupSeed.v1");
        private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

        private readonly object _gate = new();
        private readonly string _secretsPath;
        private readonly ILogger<WindowsDpapiSecretStore> _logger;
        private readonly Func<byte[], byte[]> _protect;
        private readonly Func<byte[], byte[]> _unprotect;
        private SecretFileModel? _model;

        public WindowsDpapiSecretStore(ILogger<WindowsDpapiSecretStore> logger)
            : this(
                GetDefaultPath(),
                logger,
                bytes => ProtectedData.Protect(bytes, Entropy, DataProtectionScope.CurrentUser),
                bytes => ProtectedData.Unprotect(bytes, Entropy, DataProtectionScope.CurrentUser))
        {
        }

        internal WindowsDpapiSecretStore(
            string secretsPath,
            ILogger<WindowsDpapiSecretStore> logger,
            Func<byte[], byte[]> protect,
            Func<byte[], byte[]> unprotect)
        {
            if (string.IsNullOrWhiteSpace(secretsPath))
                throw new ArgumentException("A secrets file path is required.", nameof(secretsPath));

            _secretsPath = secretsPath;
            ArgumentNullException.ThrowIfNull(logger);
            _logger = logger;
            ArgumentNullException.ThrowIfNull(protect);
            _protect = protect;
            ArgumentNullException.ThrowIfNull(unprotect);
            _unprotect = unprotect;

            var directory = Path.GetDirectoryName(_secretsPath);
            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);
        }

        public byte[]? GetSecret(string key)
        {
            if (string.IsNullOrWhiteSpace(key)) return null;

            lock (_gate)
            {
                EnsureLoaded();
                if (!_model!.ProtectedSecretsById.TryGetValue(key, out var encoded))
                    return null;

                byte[]? protectedBytes = null;
                try
                {
                    protectedBytes = Convert.FromBase64String(encoded);
                    var plaintext = _unprotect(protectedBytes);
                    return ReferenceEquals(plaintext, protectedBytes)
                        ? plaintext.ToArray()
                        : plaintext;
                }
                catch (Exception ex) when (ex is FormatException or CryptographicException)
                {
                    LogMessages.SecretDecryptFailed(_logger, ex, key);
                    return null;
                }
                finally
                {
                    if (protectedBytes is not null)
                        CryptographicOperations.ZeroMemory(protectedBytes);
                }
            }
        }

        public void SetSecret(string key, ReadOnlySpan<byte> secret)
        {
            if (string.IsNullOrWhiteSpace(key))
                throw new ArgumentException("A secret identifier is required.", nameof(key));
            if (secret.IsEmpty)
                throw new ArgumentException("Secret data cannot be empty.", nameof(secret));

            lock (_gate)
            {
                EnsureLoaded();

                var plaintext = secret.ToArray();
                byte[]? protectedBytes = null;
                try
                {
                    var protectedResult = _protect(plaintext);
                    protectedBytes = ReferenceEquals(protectedResult, plaintext)
                        ? protectedResult.ToArray()
                        : protectedResult;
                    _model!.ProtectedSecretsById[key] = Convert.ToBase64String(protectedBytes);
                    SaveUnsafe();
                }
                finally
                {
                    CryptographicOperations.ZeroMemory(plaintext);
                    if (protectedBytes is not null)
                        CryptographicOperations.ZeroMemory(protectedBytes);
                }
            }
        }

        public void RemoveSecret(string key)
        {
            if (string.IsNullOrWhiteSpace(key)) return;

            lock (_gate)
            {
                EnsureLoaded();
                if (_model!.ProtectedSecretsById.Remove(key))
                    SaveUnsafe();
            }
        }

        private static string GetDefaultPath()
        {
            var directory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "VcfEditor");
            return Path.Combine(directory, "secrets.json");
        }

        private void EnsureLoaded()
        {
            if (_model is not null) return;

            if (!File.Exists(_secretsPath))
            {
                _model = new SecretFileModel();
                return;
            }

            try
            {
                var json = File.ReadAllText(_secretsPath);
                var deserialized = JsonSerializer.Deserialize<SecretFileModel>(json) ?? new SecretFileModel();
                deserialized.ProtectedSecretsById = new Dictionary<string, string>(
                    deserialized.ProtectedSecretsById,
                    StringComparer.OrdinalIgnoreCase);
                _model = deserialized;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
            {
                var corruptPath = BuildCorruptPath(_secretsPath);
                TryMoveCorruptFile(_secretsPath, corruptPath);
                LogMessages.SecretStoreCorrupt(_logger, ex, corruptPath);
                _model = new SecretFileModel();
            }
        }

        private void SaveUnsafe()
        {
            var json = JsonSerializer.Serialize(_model, JsonOptions);
            AtomicWrite(_secretsPath, json);
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

        private sealed class SecretFileModel
        {
            public Dictionary<string, string> ProtectedSecretsById { get; set; } =
                new(StringComparer.OrdinalIgnoreCase);
        }
    }
}
