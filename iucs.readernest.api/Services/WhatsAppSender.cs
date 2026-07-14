using System.Text;
using System.Text.Json;
using iucs.readernest.application.Common.Exceptions;
using iucs.readernest.application.Common.Interfaces;
using iucs.readernest.domain.Entities.Integrations;
using iucs.readernest.domain.Repository;
using Microsoft.EntityFrameworkCore;

namespace iucs.readernest.api.Services
{
    /// <summary>
    /// Sends WhatsApp messages through the WhatsApp Business Cloud API using the
    /// credentials the admin configured in Settings → Integrations (the "whatsapp"
    /// record: phoneNumberId + accessToken). Throws a friendly error when the
    /// integration is disabled/unconfigured or the API rejects the send, so the
    /// caller (e.g. the resend-credentials action) can surface it to the admin.
    /// </summary>
    public class WhatsAppSender : IWhatsAppSender
    {
        private const string WhatsAppIntegrationKey = "whatsapp";

        private readonly IUnitOfWork _unitOfWork;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILogger<WhatsAppSender> _logger;

        public WhatsAppSender(
            IUnitOfWork unitOfWork,
            IHttpClientFactory httpClientFactory,
            ILogger<WhatsAppSender> logger)
        {
            _unitOfWork = unitOfWork;
            _httpClientFactory = httpClientFactory;
            _logger = logger;
        }

        public async Task SendAsync(string toPhone, string message, CancellationToken cancellationToken = default)
        {
            var integration = await _unitOfWork.Repository<Integration>().Query()
                .FirstOrDefaultAsync(i => i.Key == WhatsAppIntegrationKey, cancellationToken);

            var config = DecodeConfig(integration?.ConfigJson);
            var phoneNumberId = Value(config, "phoneNumberId");
            var accessToken = Value(config, "accessToken");

            if (integration is null || !integration.IsEnabled || string.IsNullOrWhiteSpace(phoneNumberId) || string.IsNullOrWhiteSpace(accessToken))
            {
                throw new DomainValidationException(
                    "WhatsApp is not enabled or fully configured. Set its phoneNumberId and accessToken in Settings → Integrations first.");
            }

            var recipient = DigitsOnly(toPhone);
            if (recipient.Length == 0)
            {
                throw new DomainValidationException("A valid phone number is required to send a WhatsApp message.");
            }

            var payload = JsonSerializer.Serialize(new
            {
                messaging_product = "whatsapp",
                to = recipient,
                type = "text",
                text = new { body = message },
            });

            var client = _httpClientFactory.CreateClient(nameof(WhatsAppSender));
            using var request = new HttpRequestMessage(
                HttpMethod.Post,
                $"https://graph.facebook.com/v21.0/{phoneNumberId}/messages");
            request.Headers.Add("Authorization", $"Bearer {accessToken}");
            request.Content = new StringContent(payload, Encoding.UTF8, "application/json");

            var response = await client.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var detail = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogWarning("WhatsApp send to {To} failed: {Status} {Detail}", recipient, (int)response.StatusCode, detail);
                throw new DomainValidationException($"WhatsApp provider rejected the message ({(int)response.StatusCode}).");
            }

            _logger.LogInformation("WhatsApp message sent to {To}.", recipient);
        }

        private static Dictionary<string, string?> DecodeConfig(string? configJson) =>
            string.IsNullOrWhiteSpace(configJson)
                ? new Dictionary<string, string?>()
                : JsonSerializer.Deserialize<Dictionary<string, string?>>(configJson) ?? new Dictionary<string, string?>();

        private static string? Value(Dictionary<string, string?> config, string key) =>
            config.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value) ? value : null;

        private static string DigitsOnly(string phone) =>
            new string((phone ?? string.Empty).Where(char.IsDigit).ToArray());
    }
}
