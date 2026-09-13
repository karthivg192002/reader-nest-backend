using System.Text;
using System.Text.Json;
using iucs.readernest.application.Common.Interfaces;

namespace iucs.readernest.api.Services
{
    /// <summary>
    /// HTTP client for the recording-render bot's own tiny trigger API (POST /start, POST
    /// /stop -- see the bot's own bot.js, deployed separately on the Jitsi server). Never
    /// throws: a missing/unreachable/misconfigured bot must degrade to "no whiteboard capture
    /// this class," never to "the class failed to start," which is why every failure here is
    /// logged and swallowed rather than surfaced to the caller.
    /// </summary>
    public class RecordingRenderClient : IRecordingRenderClient
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IConfiguration _configuration;
        private readonly ILogger<RecordingRenderClient> _logger;

        public RecordingRenderClient(IHttpClientFactory httpClientFactory, IConfiguration configuration, ILogger<RecordingRenderClient> logger)
        {
            _httpClientFactory = httpClientFactory;
            _configuration = configuration;
            _logger = logger;
        }

        public Task StartAsync(string room, CancellationToken cancellationToken = default) => SendAsync("start", room, cancellationToken);

        public Task StopAsync(string room, CancellationToken cancellationToken = default) => SendAsync("stop", room, cancellationToken);

        private async Task SendAsync(string action, string room, CancellationToken cancellationToken)
        {
            var baseUrl = _configuration["RecordingRender:BotBaseUrl"];
            if (string.IsNullOrWhiteSpace(baseUrl))
            {
                // Unconfigured deployments (local dev, anything without the bot provisioned)
                // simply don't get whiteboard capture -- not an error.
                return;
            }

            try
            {
                var client = _httpClientFactory.CreateClient("RecordingRenderBot");
                var body = new StringContent(JsonSerializer.Serialize(new { room }), Encoding.UTF8, "application/json");
                using var response = await client.PostAsync($"{baseUrl.TrimEnd('/')}/{action}", body, cancellationToken);
                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning("Recording-render bot {Action} for room {Room} returned {StatusCode}", action, room, (int)response.StatusCode);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Recording-render bot {Action} for room {Room} failed", action, room);
            }
        }
    }
}
