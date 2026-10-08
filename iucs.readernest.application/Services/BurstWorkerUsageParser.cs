using System.Text.Json;
using System.Text.RegularExpressions;
using iucs.readernest.application.Dto.Monitoring;

namespace iucs.readernest.application.Services
{
    /// <summary>
    /// Turns the burst worker's raw files (the JSON-lines usage log written by burst-scale-up.sh /
    /// burst-scale-down.sh / pull-worker-recordings.sh, the burst-status.sh JSON, and the free-text
    /// scale log) into the dashboard's cost/usage numbers. Pure functions with no I/O so the
    /// billing arithmetic and pairing rules can be unit-tested.
    /// </summary>
    public static class BurstWorkerUsageParser
    {
        // IST = UTC+5:30, the boundary the rest of the monitoring dashboard already uses for "today".
        private static readonly TimeSpan IstOffset = TimeSpan.FromMinutes(330);

        private readonly record struct UsageEvent(DateTime TimestampUtc, string Event, long? ServerId, string? Trigger, string? By);

        public static BurstWorkerUsageDto Parse(string usageJsonl, DateTime nowUtc, double hourlyRateUsd)
        {
            var events = (usageJsonl ?? string.Empty)
                .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(TryParseEvent)
                .Where(e => e is not null)
                .Select(e => e!.Value)
                .OrderBy(e => e.TimestampUtc)
                .ToList();

            // Hetzner never reuses a server id, so a per-id open/close pairing is enough.
            var open = new Dictionary<long, (DateTime CreatedAt, string Trigger, string? By)>();
            var episodes = new List<BurstWorkerEpisodeDto>();
            foreach (var e in events)
            {
                if (e.Event == "created" && e.ServerId is { } createdId)
                {
                    open[createdId] = (e.TimestampUtc, e.Trigger ?? "auto", e.By);
                }
                else if (e.Event == "deleted" && e.ServerId is { } deletedId && open.TryGetValue(deletedId, out var started))
                {
                    open.Remove(deletedId);
                    episodes.Add(NewEpisode(deletedId, started.CreatedAt, e.TimestampUtc, started.Trigger, started.By, hourlyRateUsd));
                }
            }

            // Anything still open is running right now -- cost so far, not a final figure.
            foreach (var (serverId, started) in open)
            {
                episodes.Add(NewEpisode(serverId, started.CreatedAt, null, started.Trigger, started.By, hourlyRateUsd, nowUtc));
            }

            // Attribute each rescued recording to the episode it happened during (a small grace after
            // deletion, since the last copy is logged just before the server is removed).
            foreach (var rec in events.Where(e => e.Event == "recording"))
            {
                var owner = episodes
                    .Where(ep => ep.CreatedAtUtc <= rec.TimestampUtc
                        && (ep.DeletedAtUtc is null || rec.TimestampUtc <= ep.DeletedAtUtc.Value.AddMinutes(2)))
                    .OrderByDescending(ep => ep.CreatedAtUtc)
                    .FirstOrDefault();
                if (owner is not null)
                {
                    owner.RecordingsHandled++;
                }
            }

            var istNow = nowUtc + IstOffset;
            var dayStartUtc = DateTime.SpecifyKind(istNow.Date - IstOffset, DateTimeKind.Utc);
            var monthStartUtc = DateTime.SpecifyKind(new DateTime(istNow.Year, istNow.Month, 1) - IstOffset, DateTimeKind.Utc);
            var today = episodes.Where(e => e.CreatedAtUtc >= dayStartUtc).ToList();
            var month = episodes.Where(e => e.CreatedAtUtc >= monthStartUtc).ToList();

            return new BurstWorkerUsageDto
            {
                EpisodesToday = today.Count,
                HoursToday = Round2(today.Sum(e => e.DurationHours)),
                EstimatedCostTodayUsd = Round2(today.Sum(e => e.EstimatedCostUsd)),
                MaxBilledCostTodayUsd = Round2(today.Sum(e => e.MaxBilledCostUsd)),
                RecordingsHandledToday = today.Sum(e => e.RecordingsHandled),
                EpisodesThisMonth = month.Count,
                HoursThisMonth = Round2(month.Sum(e => e.DurationHours)),
                EstimatedCostThisMonthUsd = Round2(month.Sum(e => e.EstimatedCostUsd)),
                MaxBilledCostThisMonthUsd = Round2(month.Sum(e => e.MaxBilledCostUsd)),
                RecordingsHandledThisMonth = month.Sum(e => e.RecordingsHandled),
                EpisodesAllTime = episodes.Count,
                HoursAllTime = Round2(episodes.Sum(e => e.DurationHours)),
                EstimatedCostAllTimeUsd = Round2(episodes.Sum(e => e.EstimatedCostUsd)),
                MaxBilledCostAllTimeUsd = Round2(episodes.Sum(e => e.MaxBilledCostUsd)),
                HourlyRateUsd = hourlyRateUsd,
                CurrentlyActive = open.Count > 0,
                RecentEpisodes = episodes.OrderByDescending(e => e.CreatedAtUtc).Take(20).ToList(),
            };
        }

