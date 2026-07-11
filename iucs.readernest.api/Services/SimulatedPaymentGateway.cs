using iucs.readernest.application.Common.Interfaces;
using iucs.readernest.domain.Entities.Billing;

namespace iucs.readernest.api.Services
{
    /// <summary>
    /// Development gateway: emits a Pay Now link into the parent portal and logs the
    /// call. Production replaces this registration with the client's provider
    /// (per-department credentials from configuration); the service layer is unchanged.
    /// </summary>
    public class SimulatedPaymentGateway : IPaymentGateway
    {
        private readonly string _payBaseUrl;
        private readonly ILogger<SimulatedPaymentGateway> _logger;

        public SimulatedPaymentGateway(IConfiguration configuration, ILogger<SimulatedPaymentGateway> logger)
        {
            // Defaults to the first allowed SPA origin so the link lands on the portal's Pay Now flow
            _payBaseUrl = configuration["Payments:PayNowBaseUrl"]
                ?? configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()?.FirstOrDefault()
                ?? "http://localhost:5173";
            _logger = logger;
        }

        public Task<PaymentLinkResult> CreatePaymentLinkAsync(
            Invoice invoice,
            PaymentAccount account,
            CancellationToken cancellationToken = default)
        {
            var reference = $"SIM-{Guid.NewGuid():N}";
            _logger.LogInformation(
                "Simulated payment link for invoice {InvoiceNumber} via {Provider}/{AccountRef} ({Department}): ref {Reference}",
                invoice.InvoiceNumber, account.GatewayProvider, account.GatewayAccountRef, account.Department, reference);

            return Task.FromResult(new PaymentLinkResult
            {
                Url = $"{_payBaseUrl.TrimEnd('/')}/parent/billing?invoice={invoice.Id}&ref={reference}",
                GatewayReference = reference,
            });
        }
    }
}
