using System.Text.RegularExpressions;
using iucs.readernest.application.Common.Options;
using iucs.readernest.application.Dto.Monitoring;
using Microsoft.Extensions.Options;

namespace iucs.readernest.application.Services
{
    public class BurstWorkerControlService : IBurstWorkerControlService
    {
        private const int MinHoldMinutes = 15;
        private const int MaxHoldMinutes = 480;

        private readonly MonitoringOptions _options;

        public BurstWorkerControlService(IOptions<MonitoringOptions> options)
        {
            _options = options.Value;
        }

        public async Task<BurstWorkerControlResultDto> StartAsync(int holdMinutes, string requestedBy, CancellationToken cancellationToken = default)
        {
            var main = RequireMain();
            var hold = Math.Clamp(holdMinutes, MinHoldMinutes, MaxHoldMinutes);
            var by = SanitizeName(requestedBy);

            // `hold` is an int and `by` was reduced to [A-Za-z0-9 ._@-] above, so neither can break
            // out of the single-quoted / numeric positions they're spliced into.
            var output = await BurstWorkerSsh.RunAsync(
                main,
                $"BURST_TRIGGER=manual BURST_BY='{by}' BURST_HOLD_MINUTES={hold} bash {BurstWorkerSsh.ScriptsDir}/burst-scale-up.sh; " +
                $"tail -n 6 {BurstWorkerSsh.ScaleLog}",
                TimeSpan.FromSeconds(60),
                cancellationToken);

            return new BurstWorkerControlResultDto
            {
                Action = $"start (hold {hold} min)",
                LogTail = BurstWorkerUsageParser.ParseActivity(output, 6),
                PerformedAtUtc = DateTime.UtcNow,
            };
        }

        public async Task<BurstWorkerControlResultDto> StopAsync(CancellationToken cancellationToken = default)
        {
            var main = RequireMain();

            // --manual skips the automatic path's 45-minute minimum lifetime and any start hold -- without
            // it Stop silently "skipped" for the first 45 minutes (8 Oct 2026). The teardown itself still
            // refuses unless every recorder is idle and every recording is fully on main.
            var output = await BurstWorkerSsh.RunAsync(
                main,
                $"rm -f {BurstWorkerSsh.ScriptsDir}/burst-hold-until; bash {BurstWorkerSsh.ScriptsDir}/burst-scale-down.sh --manual; " +
                $"tail -n 6 {BurstWorkerSsh.ScaleLog}",
                TimeSpan.FromMinutes(4),
                cancellationToken);

            return new BurstWorkerControlResultDto
            {
                Action = "stop when idle",
                LogTail = BurstWorkerUsageParser.ParseActivity(output, 6),
                PerformedAtUtc = DateTime.UtcNow,
            };
        }

        /// <summary>Public so the shell-injection guard can be unit-tested directly.</summary>
        public static string SanitizeName(string? name)
        {
            var cleaned = Regex.Replace(name ?? string.Empty, @"[^A-Za-z0-9 ._@-]", string.Empty).Trim();
            if (cleaned.Length > 60)
            {
                cleaned = cleaned[..60];
            }

            return cleaned.Length == 0 ? "admin" : cleaned;
        }

        private MonitoredServerOptions RequireMain() =>
            BurstWorkerSsh.FindMain(_options)
            ?? throw new InvalidOperationException("SSH is not configured for the Jitsi / Video server, so the burst worker can't be controlled from here.");
    }
}
