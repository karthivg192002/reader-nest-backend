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

        /// <summary>True when the integration config carries everything this adapter needs to charge for real.</summary>
        bool IsConfigured(IReadOnlyDictionary<string, string?> config);

        /// <summary>Human-readable list of the credentials this adapter needs, for a config error message.</summary>
        string ConfigHint { get; }

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

        /// <summary>
        /// Polls this provider for a checkout link's current state. Returns
        /// <see cref="GatewayPaymentState.Unknown"/> when the reference isn't one of this
        /// provider's (so the dispatcher can try the next adapter) or the status can't be read.
        /// </summary>
        Task<GatewayPaymentStatus> GetPaymentStatusAsync(
            string gatewayReference,
            IReadOnlyDictionary<string, string?> config,
            CancellationToken cancellationToken);
    }
}
