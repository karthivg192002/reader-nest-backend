using System.Text.Json;
using iucs.readernest.application.Common.Options;
using iucs.readernest.application.Dto.Monitoring;
using Microsoft.Extensions.Options;
using Renci.SshNet;

namespace iucs.readernest.application.Services
{
    public class BurstWorkerUsageService : IBurstWorkerUsageService
    {
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

            // The usage log lives on the Jitsi/Video server (main) -- reuse its already-configured
            // SSH credentials rather than adding a dedicated secret just for this one file read.
            var host = _options.Servers.FirstOrDefault(s => s.Name == "Jitsi / Video");
            if (host is null || string.IsNullOrWhiteSpace(host.SshHost) || string.IsNullOrWhiteSpace(host.SshPassword))
            {
                return null;
            }

            string raw;
            using (var client = new SshClient(host.SshHost, host.SshPort, host.SshUsername, host.SshPassword))
            {
                await Task.Run(client.Connect, cancellationToken);
                try
                {
                    // BurstWorkerUsageLogPath is fixed operator config, never user input.
                    var command = client.CreateCommand($"cat {_options.BurstWorkerUsageLogPath} 2>/dev/null");
                    command.CommandTimeout = TimeSpan.FromSeconds(10);
                    raw = await Task.Run(command.Execute, cancellationToken);
                }
                finally
                {
                    if (client.IsConnected)
                    {
                        client.Disconnect();
                    }
                }
            }

            var events = raw
                .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(TryParseEvent)
                .Where(e => e is not null)
                .Select(e => e!.Value)
                .OrderBy(e => e.TimestampUtc)
                .ToList();

            // Pair each "created" with the next "deleted" for the same serverId -- Hetzner never
            // reuses a server id, so a simple per-id open/close (not a stack) is enough.
            var open = new Dictionary<long, DateTime>();
            var episodes = new List<BurstWorkerEpisodeDto>();
            foreach (var e in events)
            {
                if (e.Event == "created")
                {
                    open[e.ServerId] = e.TimestampUtc;
                }
                else if (e.Event == "deleted" && open.TryGetValue(e.ServerId, out var createdAt))
                {
                    open.Remove(e.ServerId);
                    var hours = (e.TimestampUtc - createdAt).TotalHours;
                    episodes.Add(new BurstWorkerEpisodeDto
                    {
                        ServerId = e.ServerId,
                        CreatedAtUtc = createdAt,
                        DeletedAtUtc = e.TimestampUtc,
                        DurationHours = hours,
                        EstimatedCostUsd = hours * _options.BurstWorkerHourlyRateUsd,
                    });
                }
            }

            // Anything still open is a currently-running episode -- cost so far, not a final figure.
            var now = DateTime.UtcNow;
            foreach (var (serverId, createdAt) in open)
            {
                var hours = (now - createdAt).TotalHours;
                episodes.Add(new BurstWorkerEpisodeDto
                {
                    ServerId = serverId,
                    CreatedAtUtc = createdAt,
                    DeletedAtUtc = null,
                    DurationHours = hours,
                    EstimatedCostUsd = hours * _options.BurstWorkerHourlyRateUsd,
                });
            }

            // "Today" in IST, same boundary the rest of the monitoring dashboard uses (see
            // GetTodaySessionsAsync) -- an evening peak shouldn't get split across two "days"
            // just because UTC's midnight fell in the middle of it.
            var istNow = now.AddHours(5).AddMinutes(30);
            var dayStartUtc = istNow.Date.AddHours(-5).AddMinutes(-30);

            var todayEpisodes = episodes.Where(e => e.CreatedAtUtc >= dayStartUtc).ToList();

            return new BurstWorkerUsageDto
            {
                EpisodesToday = todayEpisodes.Count,
                HoursToday = Math.Round(todayEpisodes.Sum(e => e.DurationHours), 2),
                EstimatedCostTodayUsd = Math.Round(todayEpisodes.Sum(e => e.EstimatedCostUsd), 2),
                EpisodesAllTime = episodes.Count,
                HoursAllTime = Math.Round(episodes.Sum(e => e.DurationHours), 2),
                EstimatedCostAllTimeUsd = Math.Round(episodes.Sum(e => e.EstimatedCostUsd), 2),
                CurrentlyActive = open.Count > 0,
                RecentEpisodes = episodes.OrderByDescending(e => e.CreatedAtUtc).Take(20).ToList(),
            };
        }

        private readonly record struct UsageEvent(DateTime TimestampUtc, string Event, long ServerId);

        private static UsageEvent? TryParseEvent(string line)
        {
            try
            {
                using var doc = JsonDocument.Parse(line);
                var root = doc.RootElement;
                return new UsageEvent(
                    root.GetProperty("ts").GetDateTime(),
                    root.GetProperty("event").GetString() ?? string.Empty,
                    root.GetProperty("serverId").GetInt64());
            }
            catch (JsonException)
            {
                // A malformed line (partial write, manual edit) shouldn't break the whole summary.
                return null;
            }
        }
    }
}
