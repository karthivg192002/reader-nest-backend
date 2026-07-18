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
    }
}
