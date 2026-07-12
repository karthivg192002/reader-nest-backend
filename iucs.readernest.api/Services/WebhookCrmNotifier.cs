using System.Text;
using System.Text.Json;
using iucs.readernest.application.Common.Interfaces;

namespace iucs.readernest.api.Services
{
    /// <summary>
    /// Pushes lead events to the CRM webhook configured at Integrations:CrmWebhookUrl.
    /// Failures are logged, never thrown — CRM outages must not break admissions.
    /// </summary>
    public class WebhookCrmNotifier : ICrmNotifier
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly string? _webhookUrl;
        private readonly ILogger<WebhookCrmNotifier> _logger;

        public WebhookCrmNotifier(
            IHttpClientFactory httpClientFactory,
            IConfiguration configuration,
            ILogger<WebhookCrmNotifier> logger)
        {
            _httpClientFactory = httpClientFactory;
            _webhookUrl = configuration["Integrations:CrmWebhookUrl"];
            _logger = logger;
        }

        public async Task PushLeadEventAsync(string eventType, object payload, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(_webhookUrl))
            {
                return;
            }

            try
            {
                var client = _httpClientFactory.CreateClient(nameof(WebhookCrmNotifier));
                var body = JsonSerializer.Serialize(new { eventType, occurredAtUtc = DateTime.UtcNow, data = payload });
                using var content = new StringContent(body, Encoding.UTF8, "application/json");
                var response = await client.PostAsync(_webhookUrl, content, cancellationToken);
                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning("CRM webhook returned {Status} for event {EventType}.", (int)response.StatusCode, eventType);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "CRM webhook push failed for event {EventType}.", eventType);
            }
        }
    }
}
