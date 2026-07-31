using System;

namespace VcfEditor.Core.Security
{
    public interface ISecretStore
    {
        byte[]? GetSecret(string key);
        void SetSecret(string key, ReadOnlySpan<byte> secret);
        void RemoveSecret(string key);
    }
}
