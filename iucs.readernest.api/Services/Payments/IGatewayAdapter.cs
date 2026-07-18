using iucs.readernest.application.Common.Interfaces;
using iucs.readernest.domain.Entities.Billing;

namespace iucs.readernest.api.Services.Payments
{
    /// <summary>
    /// Provider-specific checkout-link creation. Credentials come from the matching
    /// Settings → Integrations record's config, resolved by the dispatcher per call.
    /// </summary>
    public interface IGatewayAdapter
    {
        /// <summary>Integration key this adapter serves ("razorpay", "cashfree").</summary>
        string IntegrationKey { get; }

        /// <summary>Config keys that must be non-empty for the adapter to run live.</summary>
        IReadOnlyList<string> RequiredConfigKeys { get; }

        Task<PaymentLinkResult> CreatePaymentLinkAsync(
            Invoice invoice,
            PaymentAccount account,
            IReadOnlyDictionary<string, string?> config,
            CancellationToken cancellationToken);

        /// <param name="gatewayPaymentId">The concrete payment id captured at settlement (not the payment-link reference).</param>
        Task<RefundResult> RefundAsync(
            string gatewayPaymentId,
            decimal amount,
            string currency,
            IReadOnlyDictionary<string, string?> config,
            CancellationToken cancellationToken);
    }
}
