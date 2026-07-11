using iucs.readernest.domain.Entities.Billing;

namespace iucs.readernest.application.Common.Interfaces
{
    public class PaymentLinkResult
    {
        public string Url { get; set; } = null!;

        /// <summary>Gateway-side reference for reconciling the eventual webhook/callback.</summary>
        public string GatewayReference { get; set; } = null!;
    }

    /// <summary>
    /// Payment gateway abstraction behind the dual-account requirement: every call
    /// carries the department's PaymentAccount so Phonics and Maths revenue stays
    /// separated at the gateway. Production swaps in the real provider via DI +
    /// configuration; no service-layer change is needed.
    /// </summary>
    public interface IPaymentGateway
    {
        Task<PaymentLinkResult> CreatePaymentLinkAsync(
            Invoice invoice,
            PaymentAccount account,
            CancellationToken cancellationToken = default);
    }
}
