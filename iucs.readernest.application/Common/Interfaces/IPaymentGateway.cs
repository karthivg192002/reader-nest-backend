using iucs.readernest.domain.Entities.Billing;

namespace iucs.readernest.application.Common.Interfaces
{
    public class PaymentLinkResult
    {
        public string Url { get; set; } = null!;

        /// <summary>Gateway-side reference for reconciling the eventual webhook/callback.</summary>
        public string GatewayReference { get; set; } = null!;
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
    }
}
