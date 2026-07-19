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
            // The payer's chosen gateway wins; otherwise fall back to the account's provider.
            var explicitlyChosen = !string.IsNullOrWhiteSpace(preferredMethodKey);
            var adapter = ResolveAdapter(preferredMethodKey) ?? ResolveAdapter(account.GatewayProvider);

            if (adapter is null)
            {
                // Unknown/unset provider and no explicit gateway → demo/simulated link so the
                // platform still works before real accounts are wired.
                _logger.LogInformation(
                    "No gateway adapter matches method '{Method}' / provider '{Provider}' for account {Account}; using the simulated gateway.",
                    preferredMethodKey, account.GatewayProvider, account.Name);
                return await _simulated.CreatePaymentLinkAsync(invoice, account, preferredMethodKey, cancellationToken);
            }

            var integration = await _unitOfWork.Repository<Integration>().Query()
                .FirstOrDefaultAsync(i => i.Key == adapter.IntegrationKey, cancellationToken);
            var config = DecodeConfig(integration?.ConfigJson);
            var enabled = integration is { IsEnabled: true };
            var configured = adapter.IsConfigured(config);

            if (enabled && configured)
            {
                return await adapter.CreatePaymentLinkAsync(invoice, account, config, cancellationToken);
            }

            // The payer picked this gateway explicitly → hand back a clear reason instead of
            // silently redirecting to a fake link. Returning (not throwing) keeps this expected,
            // payer-actionable case off the exception path so the UI shows it inline.
            if (explicitlyChosen)
            {
                var reason = !enabled
                    ? $"{adapter.IntegrationKey} payments are turned off. Enable it in Settings → Integrations."
                    : $"{adapter.IntegrationKey} is not fully configured. Add its {adapter.ConfigHint} in Settings → Integrations.";

                _logger.LogInformation(
                    "Payer chose '{Key}' but it is disabled/unconfigured for invoice {Invoice}: {Reason}",
                    adapter.IntegrationKey, invoice.InvoiceNumber, reason);
                return new PaymentLinkResult { UnavailableReason = reason };
            }

            _logger.LogInformation(
                "Integration '{Key}' disabled/unconfigured; using the simulated gateway for invoice {Invoice}.",
                adapter.IntegrationKey, invoice.InvoiceNumber);
            return await _simulated.CreatePaymentLinkAsync(invoice, account, preferredMethodKey, cancellationToken);
        }

        /// <summary>
        /// In-page checkout (popup, no redirect). Always an explicit payer choice, so a
        /// disabled/unconfigured/unsupporting gateway comes back as an actionable
        /// UnavailableReason — the UI then falls back to the redirect flow or shows why.
        /// </summary>
        public async Task<InlineCheckoutResult> CreateInlineCheckoutAsync(
            Invoice invoice,
            PaymentAccount account,
            string methodKey,
            InlinePayerInfo payer,
            CancellationToken cancellationToken = default)
        {
            var adapter = ResolveAdapter(methodKey);
            if (adapter is null)
            {
                return new InlineCheckoutResult
                {
                    UnavailableReason = $"'{methodKey}' does not support in-page checkout.",
                };
            }

            var (live, config) = await ResolveLiveConfigAsync(adapter, cancellationToken);
            if (!live)
            {
                return new InlineCheckoutResult
                {
                    UnavailableReason =
                        $"{adapter.IntegrationKey} is turned off or missing its {adapter.ConfigHint} in Settings → Integrations.",
                };
            }

            var result = await adapter.CreateInlineCheckoutAsync(invoice, account, payer, config, cancellationToken);
            return result ?? new InlineCheckoutResult
            {
                UnavailableReason = $"{adapter.IntegrationKey} does not support in-page checkout.",
            };
        }

        /// <summary>
        /// Signature check for an inline-checkout success. Each adapter claims only its own
        /// order references; an unclaimed reference is treated as NOT verified.
        /// </summary>
        public async Task<bool> VerifyInlineCheckoutAsync(
            string orderReference,
            string gatewayPaymentId,
            string signature,
            CancellationToken cancellationToken = default)
        {
            foreach (var adapter in _adapters)
            {
                var (_, config) = await ResolveLiveConfigAsync(adapter, cancellationToken);
                var verdict = adapter.VerifyInlineCheckoutSignature(orderReference, gatewayPaymentId, signature, config);
                if (verdict.HasValue)
                {
                    return verdict.Value;
                }
            }

            _logger.LogWarning("No gateway adapter claimed inline-checkout reference {Reference}; treating as unverified.", orderReference);
            return false;
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
            var adapter = ResolveAdapter(account.GatewayProvider);

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

        public async Task<GatewayPaymentStatus> GetPaymentStatusAsync(
            string gatewayReference,
            CancellationToken cancellationToken = default)
        {
            // The pending transaction doesn't record which provider minted the reference, so
            // ask each configured adapter; each returns Unknown unless the reference is its own.
            foreach (var adapter in _adapters)
            {
                var integration = await _unitOfWork.Repository<Integration>().Query()
                    .FirstOrDefaultAsync(i => i.Key == adapter.IntegrationKey, cancellationToken);
                var config = DecodeConfig(integration?.ConfigJson);

                var status = await adapter.GetPaymentStatusAsync(gatewayReference, config, cancellationToken);
                if (status.State != GatewayPaymentState.Unknown)
                {
                    return status;
                }
            }

            return new GatewayPaymentStatus { State = GatewayPaymentState.Unknown };
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
            var live = integration is { IsEnabled: true } && adapter.IsConfigured(config);

            return (live, config);
        }

        /// <summary>
        /// The payer's explicit method choice wins; the department account's GatewayProvider
        /// is the fallback for callers that don't carry one (e.g. admin share links, refunds).
        /// </summary>
        private IGatewayAdapter? ResolveAdapter(string? providerKey)
        {
            var provider = (providerKey ?? string.Empty).Trim().ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(provider))
            {
                return null;
            }

            return _adapters.FirstOrDefault(a => provider.Contains(a.IntegrationKey));
        }

        private static Dictionary<string, string?> DecodeConfig(string? configJson) =>
            string.IsNullOrWhiteSpace(configJson)
                ? new Dictionary<string, string?>()
                : JsonSerializer.Deserialize<Dictionary<string, string?>>(configJson) ?? new Dictionary<string, string?>();
    }
}
