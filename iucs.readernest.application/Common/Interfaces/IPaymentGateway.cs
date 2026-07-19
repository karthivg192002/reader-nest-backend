using iucs.readernest.domain.Entities.Billing;

namespace iucs.readernest.application.Common.Interfaces
{
    public class PaymentLinkResult
    {
        public string Url { get; set; } = null!;

        /// <summary>Gateway-side reference for reconciling the eventual webhook/callback.</summary>
        public string GatewayReference { get; set; } = null!;

        /// <summary>
        /// Set when a payer-chosen gateway can't start a checkout (turned off, or missing
        /// keys). It carries a clear, actionable message and means <see cref="Url"/> is unset,
        /// so callers surface the reason to the payer instead of creating a pending transaction.
        /// </summary>
        public string? UnavailableReason { get; set; }
    }

    /// <summary>What a gateway reports when we poll a checkout link's current state.</summary>
    public enum GatewayPaymentState
    {
        /// <summary>Reference isn't this provider's, or its status can't be read — leave the transaction untouched.</summary>
        Unknown,

        /// <summary>Link created but not yet paid.</summary>
        Pending,

        /// <summary>Payment completed at the gateway — settle the invoice.</summary>
        Paid,

        /// <summary>Link cancelled/expired — mark the attempt failed.</summary>
        Failed,
    }

    public class GatewayPaymentStatus
    {
        public GatewayPaymentState State { get; set; } = GatewayPaymentState.Unknown;

        /// <summary>Concrete payment id (e.g. Razorpay pay_…), when the gateway reports one.</summary>
        public string? PaymentId { get; set; }
    }

    public class RefundResult
    {
        /// <summary>The gateway's refund id (e.g. Razorpay "rfnd_…"); recorded on the Refund row.</summary>
        public string GatewayRefundId { get; set; } = null!;
    }

    /// <summary>
    /// Everything the browser needs to open the gateway's in-page checkout popup
    /// (Razorpay Standard Checkout over an Order). The order id is the gateway
    /// reference the pending transaction is keyed on.
    /// </summary>
    public class InlineCheckoutResult
    {
        public string? KeyId { get; set; }

        /// <summary>Gateway order reference (e.g. Razorpay "order_…").</summary>
        public string? OrderId { get; set; }

        /// <summary>Amount in the currency's minor unit (paise for INR).</summary>
        public long AmountMinor { get; set; }

        public string Currency { get; set; } = "INR";

        public string? Description { get; set; }

        public string? PrefillName { get; set; }

        public string? PrefillEmail { get; set; }

        public string? PrefillContact { get; set; }

        /// <summary>
        /// Set when this gateway can't run an in-page checkout (unsupported provider,
        /// turned off, or missing keys); callers fall back or surface the reason.
        /// </summary>
        public string? UnavailableReason { get; set; }
    }

    /// <summary>Who is paying, for the checkout popup's prefill fields.</summary>
    public class InlinePayerInfo
    {
        public string? Name { get; set; }

        public string? Email { get; set; }

        public string? Contact { get; set; }
    }

    /// <summary>
    /// Payment gateway abstraction behind the dual-account requirement: every call
    /// carries the department's PaymentAccount so Phonics and Maths revenue stays
    /// separated at the gateway. Production swaps in the real provider via DI +
    /// configuration; no service-layer change is needed.
    /// </summary>
    public interface IPaymentGateway
    {
        /// <param name="preferredMethodKey">
        /// The gateway the payer explicitly chose (integration key, e.g. "razorpay").
        /// Takes precedence over the account's GatewayProvider so the Pay Now popup
        /// choice is honoured; null keeps account-based routing (admin share links).
        /// When a chosen provider is enabled but not fully configured, the result carries
        /// <see cref="PaymentLinkResult.UnavailableReason"/> rather than silently simulating.
        /// </param>
        Task<PaymentLinkResult> CreatePaymentLinkAsync(
            Invoice invoice,
            PaymentAccount account,
            string? preferredMethodKey = null,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Disburses a refund for a previously gateway-settled transaction. Routed through
        /// the same department account the original payment used, so the refund call hits
        /// the same gateway/credentials the money was collected with.
        /// </summary>
        Task<RefundResult> RefundAsync(
            PaymentTransaction transaction,
            PaymentAccount account,
            decimal amount,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Polls the gateway for the current state of a checkout link (by its gateway
        /// reference). This is the pull-based counterpart to the webhook: it lets the invoice
        /// settle from an outbound call even when the provider can't reach a webhook (local
        /// dev, or a missed/delayed webhook in production). Returns <see cref="GatewayPaymentState.Unknown"/>
        /// when the reference belongs to no configured provider.
        /// </summary>
        Task<GatewayPaymentStatus> GetPaymentStatusAsync(
            string gatewayReference,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Creates a gateway order for an in-page checkout popup (no redirect). Default:
        /// unsupported — only providers with an inline checkout (Razorpay) override this.
        /// </summary>
        Task<InlineCheckoutResult> CreateInlineCheckoutAsync(
            Invoice invoice,
            PaymentAccount account,
            string methodKey,
            InlinePayerInfo payer,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new InlineCheckoutResult
            {
                UnavailableReason = "In-page checkout is not supported by this payment gateway.",
            });

        /// <summary>
        /// Verifies the signature the checkout popup hands back after a successful payment
        /// (Razorpay: HMAC-SHA256 of "orderId|paymentId" with the key secret). False means
        /// the proof doesn't check out and the payment must NOT be settled from it.
        /// </summary>
        Task<bool> VerifyInlineCheckoutAsync(
            string orderReference,
            string gatewayPaymentId,
            string signature,
            CancellationToken cancellationToken = default) => Task.FromResult(false);
    }
}
