using System.Security.Cryptography;
using System.Text;
using iucs.readernest.application.Common.Interfaces;
using Microsoft.Extensions.Options;

namespace iucs.readernest.application.Helper
{
    public class PinVaultOptions
    {
        public const string SectionName = "PinVault";

        /// <summary>
        /// Dedicated secret for the PIN vault (any string of 32+ characters). Set it via the
        /// PinVault__Key environment variable. Left blank, a key is derived from
        /// <see cref="FallbackSecret"/> (the JWT signing key) with a separate context label, so the
        /// feature works without extra setup -- but rotating the JWT key then makes stored PINs
        /// unreadable (they show as "reset to view again"), so set this if that matters.
        /// </summary>
        public string? Key { get; set; }

        public string? FallbackSecret { get; set; }
    }

    /// <summary>AES-256-GCM. Stored as "v1:" + base64(nonce[12] | tag[16] | ciphertext).</summary>
    public class AesGcmPinVault : IPinVault
    {
        private const string Prefix = "v1:";
        private const int NonceSize = 12;
        private const int TagSize = 16;
        private readonly byte[] _key;

        public AesGcmPinVault(IOptions<PinVaultOptions> options)
        {
            var o = options.Value;
            var secret = !string.IsNullOrWhiteSpace(o.Key) ? o.Key : o.FallbackSecret;
            if (string.IsNullOrWhiteSpace(secret))
            {
                throw new InvalidOperationException("PinVault: neither PinVault:Key nor a fallback secret is configured.");
            }

            // HKDF gives a full-strength 256-bit key from whatever-length secret, and the label
            // keeps this key distinct from anything else derived from the same secret (JWT signing).
            _key = HKDF.DeriveKey(HashAlgorithmName.SHA256, Encoding.UTF8.GetBytes(secret), 32,
                salt: null, info: Encoding.UTF8.GetBytes("reader-nest/pin-vault/v1"));
        }

        public string Protect(string pin)
        {
            var plain = Encoding.UTF8.GetBytes(pin);
            var nonce = RandomNumberGenerator.GetBytes(NonceSize);
            var cipher = new byte[plain.Length];
            var tag = new byte[TagSize];
            using var aes = new AesGcm(_key, TagSize);
            aes.Encrypt(nonce, plain, cipher, tag);
            return Prefix + Convert.ToBase64String(nonce.Concat(tag).Concat(cipher).ToArray());
        }

        public string? TryReveal(string protectedPin)
        {
            if (string.IsNullOrEmpty(protectedPin) || !protectedPin.StartsWith(Prefix, StringComparison.Ordinal)) return null;
            try
            {
                var raw = Convert.FromBase64String(protectedPin[Prefix.Length..]);
                if (raw.Length <= NonceSize + TagSize) return null;
                var nonce = raw[..NonceSize];
                var tag = raw[NonceSize..(NonceSize + TagSize)];
                var cipher = raw[(NonceSize + TagSize)..];
                var plain = new byte[cipher.Length];
                using var aes = new AesGcm(_key, TagSize);
                aes.Decrypt(nonce, cipher, tag, plain);
                return Encoding.UTF8.GetString(plain);
            }
            catch (Exception ex) when (ex is CryptographicException or FormatException)
            {
                return null;
            }
        }
    }
}
