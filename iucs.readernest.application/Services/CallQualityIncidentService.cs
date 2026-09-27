using iucs.readernest.application.Common.Options;
using iucs.readernest.application.Dto.Monitoring;
using Microsoft.Extensions.Options;

namespace iucs.readernest.application.Services
{
    public class CallQualityIncidentService : ICallQualityIncidentService
    {
        // Fixed container names on the Jitsi/Video server (two video bridges share the load); never user input.
        private static readonly string[] JvbContainers = { "docker-jitsi-meet-jvb-1", "docker-jitsi-meet-jvb2-1" };
        private readonly MonitoringOptions _options;

        public CallQualityIncidentService(IOptions<MonitoringOptions> options)
        {
            _options = options.Value;
        }

        public async Task<List<CallQualityIncidentDto>> GetRecentAsync(CancellationToken cancellationToken = default)
        {
            var main = BurstWorkerSsh.FindMain(_options);
            if (main is null)
            {
                return new List<CallQualityIncidentDto>();
            }

            string raw;
            try
            {
                raw = await BurstWorkerSsh.RunAsync(
                    main,
                    $"for c in {string.Join(' ', JvbContainers)}; do docker logs $c --since 24h 2>&1; done | grep 'SendSideBandwidthEstimation.maybeLogLowBitrateWarning'",
                    TimeSpan.FromSeconds(20),
                    cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Runs inside the dashboard's single /summary call: never let it fail the whole page.
                return new List<CallQualityIncidentDto>();
            }

            return CallQualityIncidentParser.Parse(raw, DateTime.UtcNow);
        }
    }
}
