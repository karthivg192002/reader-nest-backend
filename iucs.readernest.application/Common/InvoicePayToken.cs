using System.Security.Cryptography;
using System.Text;
using iucs.readernest.domain.Entities.Billing;

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

        /// <summary>
        /// Issues a fresh link for <paramref name="invoice"/> (tracked; the caller saves): a new
        /// per-link code, so any earlier link for it stops working, valid for
        /// <see cref="PaymentLinkRules.ValidHours"/> hours.
        /// </summary>
        public static string Issue(Invoice invoice, string? secret)
        {
            invoice.PaymentLinkCode = Guid.NewGuid().ToString("N")[..16];
            invoice.PaymentLinkExpiresAtUtc = PaymentLinkRules.NewExpiryUtc();
            return Prefix + invoice.Id.ToString("N") + Sign(invoice.Id, invoice.PaymentLinkCode, secret);
        }

        public static bool LooksLikeInvoiceToken(string token) =>
            token.Length == 1 + 32 + SignatureLength && token[0] == Prefix;

        /// <summary>The invoice id a code claims to be for (not yet verified -- see <see cref="IsGenuine"/>).</summary>
        public static Guid? PeekInvoiceId(string token) =>
            LooksLikeInvoiceToken(token) && Guid.TryParseExact(token.Substring(1, 32), "N", out var invoiceId) ? invoiceId : null;

        /// <summary>
        /// Whether the code is an unaltered one for the invoice's CURRENT link. A link made before
        /// per-link codes existed (no code stored yet) is still accepted until the invoice gets a
        /// fresh one; after that only the newest link works.
        /// </summary>
        public static bool IsGenuine(string token, Guid invoiceId, string? currentCode, string? secret)
        {
            if (PeekInvoiceId(token) != invoiceId)
            {
                return false;
            }

            var expected = Encoding.ASCII.GetBytes(Sign(invoiceId, currentCode, secret));
            var actual = Encoding.ASCII.GetBytes(token[33..].ToLowerInvariant());
            return CryptographicOperations.FixedTimeEquals(expected, actual);
        }

        private static string Sign(Guid invoiceId, string? code, string? secret)
        {
            var key = Encoding.UTF8.GetBytes(string.IsNullOrWhiteSpace(secret) ? FallbackSecret : secret);
            var payload = "invoice-pay:" + invoiceId.ToString("N") + (code is null ? string.Empty : ":" + code);
            var hash = HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(payload));
            return Convert.ToHexString(hash).ToLowerInvariant()[..SignatureLength];
        }
    }

    /// <summary>Client (2026-10-16): a payment link is valid for 48 hours from when it's made.</summary>
    public static class PaymentLinkRules
    {
        public const int ValidHours = 48;

        public static DateTime NewExpiryUtc() => DateTime.UtcNow.AddHours(ValidHours);

        public const string ExpiredMessage =
            "This payment link has expired (links are valid for 48 hours). Please ask your counsellor for a new one.";

        /// <summary>Line added to every message staff send with a link.</summary>
        public const string ValidityNote = "Please note: this payment link is valid for 48 hours only.";
    }
}
