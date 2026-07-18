using System.Text;
using System.Text.Json;
using iucs.readernest.application.Common.Exceptions;
using iucs.readernest.application.Common.Interfaces;
using iucs.readernest.domain.Entities.Billing;

namespace iucs.readernest.api.Services.Payments
{
    /// <summary>
    /// Razorpay Payment Links (https://api.razorpay.com/v1/payment_links). The link id
    /// (plink_…) is the gateway reference; the payment_link.paid webhook settles on it.
    /// Test vs live is decided by the keys the admin configures (rzp_test_… / rzp_live_…).
    /// </summary>
    public class RazorpayGateway : IGatewayAdapter
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILogger<RazorpayGateway> _logger;

        public RazorpayGateway(IHttpClientFactory httpClientFactory, ILogger<RazorpayGateway> logger)
        {
            _httpClientFactory = httpClientFactory;
            _logger = logger;
        }

        public string IntegrationKey => "razorpay";

        public IReadOnlyList<string> RequiredConfigKeys { get; } = ["keyId", "keySecret"];

        public async Task<PaymentLinkResult> CreatePaymentLinkAsync(
            Invoice invoice,
            PaymentAccount account,
            IReadOnlyDictionary<string, string?> config,
            CancellationToken cancellationToken)
        {
            var keyId = config["keyId"]!;
            var keySecret = config["keySecret"]!;
            var remaining = invoice.Amount - invoice.AmountPaid;

            var payload = JsonSerializer.Serialize(new
            {
                amount = (long)Math.Round(remaining * 100m), // paise
                currency = invoice.Currency,
                reference_id = $"{invoice.InvoiceNumber}-{Guid.NewGuid():N}"[..40],
                description = $"The Reader Nest — invoice {invoice.InvoiceNumber} ({account.Department})",
                notes = new { invoiceId = invoice.Id.ToString(), department = account.Department.ToString() },
            });

            var client = _httpClientFactory.CreateClient(nameof(RazorpayGateway));
            using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.razorpay.com/v1/payment_links");
            request.Headers.Add(
                "Authorization",
                "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes($"{keyId}:{keySecret}")));
            request.Content = new StringContent(payload, Encoding.UTF8, "application/json");

            var response = await client.SendAsync(request, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "Razorpay payment-link creation failed for invoice {Invoice}: {Status} {Body}",
                    invoice.InvoiceNumber, (int)response.StatusCode, body);
                throw new DomainValidationException(
                    $"The payment gateway rejected the request ({(int)response.StatusCode}). Please try again or contact the centre.");
            }

            using var json = JsonDocument.Parse(body);
            var root = json.RootElement;
            return new PaymentLinkResult
            {
                Url = root.GetProperty("short_url").GetString()!,
                GatewayReference = root.GetProperty("id").GetString()!,
            };
        }

        /// <summary>Razorpay Payments Refund (https://api.razorpay.com/v1/payments/{id}/refund) — full or partial.</summary>
        public async Task<RefundResult> RefundAsync(
            string gatewayPaymentId,
            decimal amount,
            string currency,
            IReadOnlyDictionary<string, string?> config,
            CancellationToken cancellationToken)
        {
            var keyId = config["keyId"]!;
            var keySecret = config["keySecret"]!;

            var payload = JsonSerializer.Serialize(new { amount = (long)Math.Round(amount * 100m) });

            var client = _httpClientFactory.CreateClient(nameof(RazorpayGateway));
            using var request = new HttpRequestMessage(
                HttpMethod.Post, $"https://api.razorpay.com/v1/payments/{gatewayPaymentId}/refund");
            request.Headers.Add(
                "Authorization",
                "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes($"{keyId}:{keySecret}")));
            request.Content = new StringContent(payload, Encoding.UTF8, "application/json");

            var response = await client.SendAsync(request, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "Razorpay refund failed for payment {Payment}: {Status} {Body}",
                    gatewayPaymentId, (int)response.StatusCode, body);
                throw new DomainValidationException(
                    $"The payment gateway rejected the refund ({(int)response.StatusCode}). Please try again or contact support.");
            }

            using var json = JsonDocument.Parse(body);
            return new RefundResult { GatewayRefundId = json.RootElement.GetProperty("id").GetString()! };
        }
    }
}
