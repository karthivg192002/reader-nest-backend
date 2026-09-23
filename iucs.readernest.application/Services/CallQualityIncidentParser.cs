using System.Globalization;
using System.Text.RegularExpressions;
using iucs.readernest.application.Dto.Monitoring;

namespace iucs.readernest.application.Services
{
    /// <summary>
    /// Pure parsing of JVB's own bandwidth-estimation warnings into per-participant incidents, kept
    /// apart from SSH so it is unit-testable. A "low bitrate" warning is JVB reporting its own
    /// achieved send rate to one participant collapsing below its configured floor -- during a
    /// concurrent-class burst this is a server-side scheduling stall misread as network loss (the
    /// root cause the JVB CPU-isolation fix targets); in isolation (one call, no concurrent load) it
    /// is more likely that participant's own network. This groups raw log lines into one row per
    /// participant-per-call so an admin can tell the two patterns apart at a glance (many different
    /// people at once = systemic; one person for a stretch, alone = individual).
    /// </summary>
    public static class CallQualityIncidentParser
    {
        // JVB's own container clock is IST (Asia/Kolkata), so its log lines are already local time.
        private static readonly TimeSpan IstOffset = TimeSpan.FromMinutes(330);

        // "JVB 2026-09-22 23:03:56.062 WARNING: [35] [confId=... conf_name=trn-xxx@muc.meet.jitsi
        //  meeting_id=... epId=abcdef12 stats_id=Lelah-JW7] SendSideBandwidthEstimation.maybeLogLowBitrateWarning-...:
        //  Estimated available bandwidth 5 kbps is below configured min bitrate 30 kbps."
        private static readonly Regex LineRegex = new(
            @"^JVB\s+(?<at>\d{4}-\d{2}-\d{2}\s\d{2}:\d{2}:\d{2})\.\d+\s+WARNING.*?" +
            @"conf_name=(?<room>[^@\s]+)@[^\s\]]+.*?epId=(?<ep>\S+)\s+stats_id=(?<label>[^\]\s]+)\].*?" +
            @"SendSideBandwidthEstimation\.maybeLogLowBitrateWarning.*?" +
            @"Estimated available bandwidth (?<kbps>[0-9.]+)\s*kbps",
            RegexOptions.Compiled | RegexOptions.Singleline);

        public static List<CallQualityIncidentDto> Parse(string rawLog, DateTime nowUtc, int maxResults = 20)
        {
            // (room, epId) -> running aggregate. epId (not the display label alone) is the real
            // per-participant key -- Jitsi assigns a fresh epId on every (re)join, so two separate
            // stretches for the same person surface as two rows rather than silently merging.
            var byParticipant = new Dictionary<(string Room, string EpId), CallQualityIncidentDto>();

            foreach (var raw in (rawLog ?? "").Split('\n'))
            {
                var m = LineRegex.Match(raw.Trim());
                if (!m.Success) continue;

                var localAt = DateTime.ParseExact(m.Groups["at"].Value, "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
                var atUtc = DateTime.SpecifyKind(localAt - IstOffset, DateTimeKind.Utc);
                if (atUtc > nowUtc.AddMinutes(5)) continue; // clock-skew guard, not a real future event

                var kbps = double.Parse(m.Groups["kbps"].Value, CultureInfo.InvariantCulture);
                var key = (m.Groups["room"].Value, m.Groups["ep"].Value);

                if (!byParticipant.TryGetValue(key, out var incident))
                {
                    incident = new CallQualityIncidentDto
                    {
                        RoomName = m.Groups["room"].Value,
                        ParticipantLabel = m.Groups["label"].Value,
                        FirstAtUtc = atUtc,
                        LastAtUtc = atUtc,
                        EventCount = 0,
                        LowestKbps = kbps,
                    };
                    byParticipant[key] = incident;
                }

                incident.EventCount++;
                incident.LowestKbps = Math.Min(incident.LowestKbps, kbps);
                if (atUtc < incident.FirstAtUtc) incident.FirstAtUtc = atUtc;
                if (atUtc > incident.LastAtUtc) incident.LastAtUtc = atUtc;
            }

            return byParticipant.Values
                .OrderByDescending(i => i.LastAtUtc)
                .Take(maxResults)
                .ToList();
        }

        /// <summary>
        /// The systemic-vs-individual read: many distinct participants colliding inside the same
        /// short window points at shared bridge contention (the CPU-isolation fix's target); a
        /// handful of isolated incidents, especially with no time overlap, reads as ordinary
        /// individual network variance that no server change eliminates.
        /// </summary>
        public static bool LooksSystemic(IReadOnlyList<CallQualityIncidentDto> incidents, int distinctRoomThreshold = 3)
        {
            return incidents.Select(i => i.RoomName).Distinct().Count() >= distinctRoomThreshold;
        }
    }
}
