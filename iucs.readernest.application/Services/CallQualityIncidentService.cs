using iucs.readernest.application.Common.Options;
using iucs.readernest.application.Dto.Monitoring;
using Microsoft.Extensions.Options;

namespace iucs.readernest.application.Services
{
    public class CallQualityIncidentService : ICallQualityIncidentService
    {
        // Fixed container name on the Jitsi/Video server; never user input.
        private const string JvbContainer = "docker-jitsi-meet-jvb-1";
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
                    $"docker logs {JvbContainer} --since 24h 2>&1 | grep 'SendSideBandwidthEstimation.maybeLogLowBitrateWarning'",
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
