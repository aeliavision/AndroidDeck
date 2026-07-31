using System;
using System.Security.Cryptography;
using System.Text;

namespace VcfEditor.Core.Security
{
    public sealed class PairingKeyExchange : IDisposable
    {
        private readonly ECDiffieHellman _key = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);

        public string PublicKeyBase64 => Convert.ToBase64String(_key.ExportSubjectPublicKeyInfo());

        public byte[] DeriveSecret(string serverPublicKeyBase64, string sessionId)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(serverPublicKeyBase64);
            ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
            var serverBytes = Convert.FromBase64String(serverPublicKeyBase64);
            try
            {
                using var server = ECDiffieHellman.Create();
                server.ImportSubjectPublicKeyInfo(serverBytes, out var read);
                if (read != serverBytes.Length) throw new CryptographicException("Invalid server public key.");
                var raw = _key.DeriveRawSecretAgreement(server.PublicKey);
                try
                {
                    return HkdfSha256(raw, Encoding.UTF8.GetBytes(sessionId),
                        Encoding.UTF8.GetBytes("AndroidDeck pairing v3"), 32);
                }
                finally { CryptographicOperations.ZeroMemory(raw); }
            }
            finally { CryptographicOperations.ZeroMemory(serverBytes); }
        }

        internal static byte[] HkdfSha256(byte[] ikm, byte[] salt, byte[] info, int length)
        {
            using var extract = new HMACSHA256(salt.Length == 0 ? new byte[32] : salt);
            var prk = extract.ComputeHash(ikm);
            try
            {
                var okm = new byte[length];
                var previous = Array.Empty<byte>();
                var offset = 0;
                byte counter = 1;
                while (offset < length)
                {
                    using var expand = new HMACSHA256(prk);
                    var input = new byte[previous.Length + info.Length + 1];
                    Buffer.BlockCopy(previous, 0, input, 0, previous.Length);
                    Buffer.BlockCopy(info, 0, input, previous.Length, info.Length);
                    input[^1] = counter++;
                    var current = expand.ComputeHash(input);
                    CryptographicOperations.ZeroMemory(input);
                    if (previous.Length > 0) CryptographicOperations.ZeroMemory(previous);
                    previous = current;
                    var count = Math.Min(current.Length, length - offset);
                    Buffer.BlockCopy(current, 0, okm, offset, count);
                    offset += count;
                }
                if (previous.Length > 0) CryptographicOperations.ZeroMemory(previous);
                return okm;
            }
            finally { CryptographicOperations.ZeroMemory(prk); }
        }

        public void Dispose() => _key.Dispose();
    }
}
