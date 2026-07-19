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

        public string ConfigHint => "Key Id (keyId) and Key Secret (keySecret)";

        // Tolerant to a few common field names the admin may use for the same values.
        private static string? KeyId(IReadOnlyDictionary<string, string?> c) =>
            Val(c, "keyId") ?? Val(c, "razorpayKey") ?? Val(c, "keyid") ?? Val(c, "apiKey");

        private static string? KeySecret(IReadOnlyDictionary<string, string?> c) =>
            Val(c, "keySecret") ?? Val(c, "razorpaySecret") ?? Val(c, "secret") ?? Val(c, "apiSecret");

        public bool IsConfigured(IReadOnlyDictionary<string, string?> config) =>
            KeyId(config) is not null && KeySecret(config) is not null;

        public async Task<PaymentLinkResult> CreatePaymentLinkAsync(
            Invoice invoice,
            PaymentAccount account,
            IReadOnlyDictionary<string, string?> config,
            CancellationToken cancellationToken)
        {
            var keyId = KeyId(config)!;
            var keySecret = KeySecret(config)!;
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
                // 401 = Razorpay refused the credentials themselves — a config problem the
                // centre must fix, not something the payer can retry through.
                throw new DomainValidationException(response.StatusCode == System.Net.HttpStatusCode.Unauthorized
                    ? "Razorpay rejected the centre's API credentials (401). The Key Id and Key Secret in Settings → Integrations are invalid or not from the same key pair."
                    : $"The payment gateway rejected the request ({(int)response.StatusCode}). Please try again or contact the centre.");
            }

            using var json = JsonDocument.Parse(body);
            var root = json.RootElement;
            return new PaymentLinkResult
            {
                Url = root.GetProperty("short_url").GetString()!,
                GatewayReference = root.GetProperty("id").GetString()!,
            };
        }

        /// <summary>
        /// Razorpay Orders (https://api.razorpay.com/v1/orders) for the in-page Standard
        /// Checkout popup: the browser opens checkout.js against this order and hands back
        /// a payment id + signature that <see cref="VerifyInlineCheckoutSignature"/> proves.
        /// </summary>
        public async Task<InlineCheckoutResult?> CreateInlineCheckoutAsync(
            Invoice invoice,
            PaymentAccount account,
            InlinePayerInfo payer,
            IReadOnlyDictionary<string, string?> config,
            CancellationToken cancellationToken)
        {
            var keyId = KeyId(config)!;
            var keySecret = KeySecret(config)!;
            var remaining = invoice.Amount - invoice.AmountPaid;
            var amountMinor = (long)Math.Round(remaining * 100m); // paise

            var payload = JsonSerializer.Serialize(new
            {
                amount = amountMinor,
                currency = invoice.Currency,
                receipt = $"{invoice.InvoiceNumber}-{Guid.NewGuid():N}"[..40],
                notes = new { invoiceId = invoice.Id.ToString(), department = account.Department.ToString() },
            });

            var client = _httpClientFactory.CreateClient(nameof(RazorpayGateway));
            using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.razorpay.com/v1/orders");
            request.Headers.Add(
                "Authorization",
                "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes($"{keyId}:{keySecret}")));
            request.Content = new StringContent(payload, Encoding.UTF8, "application/json");

            var response = await client.SendAsync(request, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "Razorpay order creation failed for invoice {Invoice}: {Status} {Body}",
                    invoice.InvoiceNumber, (int)response.StatusCode, body);
                throw new DomainValidationException(response.StatusCode == System.Net.HttpStatusCode.Unauthorized
                    ? "Razorpay rejected the centre's API credentials (401). The Key Id and Key Secret in Settings → Integrations are invalid or not from the same key pair."
                    : $"The payment gateway rejected the request ({(int)response.StatusCode}). Please try again or contact the centre.");
            }

            using var json = JsonDocument.Parse(body);
            return new InlineCheckoutResult
            {
                KeyId = keyId,
                OrderId = json.RootElement.GetProperty("id").GetString()!,
                AmountMinor = amountMinor,
                Currency = invoice.Currency,
                Description = $"The Reader Nest — invoice {invoice.InvoiceNumber} ({account.Department})",
                PrefillName = payer.Name,
                PrefillEmail = payer.Email,
                PrefillContact = payer.Contact,
            };
        }

        /// <summary>
        /// Standard Checkout success proof: HMAC-SHA256("orderId|paymentId", keySecret)
        /// must equal the signature the popup returned. Constant-time compare.
        /// </summary>
        public bool? VerifyInlineCheckoutSignature(
            string orderReference,
            string gatewayPaymentId,
            string signature,
            IReadOnlyDictionary<string, string?> config)
        {
            if (!orderReference.StartsWith("order_", StringComparison.Ordinal))
            {
                return null; // not a Razorpay order — let another adapter claim it
            }

            var keySecret = KeySecret(config);
            if (keySecret is null)
            {
                return false;
            }

            using var hmac = new System.Security.Cryptography.HMACSHA256(Encoding.UTF8.GetBytes(keySecret));
            var expected = Convert.ToHexString(
                hmac.ComputeHash(Encoding.UTF8.GetBytes($"{orderReference}|{gatewayPaymentId}"))).ToLowerInvariant();

            return System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(expected),
                Encoding.UTF8.GetBytes(signature.Trim().ToLowerInvariant()));
        }

        /// <summary>Razorpay Payments Refund (https://api.razorpay.com/v1/payments/{id}/refund) — full or partial.</summary>
        public async Task<RefundResult> RefundAsync(
            string gatewayPaymentId,
            decimal amount,
            string currency,
            IReadOnlyDictionary<string, string?> config,
            CancellationToken cancellationToken)
        {
            var keyId = KeyId(config)!;
            var keySecret = KeySecret(config)!;

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

        public async Task<GatewayPaymentStatus> GetPaymentStatusAsync(
            string gatewayReference,
            IReadOnlyDictionary<string, string?> config,
            CancellationToken cancellationToken)
        {
            // Razorpay references: payment links are "plink_…", inline-checkout orders "order_…".
            if (string.IsNullOrWhiteSpace(gatewayReference))
            {
                return new GatewayPaymentStatus { State = GatewayPaymentState.Unknown };
            }

            var isLink = gatewayReference.StartsWith("plink_", StringComparison.Ordinal);
            var isOrder = gatewayReference.StartsWith("order_", StringComparison.Ordinal);
            if (!isLink && !isOrder)
            {
                return new GatewayPaymentStatus { State = GatewayPaymentState.Unknown };
            }

            var keyId = KeyId(config);
            var keySecret = KeySecret(config);
            if (keyId is null || keySecret is null)
            {
                return new GatewayPaymentStatus { State = GatewayPaymentState.Unknown };
            }

            if (isOrder)
            {
                return await GetOrderStatusAsync(gatewayReference, keyId, keySecret, cancellationToken);
            }

            var client = _httpClientFactory.CreateClient(nameof(RazorpayGateway));
            using var request = new HttpRequestMessage(
                HttpMethod.Get, $"https://api.razorpay.com/v1/payment_links/{gatewayReference}");
            request.Headers.Add(
                "Authorization",
                "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes($"{keyId}:{keySecret}")));

            var response = await client.SendAsync(request, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "Razorpay status lookup for {Reference} failed: {Status} {Body}",
                    gatewayReference, (int)response.StatusCode, body);
                return new GatewayPaymentStatus { State = GatewayPaymentState.Unknown };
            }

            using var json = JsonDocument.Parse(body);
            var root = json.RootElement;
            // status: created | partially_paid | paid | cancelled | expired
            var status = root.TryGetProperty("status", out var s) ? s.GetString() : null;

            return status switch
            {
                "paid" => new GatewayPaymentStatus
                {
                    State = GatewayPaymentState.Paid,
                    PaymentId = FirstPaymentId(root),
                },
                "cancelled" or "expired" => new GatewayPaymentStatus { State = GatewayPaymentState.Failed },
                _ => new GatewayPaymentStatus { State = GatewayPaymentState.Pending },
            };
        }

        /// <summary>
        /// Order-based status (inline checkout): a captured payment on the order settles it —
        /// the pull-based safety net for popups closed after paying but before the verify call.
        /// Orders don't expire like links, so an unpaid one just stays Pending.
        /// </summary>
        private async Task<GatewayPaymentStatus> GetOrderStatusAsync(
            string orderId,
            string keyId,
            string keySecret,
            CancellationToken cancellationToken)
        {
            var client = _httpClientFactory.CreateClient(nameof(RazorpayGateway));
            using var request = new HttpRequestMessage(
                HttpMethod.Get, $"https://api.razorpay.com/v1/orders/{orderId}/payments");
            request.Headers.Add(
                "Authorization",
                "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes($"{keyId}:{keySecret}")));

            var response = await client.SendAsync(request, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "Razorpay order-payments lookup for {Order} failed: {Status} {Body}",
                    orderId, (int)response.StatusCode, body);
                return new GatewayPaymentStatus { State = GatewayPaymentState.Unknown };
            }

            using var json = JsonDocument.Parse(body);
            if (json.RootElement.TryGetProperty("items", out var items) && items.ValueKind == JsonValueKind.Array)
            {
                foreach (var payment in items.EnumerateArray())
                {
                    var status = payment.TryGetProperty("status", out var s) ? s.GetString() : null;
                    if (status is "captured")
                    {
                        return new GatewayPaymentStatus
                        {
                            State = GatewayPaymentState.Paid,
                            PaymentId = payment.TryGetProperty("id", out var pid) ? pid.GetString() : null,
                        };
                    }
                }
            }

            return new GatewayPaymentStatus { State = GatewayPaymentState.Pending };
        }

        /// <summary>The captured payment id from a paid link's payments array, if present.</summary>
        private static string? FirstPaymentId(JsonElement linkRoot)
        {
            if (linkRoot.TryGetProperty("payments", out var payments)
                && payments.ValueKind == JsonValueKind.Array
                && payments.GetArrayLength() > 0
                && payments[0].TryGetProperty("payment_id", out var pid))
            {
                return pid.GetString();
            }

            return null;
        }

        // Trimmed so a stray space/newline pasted into Settings → Integrations can't corrupt Basic auth.
        private static string? Val(IReadOnlyDictionary<string, string?> config, string key) =>
            config.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value) ? value.Trim() : null;
    }
}
