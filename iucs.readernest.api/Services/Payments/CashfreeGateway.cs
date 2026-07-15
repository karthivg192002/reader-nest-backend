using System.Text;
using System.Text.Json;
using iucs.readernest.application.Common.Exceptions;
using iucs.readernest.application.Common.Interfaces;
using iucs.readernest.domain.Entities.Billing;

namespace iucs.readernest.api.Services.Payments
{
    /// <summary>
    /// Cashfree Payment Links (POST {base}/pg/links). link_id is the gateway reference;
    /// the PAYMENT_LINK_EVENT webhook settles on it. The optional "mode" config key
    /// ("live") switches from the sandbox to the production host.
    /// </summary>
    public class CashfreeGateway : IGatewayAdapter
    {
        private const string ApiVersion = "2023-08-01";

        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILogger<CashfreeGateway> _logger;

        public CashfreeGateway(IHttpClientFactory httpClientFactory, ILogger<CashfreeGateway> logger)
        {
            _httpClientFactory = httpClientFactory;
            _logger = logger;
        }

        public string IntegrationKey => "cashfree";

        public IReadOnlyList<string> RequiredConfigKeys { get; } = ["appId", "secretKey"];

        public async Task<PaymentLinkResult> CreatePaymentLinkAsync(
            Invoice invoice,
            PaymentAccount account,
            IReadOnlyDictionary<string, string?> config,
            CancellationToken cancellationToken)
        {
            var baseUrl = string.Equals(Value(config, "mode"), "live", StringComparison.OrdinalIgnoreCase)
                ? "https://api.cashfree.com"
                : "https://sandbox.cashfree.com";
            var remaining = invoice.Amount - invoice.AmountPaid;
            var linkId = $"RN-{Guid.NewGuid():N}"[..24];

            // Cashfree requires customer details on a link; phone falls back to a
            // placeholder when the parent record has none (the link still works).
            var payload = JsonSerializer.Serialize(new
            {
                link_id = linkId,
                link_amount = remaining,
                link_currency = invoice.Currency,
                link_purpose = $"The Reader Nest — invoice {invoice.InvoiceNumber} ({account.Department})",
                customer_details = new
                {
                    customer_phone = Value(config, "fallbackCustomerPhone") ?? "9999999999",
                },
                link_notes = new Dictionary<string, string>
                {
                    ["invoiceId"] = invoice.Id.ToString(),
                    ["department"] = account.Department.ToString(),
                },
            });

            var client = _httpClientFactory.CreateClient(nameof(CashfreeGateway));
            using var request = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/pg/links");
            request.Headers.Add("x-client-id", config["appId"]!);
            request.Headers.Add("x-client-secret", config["secretKey"]!);
            request.Headers.Add("x-api-version", ApiVersion);
            request.Content = new StringContent(payload, Encoding.UTF8, "application/json");

            var response = await client.SendAsync(request, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "Cashfree payment-link creation failed for invoice {Invoice}: {Status} {Body}",
                    invoice.InvoiceNumber, (int)response.StatusCode, body);
                throw new DomainValidationException(
                    $"The payment gateway rejected the request ({(int)response.StatusCode}). Please try again or contact the centre.");
            }

            using var json = JsonDocument.Parse(body);
            var root = json.RootElement;
            return new PaymentLinkResult
            {
                Url = root.GetProperty("link_url").GetString()!,
                GatewayReference = root.GetProperty("link_id").GetString()!,
            };
        }

        private static string? Value(IReadOnlyDictionary<string, string?> config, string key) =>
            config.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value) ? value : null;
    }
}
