using iucs.readernest.application.Common.Options;
using iucs.readernest.application.Dto.Monitoring;
using Microsoft.Extensions.Options;

namespace iucs.readernest.application.Services
{
    public class BurstWorkerUsageService : IBurstWorkerUsageService
    {
        private const string StatusMarker = "@@STATUS@@";
        private const string ActivityMarker = "@@ACTIVITY@@";

        private readonly MonitoringOptions _options;

        public BurstWorkerUsageService(IOptions<MonitoringOptions> options)
        {
            _options = options.Value;
        }

        public async Task<BurstWorkerUsageDto?> GetUsageSummaryAsync(CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(_options.BurstWorkerUsageLogPath))
            {
                return null;
            }

            var main = BurstWorkerSsh.FindMain(_options);
            if (main is null)
            {
                return null;
            }

            string raw;
            try
            {
                // One SSH round trip for everything: the usage log, the live server status, and the
                // recent scale log. BurstWorkerUsageLogPath is fixed operator config, never user input.
                raw = await BurstWorkerSsh.RunAsync(
                    main,
                    $"cat {_options.BurstWorkerUsageLogPath} 2>/dev/null; echo; echo {StatusMarker}; " +
                    $"bash {BurstWorkerSsh.ScriptsDir}/burst-status.sh 2>/dev/null; echo {ActivityMarker}; " +
                    $"tail -n 120 {BurstWorkerSsh.ScaleLog} 2>/dev/null",
                    TimeSpan.FromSeconds(20),
                    cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // This runs inside the dashboard's single /summary call: an unreachable main (or an SSH
                // hiccup) must degrade to "usage unavailable", never fail the whole monitoring page.
                return null;
            }

            var statusAt = raw.IndexOf(StatusMarker, StringComparison.Ordinal);
            var activityAt = raw.IndexOf(ActivityMarker, StringComparison.Ordinal);
            if (statusAt < 0 || activityAt < statusAt)
            {
                return null;
            }

            var usageText = raw[..statusAt];
            var statusText = raw[(statusAt + StatusMarker.Length)..activityAt];
            var activityText = raw[(activityAt + ActivityMarker.Length)..];

            var now = DateTime.UtcNow;
            var usage = BurstWorkerUsageParser.Parse(usageText, now, _options.BurstWorkerHourlyRateUsd);
            usage.Status = BurstWorkerUsageParser.ParseStatus(statusText, now);
            usage.RecentActivity = BurstWorkerUsageParser.ParseActivity(activityText, 25);

            // The live cloud status is the source of truth for "is it running"; the log-derived flag is only the fallback.
            if (usage.Status.Known)
            {
                usage.CurrentlyActive = usage.Status.Exists;
            }

            return usage;
        }
    }
}
