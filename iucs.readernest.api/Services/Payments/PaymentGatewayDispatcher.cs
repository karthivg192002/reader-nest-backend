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
            CancellationToken cancellationToken = default)
        {
            var adapter = ResolveAdapter(account);
            if (adapter is null)
            {
                _logger.LogInformation(
                    "No gateway adapter matches provider '{Provider}' for account {Account}; using the simulated gateway.",
                    account.GatewayProvider, account.Name);
                return await _simulated.CreatePaymentLinkAsync(invoice, account, cancellationToken);
            }

            var integration = await _unitOfWork.Repository<Integration>().Query()
                .FirstOrDefaultAsync(i => i.Key == adapter.IntegrationKey, cancellationToken);
            var config = DecodeConfig(integration?.ConfigJson);

            var live = integration is not null
                && integration.IsEnabled
                && adapter.RequiredConfigKeys.All(k =>
                    config.TryGetValue(k, out var v) && !string.IsNullOrWhiteSpace(v));

            if (!live)
            {
                _logger.LogInformation(
                    "Integration '{Key}' is disabled or missing credentials; using the simulated gateway for invoice {Invoice}.",
                    adapter.IntegrationKey, invoice.InvoiceNumber);
                return await _simulated.CreatePaymentLinkAsync(invoice, account, cancellationToken);
            }

            return await adapter.CreatePaymentLinkAsync(invoice, account, config, cancellationToken);
        }

        private IGatewayAdapter? ResolveAdapter(PaymentAccount account)
        {
            var provider = (account.GatewayProvider ?? string.Empty).ToLowerInvariant();
            return _adapters.FirstOrDefault(a => provider.Contains(a.IntegrationKey));
        }

        private static Dictionary<string, string?> DecodeConfig(string? configJson) =>
            string.IsNullOrWhiteSpace(configJson)
                ? new Dictionary<string, string?>()
                : JsonSerializer.Deserialize<Dictionary<string, string?>>(configJson) ?? new Dictionary<string, string?>();
    }
}
