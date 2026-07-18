using System.Text.Json;
using iucs.readernest.application.Common.Interfaces;
using iucs.readernest.domain.Entities.Billing;
using iucs.readernest.domain.Entities.Integrations;
using iucs.readernest.domain.Repository;
using Microsoft.EntityFrameworkCore;

namespace iucs.readernest.api.Services.Payments
{
    /// <summary>
    /// The DI IPaymentGateway. Routes each call to the adapter matching the department
    /// account's GatewayProvider, with credentials read live from the matching
    /// Settings → Integrations record. Falls back to the simulated gateway whenever the
    /// integration is disabled or its credentials are blank, so the platform works
    /// end-to-end before the client provides keys — and goes live by just filling them in.
    /// </summary>
    public class PaymentGatewayDispatcher : IPaymentGateway
    {
        private readonly IUnitOfWork _unitOfWork;
        private readonly IEnumerable<IGatewayAdapter> _adapters;
        private readonly SimulatedPaymentGateway _simulated;
        private readonly ILogger<PaymentGatewayDispatcher> _logger;

        public PaymentGatewayDispatcher(
            IUnitOfWork unitOfWork,
            IEnumerable<IGatewayAdapter> adapters,
            SimulatedPaymentGateway simulated,
            ILogger<PaymentGatewayDispatcher> logger)
        {
            _unitOfWork = unitOfWork;
            _adapters = adapters;
            _simulated = simulated;
            _logger = logger;
        }

        public async Task<PaymentLinkResult> CreatePaymentLinkAsync(
            Invoice invoice,
            PaymentAccount account,
            string? preferredMethodKey = null,
            CancellationToken cancellationToken = default)
        {
            var adapter = ResolveAdapter(account, preferredMethodKey);
            if (adapter is null)
            {
                _logger.LogInformation(
                    "No gateway adapter matches method '{Method}' / provider '{Provider}' for account {Account}; using the simulated gateway.",
                    preferredMethodKey, account.GatewayProvider, account.Name);
                return await _simulated.CreatePaymentLinkAsync(invoice, account, preferredMethodKey, cancellationToken);
            }

            var (live, config) = await ResolveLiveConfigAsync(adapter, cancellationToken);
            if (!live)
            {
                _logger.LogInformation(
                    "Integration '{Key}' is disabled or missing credentials; using the simulated gateway for invoice {Invoice}.",
                    adapter.IntegrationKey, invoice.InvoiceNumber);
                return await _simulated.CreatePaymentLinkAsync(invoice, account, preferredMethodKey, cancellationToken);
            }

            return await adapter.CreatePaymentLinkAsync(invoice, account, config, cancellationToken);
        }

        /// <summary>
        /// Disburses through whichever gateway the department account uses. Cash transactions
        /// never reach here — BillingService only calls this for gateway-settled ones, whose
        /// GatewayTransactionId carries the concrete payment id after webhook settlement
        /// ("linkRef|paymentId"); anything else (never settled by a webhook, e.g. a payment
        /// still Pending) has no real payment to refund, so it also falls back to simulated.
        /// </summary>
        public async Task<RefundResult> RefundAsync(
            PaymentTransaction transaction,
            PaymentAccount account,
            decimal amount,
            CancellationToken cancellationToken = default)
        {
            var gatewayPaymentId = ExtractSettledPaymentId(transaction.GatewayTransactionId);
            var adapter = ResolveAdapter(account, preferredMethodKey: null);

            if (adapter is null || gatewayPaymentId is null)
            {
                _logger.LogInformation(
                    "No live gateway match (or no settled payment id) for transaction {Transaction}; using the simulated refund.",
                    transaction.Id);
                return await _simulated.RefundAsync(transaction, account, amount, cancellationToken);
            }

            var (live, config) = await ResolveLiveConfigAsync(adapter, cancellationToken);
            if (!live)
            {
                _logger.LogInformation(
                    "Integration '{Key}' is disabled or missing credentials; using the simulated refund for transaction {Transaction}.",
                    adapter.IntegrationKey, transaction.Id);
                return await _simulated.RefundAsync(transaction, account, amount, cancellationToken);
            }

            return await adapter.RefundAsync(gatewayPaymentId, amount, transaction.Currency, config, cancellationToken);
        }

        /// <summary>GatewayTransactionId becomes "linkRef|paymentId" once a webhook settles it (see BillingService.SettleGatewayTransactionAsync); null before that.</summary>
        private static string? ExtractSettledPaymentId(string? gatewayTransactionId)
        {
            if (string.IsNullOrEmpty(gatewayTransactionId))
            {
                return null;
            }

            var separatorIndex = gatewayTransactionId.IndexOf('|');
            return separatorIndex >= 0 && separatorIndex < gatewayTransactionId.Length - 1
                ? gatewayTransactionId[(separatorIndex + 1)..]
                : null;
        }

        private async Task<(bool Live, Dictionary<string, string?> Config)> ResolveLiveConfigAsync(
            IGatewayAdapter adapter, CancellationToken cancellationToken)
        {
            var integration = await _unitOfWork.Repository<Integration>().Query()
                .FirstOrDefaultAsync(i => i.Key == adapter.IntegrationKey, cancellationToken);
            var config = DecodeConfig(integration?.ConfigJson);

            var live = integration is not null
                && integration.IsEnabled
                && adapter.RequiredConfigKeys.All(k =>
                    config.TryGetValue(k, out var v) && !string.IsNullOrWhiteSpace(v));

            return (live, config);
        }

        /// <summary>
        /// The payer's explicit method choice wins; the department account's GatewayProvider
        /// is the fallback for callers that don't carry one (e.g. admin share links, refunds).
        /// </summary>
        private IGatewayAdapter? ResolveAdapter(PaymentAccount account, string? preferredMethodKey)
        {
            var method = (preferredMethodKey ?? string.Empty).Trim().ToLowerInvariant();
            var byMethod = _adapters.FirstOrDefault(a => method.Contains(a.IntegrationKey));
            if (byMethod is not null)
            {
                return byMethod;
            }

            var provider = (account.GatewayProvider ?? string.Empty).ToLowerInvariant();
            return _adapters.FirstOrDefault(a => provider.Contains(a.IntegrationKey));
        }

        private static Dictionary<string, string?> DecodeConfig(string? configJson) =>
            string.IsNullOrWhiteSpace(configJson)
                ? new Dictionary<string, string?>()
                : JsonSerializer.Deserialize<Dictionary<string, string?>>(configJson) ?? new Dictionary<string, string?>();
    }
}
