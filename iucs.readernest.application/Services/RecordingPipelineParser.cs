using System.Globalization;
using System.Text.RegularExpressions;
using iucs.readernest.application.Dto.Monitoring;

namespace iucs.readernest.application.Services
{
    /// <summary>Pure parsing of the recording-pipeline facts collected from main, kept apart from SSH so it is unit-testable.</summary>
    public static class RecordingPipelineParser
    {
        private static readonly TimeSpan IstOffset = TimeSpan.FromMinutes(330);
        private const double Gb = 1024d * 1024 * 1024;

        // e.g. "2026-09-20T16:40:33Z room=trn-abc storageUrl=https://x/y.mp4 duration=375 -> HTTP 204 "
        private static readonly Regex LineRegex = new(
            @"^(?<at>\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}Z)\s+room=(?<room>\S+)\s+storageUrl=(?<url>\S+)\s+duration=(?<dur>\d+)\s+->\s+HTTP\s+(?<code>\d+)",
            RegexOptions.Compiled);

        public static RecordingPipelineDto Parse(
            string finalizeLog, int pendingRegistration, long? newestEpochSeconds,
            string diskLine, long growthBytesLast24h, DateTime nowUtc)
        {
            var dayStartUtc = DateTime.SpecifyKind((nowUtc + IstOffset).Date - IstOffset, DateTimeKind.Utc);

            // A finalize call can be retried, so the same file may appear twice (a failure then a success):
            // the last outcome per file is the truth.
            var latest = new Dictionary<string, (DateTime At, string Room, int Duration, int Code)>();
            foreach (var raw in (finalizeLog ?? "").Split('\n'))
            {
                var m = LineRegex.Match(raw.Trim());
                if (!m.Success) continue;
                var at = DateTime.SpecifyKind(
                    DateTime.ParseExact(m.Groups["at"].Value, "yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture),
                    DateTimeKind.Utc);
                if (at < dayStartUtc || at > nowUtc.AddMinutes(5)) continue;
                latest[m.Groups["url"].Value] = (at, m.Groups["room"].Value, int.Parse(m.Groups["dur"].Value), int.Parse(m.Groups["code"].Value));
            }

            var dto = new RecordingPipelineDto { PendingRegistration = Math.Max(0, pendingRegistration) };
            foreach (var e in latest.Values.OrderBy(v => v.At))
            {
                if (e.Code == 200)
                {
                    dto.RegisteredToday++;
                }
                else if (e.Code == 204)
                {
                    dto.UnattachedToday++;
                    dto.Unattached.Add(new UnattachedRecordingDto { Room = e.Room, AtUtc = e.At, DurationSeconds = e.Duration });
                }
                else
                {
                    dto.FailedToday++;
                }
            }
            dto.Unattached.Reverse();

            if (newestEpochSeconds is > 0)
            {
                dto.NewestRecordingUtc = DateTimeOffset.FromUnixTimeSeconds(newestEpochSeconds.Value).UtcDateTime;
            }

            // "<size bytes> <avail bytes>" from df -B1 --output=size,avail
            var parts = (diskLine ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length >= 2
                && double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var size)
                && double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var avail)
                && size > 0)
            {
                dto.DiskTotalGb = Math.Round(size / Gb, 1);
                dto.DiskFreeGb = Math.Round(avail / Gb, 1);
                dto.DiskFreePercent = Math.Round(avail / size * 100, 1);
                dto.GrowthLast24hGb = Math.Round(Math.Max(0, growthBytesLast24h) / Gb, 1);
                if (growthBytesLast24h > 0)
                {
                    dto.DaysOfDiskLeft = Math.Round(avail / growthBytesLast24h, 1);
                }
            }

            return dto;
        }
    }
}
