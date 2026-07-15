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
    /// Sends SMS through the provider configured in Settings → Integrations (the "sms"
    /// record). Supports MSG91 (provider "msg91": authKey + senderId) and Twilio
    /// (provider "twilio": accountSid + authToken + fromNumber). Throws a friendly
    /// error when the integration is disabled/unconfigured or the provider rejects
    /// the send, so admin actions surface the reason.
    /// </summary>
    public class SmsSender : ISmsSender
    {
        private const string SmsIntegrationKey = "sms";

        private readonly IUnitOfWork _unitOfWork;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILogger<SmsSender> _logger;

        public SmsSender(IUnitOfWork unitOfWork, IHttpClientFactory httpClientFactory, ILogger<SmsSender> logger)
        {
            _unitOfWork = unitOfWork;
            _httpClientFactory = httpClientFactory;
            _logger = logger;
        }

        public async Task SendAsync(string toPhone, string message, CancellationToken cancellationToken = default)
        {
            var integration = await _unitOfWork.Repository<Integration>().Query()
                .FirstOrDefaultAsync(i => i.Key == SmsIntegrationKey, cancellationToken);
            var config = DecodeConfig(integration?.ConfigJson);

            if (integration is null || !integration.IsEnabled)
            {
                throw new DomainValidationException(
                    "SMS is not enabled. Configure and enable the SMS integration in Settings → Integrations first.");
            }

            var recipient = DigitsOnly(toPhone);
            if (recipient.Length == 0)
            {
                throw new DomainValidationException("A valid phone number is required to send an SMS.");
            }

            var provider = (Value(config, "provider") ?? "msg91").ToLowerInvariant();
            switch (provider)
            {
                case "twilio":
                    await SendViaTwilioAsync(config, recipient, message, cancellationToken);
                    break;

                case "msg91":
                    await SendViaMsg91Async(config, recipient, message, cancellationToken);
                    break;

                default:
                    throw new DomainValidationException(
                        $"Unknown SMS provider '{provider}'. Set the integration's provider to 'msg91' or 'twilio'.");
            }

            _logger.LogInformation("SMS sent to {To} via {Provider}.", recipient, provider);
        }

        private async Task SendViaMsg91Async(
            Dictionary<string, string?> config,
            string recipient,
            string message,
            CancellationToken cancellationToken)
        {
            var authKey = Value(config, "authKey")
                ?? throw new DomainValidationException("MSG91 needs its authKey set in Settings → Integrations.");
            var senderId = Value(config, "senderId") ?? "RDRNST";

            var payload = JsonSerializer.Serialize(new
            {
                sender = senderId,
                route = "4", // transactional
                country = "91",
                sms = new[] { new { message, to = new[] { recipient } } },
            });

            var client = _httpClientFactory.CreateClient(nameof(SmsSender));
            using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.msg91.com/api/v2/sendsms");
            request.Headers.Add("authkey", authKey);
            request.Content = new StringContent(payload, Encoding.UTF8, "application/json");

            var response = await client.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var detail = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogWarning("MSG91 send to {To} failed: {Status} {Detail}", recipient, (int)response.StatusCode, detail);
                throw new DomainValidationException($"The SMS provider rejected the message ({(int)response.StatusCode}).");
            }
        }

        private async Task SendViaTwilioAsync(
            Dictionary<string, string?> config,
            string recipient,
            string message,
            CancellationToken cancellationToken)
        {
            var accountSid = Value(config, "accountSid")
                ?? throw new DomainValidationException("Twilio needs its accountSid set in Settings → Integrations.");
            var authToken = Value(config, "authToken")
                ?? throw new DomainValidationException("Twilio needs its authToken set in Settings → Integrations.");
            var fromNumber = Value(config, "fromNumber")
                ?? throw new DomainValidationException("Twilio needs its fromNumber set in Settings → Integrations.");

            var client = _httpClientFactory.CreateClient(nameof(SmsSender));
            using var request = new HttpRequestMessage(
                HttpMethod.Post,
                $"https://api.twilio.com/2010-04-01/Accounts/{accountSid}/Messages.json");
            request.Headers.Add(
                "Authorization",
                "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes($"{accountSid}:{authToken}")));
            request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["To"] = recipient.StartsWith('+') ? recipient : $"+{recipient}",
                ["From"] = fromNumber,
                ["Body"] = message,
            });

            var response = await client.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var detail = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogWarning("Twilio send to {To} failed: {Status} {Detail}", recipient, (int)response.StatusCode, detail);
                throw new DomainValidationException($"The SMS provider rejected the message ({(int)response.StatusCode}).");
            }
        }

        private static Dictionary<string, string?> DecodeConfig(string? configJson) =>
            string.IsNullOrWhiteSpace(configJson)
                ? new Dictionary<string, string?>()
                : JsonSerializer.Deserialize<Dictionary<string, string?>>(configJson) ?? new Dictionary<string, string?>();

        private static string? Value(Dictionary<string, string?> config, string key) =>
            config.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value) ? value : null;

        private static string DigitsOnly(string phone) =>
            new((phone ?? string.Empty).Where(c => char.IsDigit(c) || c == '+').ToArray());
    }
}
