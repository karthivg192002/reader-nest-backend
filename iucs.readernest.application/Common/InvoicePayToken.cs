using System.Security.Cryptography;
using System.Text;

namespace iucs.readernest.application.Common
{
    /// <summary>
    /// The code in a portal payment link (/pay/{code}) for an invoice that has no admission demo
    /// behind it (Payments "Payment link", manual admission). Stateless and tamper-proof: the
    /// invoice id plus a short HMAC over it with the server's signing key, so it needs no stored
    /// token and can't be guessed or edited to reach someone else's invoice. Starts with "i", which
    /// never begins a demo booking's own (hex) payment token, so the two kinds can't collide.
    /// </summary>
    public static class InvoicePayToken
    {
        private const char Prefix = 'i';
        private const int SignatureLength = 16;
        // Only for a deployment with no Jwt:SigningKey (local dev / tests); production always sets one.
        private const string FallbackSecret = "reader-nest-invoice-pay-link";

        public static string Create(Guid invoiceId, string? secret) =>
            Prefix + invoiceId.ToString("N") + Sign(invoiceId, secret);

        public static bool LooksLikeInvoiceToken(string token) =>
            token.Length == 1 + 32 + SignatureLength && token[0] == Prefix;

        /// <summary>The invoice id, or null for anything that isn't a genuine, unaltered code.</summary>
        public static Guid? Read(string token, string? secret)
        {
            if (!LooksLikeInvoiceToken(token) || !Guid.TryParseExact(token.Substring(1, 32), "N", out var invoiceId))
            {
                return null;
            }

            var expected = Encoding.ASCII.GetBytes(Sign(invoiceId, secret));
            var actual = Encoding.ASCII.GetBytes(token[33..].ToLowerInvariant());
            return CryptographicOperations.FixedTimeEquals(expected, actual) ? invoiceId : null;
        }

        private static string Sign(Guid invoiceId, string? secret)
        {
            var key = Encoding.UTF8.GetBytes(string.IsNullOrWhiteSpace(secret) ? FallbackSecret : secret);
            var hash = HMACSHA256.HashData(key, Encoding.UTF8.GetBytes("invoice-pay:" + invoiceId.ToString("N")));
            return Convert.ToHexString(hash).ToLowerInvariant()[..SignatureLength];
        }
    }
}