        /// <summary>Parses burst-status.sh's one-line JSON. Anything unreadable becomes Known=false so the UI shows "unknown" rather than a guess.</summary>
        public static BurstWorkerStatusDto ParseStatus(string json, DateTime nowUtc)
        {
            try
            {
                using var doc = JsonDocument.Parse(json.Trim());
                var root = doc.RootElement;
                var known = root.TryGetProperty("ok", out var ok) && ok.ValueKind == JsonValueKind.True;
                if (!known)
                {
                    return new BurstWorkerStatusDto { Known = false };
                }

                var holdEpoch = root.TryGetProperty("holdUntilEpoch", out var h) && h.ValueKind == JsonValueKind.Number ? h.GetInt64() : 0;
                var hold = holdEpoch > 0 ? DateTimeOffset.FromUnixTimeSeconds(holdEpoch).UtcDateTime : (DateTime?)null;

                return new BurstWorkerStatusDto
                {
                    Known = true,
                    Exists = root.TryGetProperty("exists", out var ex) && ex.ValueKind == JsonValueKind.True,
                    ServerId = root.TryGetProperty("serverId", out var id) && id.ValueKind == JsonValueKind.Number ? id.GetInt64() : null,
                    CreatedAtUtc = root.TryGetProperty("createdAtUtc", out var c) && c.ValueKind == JsonValueKind.String ? c.GetDateTime().ToUniversalTime() : null,
                    HoldUntilUtc = hold is { } until && until > nowUtc ? until : null,
                };
            }
            catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException)
            {
                return new BurstWorkerStatusDto { Known = false };
            }
        }

        /// <summary>Keeps only real, timestamped log lines (the scale log also captures rsync's own noise) and returns the last <paramref name="max"/>, oldest first.</summary>
        public static List<string> ParseActivity(string logText, int max)
        {
            return (logText ?? string.Empty)
                .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(l => Regex.IsMatch(l, @"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}Z "))
                .Select(l => l.Length > 240 ? l[..240] + "…" : l)
                .TakeLast(max)
                .ToList();
        }

        private static BurstWorkerEpisodeDto NewEpisode(long serverId, DateTime createdAt, DateTime? deletedAt, string trigger, string? by, double rate, DateTime? nowIfOpen = null)
        {
            var end = deletedAt ?? nowIfOpen ?? createdAt;
            var hours = Math.Max(0, (end - createdAt).TotalHours);
            var billedHours = Math.Max(1, (int)Math.Ceiling(hours));
            return new BurstWorkerEpisodeDto
            {
                ServerId = serverId,
                CreatedAtUtc = createdAt,
                DeletedAtUtc = deletedAt,
                DurationHours = hours,
                EstimatedCostUsd = hours * rate,
                BilledHours = billedHours,
                MaxBilledCostUsd = billedHours * rate,
                Trigger = trigger,
                RequestedBy = by,
            };
        }

        private static double Round2(double v) => Math.Round(v, 2);

        private static UsageEvent? TryParseEvent(string line)
        {
            try
            {
                using var doc = JsonDocument.Parse(line);
                var root = doc.RootElement;
                return new UsageEvent(
                    root.GetProperty("ts").GetDateTime().ToUniversalTime(),
                    root.GetProperty("event").GetString() ?? string.Empty,
                    root.TryGetProperty("serverId", out var id) && id.ValueKind == JsonValueKind.Number ? id.GetInt64() : null,
                    root.TryGetProperty("trigger", out var t) && t.ValueKind == JsonValueKind.String ? t.GetString() : null,
                    root.TryGetProperty("by", out var b) && b.ValueKind == JsonValueKind.String ? b.GetString() : null);
            }
            catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException or FormatException)
            {
                // A malformed line (partial write, manual edit) must not break the whole summary.
                return null;
            }
        }
    }
}
