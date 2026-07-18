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

        public string ConfigHint => "App Id (appId) and Secret Key (secretKey)";

        public bool IsConfigured(IReadOnlyDictionary<string, string?> config) =>
            Value(config, "appId") is not null && Value(config, "secretKey") is not null;

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

        /// <summary>
        /// Cashfree order refund (POST {base}/pg/orders/{order_id}/refunds). Cashfree keys
        /// refunds by the order, not the payment — the webhook only ever gives us the
        /// settled transaction id (PaymentsWebhookController.Cashfree), so that's what's
        /// passed here as the order reference. Verify this still matches Cashfree's current
        /// API once real production credentials are configured.
        /// </summary>
        public async Task<RefundResult> RefundAsync(
            string gatewayPaymentId,
            decimal amount,
            string currency,
            IReadOnlyDictionary<string, string?> config,
            CancellationToken cancellationToken)
        {
            var baseUrl = string.Equals(Value(config, "mode"), "live", StringComparison.OrdinalIgnoreCase)
                ? "https://api.cashfree.com"
                : "https://sandbox.cashfree.com";
            var refundId = $"RFND-{Guid.NewGuid():N}"[..24];

            var payload = JsonSerializer.Serialize(new
            {
                refund_amount = amount,
                refund_id = refundId,
                refund_note = "The Reader Nest — refund",
            });

            var client = _httpClientFactory.CreateClient(nameof(CashfreeGateway));
            using var request = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/pg/orders/{gatewayPaymentId}/refunds");
            request.Headers.Add("x-client-id", config["appId"]!);
            request.Headers.Add("x-client-secret", config["secretKey"]!);
            request.Headers.Add("x-api-version", ApiVersion);
            request.Content = new StringContent(payload, Encoding.UTF8, "application/json");

            var response = await client.SendAsync(request, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "Cashfree refund failed for order {Order}: {Status} {Body}",
                    gatewayPaymentId, (int)response.StatusCode, body);
                throw new DomainValidationException(
                    $"The payment gateway rejected the refund ({(int)response.StatusCode}). Please try again or contact support.");
            }

            using var json = JsonDocument.Parse(body);
            var id = json.RootElement.TryGetProperty("cf_refund_id", out var cfId)
                ? cfId.ToString()
                : refundId;
            return new RefundResult { GatewayRefundId = id };
        }

        public async Task<GatewayPaymentStatus> GetPaymentStatusAsync(
            string gatewayReference,
            IReadOnlyDictionary<string, string?> config,
            CancellationToken cancellationToken)
        {
            // Our Cashfree link ids are minted as "RN-…"; anything else isn't ours.
            if (string.IsNullOrWhiteSpace(gatewayReference) || !gatewayReference.StartsWith("RN-", StringComparison.Ordinal))
            {
                return new GatewayPaymentStatus { State = GatewayPaymentState.Unknown };
            }

            var appId = Value(config, "appId");
            var secretKey = Value(config, "secretKey");
            if (appId is null || secretKey is null)
            {
                return new GatewayPaymentStatus { State = GatewayPaymentState.Unknown };
            }

            var baseUrl = string.Equals(Value(config, "mode"), "live", StringComparison.OrdinalIgnoreCase)
                ? "https://api.cashfree.com"
                : "https://sandbox.cashfree.com";

            var client = _httpClientFactory.CreateClient(nameof(CashfreeGateway));
            using var request = new HttpRequestMessage(HttpMethod.Get, $"{baseUrl}/pg/links/{gatewayReference}");
            request.Headers.Add("x-client-id", appId);
            request.Headers.Add("x-client-secret", secretKey);
            request.Headers.Add("x-api-version", ApiVersion);

            var response = await client.SendAsync(request, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "Cashfree status lookup for {Reference} failed: {Status} {Body}",
                    gatewayReference, (int)response.StatusCode, body);
                return new GatewayPaymentStatus { State = GatewayPaymentState.Unknown };
            }

            using var json = JsonDocument.Parse(body);
            var root = json.RootElement;
            // link_status: ACTIVE | PAID | PARTIALLY_PAID | EXPIRED | CANCELLED
            var status = root.TryGetProperty("link_status", out var s) ? s.GetString() : null;

            return (status?.ToUpperInvariant()) switch
            {
                "PAID" => new GatewayPaymentStatus { State = GatewayPaymentState.Paid },
                "EXPIRED" or "CANCELLED" => new GatewayPaymentStatus { State = GatewayPaymentState.Failed },
                _ => new GatewayPaymentStatus { State = GatewayPaymentState.Pending },
            };
        }

        // Trimmed so a stray space/newline pasted into Settings → Integrations can't corrupt the auth headers.
        private static string? Value(IReadOnlyDictionary<string, string?> config, string key) =>
            config.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value) ? value.Trim() : null;
    }
}
