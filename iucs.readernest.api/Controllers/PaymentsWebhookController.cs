using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using iucs.readernest.application.Common.Exceptions;
using iucs.readernest.application.Services;
using iucs.readernest.domain.Entities.Integrations;
using iucs.readernest.domain.Repository;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace iucs.readernest.api.Controllers
{
    /// <summary>
    /// Gateway callbacks. Anonymous by necessity (gateways can't log in) — every request
    /// is authenticated by verifying the provider's HMAC signature against the secret
    /// configured in Settings → Integrations. Unknown references return 200 so gateways
    /// don't retry forever over events that aren't ours; bad signatures return 401.
    /// </summary>
    [ApiController]
    [Route("api/payments/webhook")]
    public class PaymentsWebhookController : ControllerBase
    {
        private readonly IBillingService _billingService;
        private readonly IUnitOfWork _unitOfWork;
        private readonly ILogger<PaymentsWebhookController> _logger;

        public PaymentsWebhookController(
            IBillingService billingService,
            IUnitOfWork unitOfWork,
            ILogger<PaymentsWebhookController> logger)
        {
            _billingService = billingService;
            _unitOfWork = unitOfWork;
            _logger = logger;
        }

        [HttpPost("razorpay")]
        [AllowAnonymous]
        public async Task<IActionResult> Razorpay(CancellationToken cancellationToken)
        {
            var body = await ReadBodyAsync(cancellationToken);
            var secret = await GetConfigValueAsync("razorpay", "webhookSecret", cancellationToken);
            if (string.IsNullOrWhiteSpace(secret))
            {
                _logger.LogWarning("Razorpay webhook received but no webhookSecret is configured; rejecting.");
                return Unauthorized();
            }

            var signature = Request.Headers["X-Razorpay-Signature"].FirstOrDefault();
            if (!VerifyHmacHex(body, secret, signature))
            {
                _logger.LogWarning("Razorpay webhook signature verification failed.");
                return Unauthorized();
            }

            using var json = JsonDocument.Parse(body);
            var root = json.RootElement;
            var eventName = root.TryGetProperty("event", out var ev) ? ev.GetString() : null;

            switch (eventName)
            {
                case "payment_link.paid":
                {
                    var payload = root.GetProperty("payload");
                    var linkId = payload.GetProperty("payment_link").GetProperty("entity").GetProperty("id").GetString();
                    string? paymentId = null;
                    if (payload.TryGetProperty("payment", out var payment))
                    {
                        paymentId = payment.GetProperty("entity").GetProperty("id").GetString();
                    }

                    await SettleSafelyAsync(linkId, succeeded: true, paymentId, null, cancellationToken);
                    break;
                }

                case "payment_link.expired":
                case "payment_link.cancelled":
                {
                    var linkId = root.GetProperty("payload").GetProperty("payment_link").GetProperty("entity").GetProperty("id").GetString();
                    await SettleSafelyAsync(linkId, succeeded: false, null, $"Razorpay: {eventName}", cancellationToken);
                    break;
                }

                default:
                    _logger.LogInformation("Ignoring unhandled Razorpay webhook event '{Event}'.", eventName);
                    break;
            }

            return Ok();
        }

        [HttpPost("cashfree")]
        [AllowAnonymous]
        public async Task<IActionResult> Cashfree(CancellationToken cancellationToken)
        {
            var body = await ReadBodyAsync(cancellationToken);
            var secret = await GetConfigValueAsync("cashfree", "secretKey", cancellationToken);
            if (string.IsNullOrWhiteSpace(secret))
            {
                _logger.LogWarning("Cashfree webhook received but no secretKey is configured; rejecting.");
                return Unauthorized();
            }

            var signature = Request.Headers["x-webhook-signature"].FirstOrDefault();
            var timestamp = Request.Headers["x-webhook-timestamp"].FirstOrDefault();
            if (!VerifyHmacBase64(timestamp + body, secret, signature))
            {
                _logger.LogWarning("Cashfree webhook signature verification failed.");
                return Unauthorized();
            }

            using var json = JsonDocument.Parse(body);
            var root = json.RootElement;
            var type = root.TryGetProperty("type", out var t) ? t.GetString() : null;

            if (type == "PAYMENT_LINK_EVENT" && root.TryGetProperty("data", out var data))
            {
                var linkId = data.TryGetProperty("link_id", out var l) ? l.GetString() : null;
                var status = data.TryGetProperty("link_status", out var s) ? s.GetString() : null;

                if (string.Equals(status, "PAID", StringComparison.OrdinalIgnoreCase))
                {
                    string? paymentId = null;
                    if (data.TryGetProperty("order", out var order) && order.TryGetProperty("transaction_id", out var txn))
                    {
                        paymentId = txn.ValueKind == JsonValueKind.Number ? txn.GetRawText() : txn.GetString();
                    }

                    await SettleSafelyAsync(linkId, succeeded: true, paymentId, null, cancellationToken);
                }
                else if (string.Equals(status, "EXPIRED", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(status, "CANCELLED", StringComparison.OrdinalIgnoreCase))
                {
                    await SettleSafelyAsync(linkId, succeeded: false, null, $"Cashfree: link {status}", cancellationToken);
                }
            }
            else
            {
                _logger.LogInformation("Ignoring unhandled Cashfree webhook type '{Type}'.", type);
            }

            return Ok();
        }

        private async Task SettleSafelyAsync(
            string? gatewayReference,
            bool succeeded,
            string? paymentId,
            string? failureReason,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(gatewayReference))
            {
                return;
            }

            try
            {
                await _billingService.SettleGatewayTransactionAsync(
                    gatewayReference, succeeded, paymentId, failureReason, cancellationToken);
            }
            catch (NotFoundException)
            {
                // Not one of our pending references (e.g. link created outside the platform) — acknowledge, don't retry.
                _logger.LogWarning("Webhook referenced unknown gateway reference '{Reference}'.", gatewayReference);
            }
        }

        private async Task<string> ReadBodyAsync(CancellationToken cancellationToken)
        {
            using var reader = new StreamReader(Request.Body, Encoding.UTF8);
            return await reader.ReadToEndAsync(cancellationToken);
        }

        private async Task<string?> GetConfigValueAsync(string integrationKey, string configKey, CancellationToken cancellationToken)
        {
            var integration = await _unitOfWork.Repository<Integration>().Query()
                .FirstOrDefaultAsync(i => i.Key == integrationKey, cancellationToken);
            if (integration?.ConfigJson is null)
            {
                return null;
            }

            var config = JsonSerializer.Deserialize<Dictionary<string, string?>>(integration.ConfigJson);
            return config is not null && config.TryGetValue(configKey, out var value) && !string.IsNullOrWhiteSpace(value)
                ? value
                : null;
        }

        private static bool VerifyHmacHex(string payload, string secret, string? signature)
        {
            if (string.IsNullOrWhiteSpace(signature))
            {
                return false;
            }

            var computed = Convert.ToHexString(
                HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(payload)));
            return CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(computed.ToLowerInvariant()),
                Encoding.UTF8.GetBytes(signature.Trim().ToLowerInvariant()));
        }

        private static bool VerifyHmacBase64(string payload, string secret, string? signature)
        {
            if (string.IsNullOrWhiteSpace(signature))
            {
                return false;
            }

            var computed = Convert.ToBase64String(
                HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(payload)));
            return CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(computed),
                Encoding.UTF8.GetBytes(signature.Trim()));
        }
    }
}
