using iucs.readernest.application.Dto.Monitoring;

namespace iucs.readernest.application.Services
{
    /// <summary>
    /// App-level health checks shown alongside the Prometheus alerts on the Monitoring page.
    /// Both come from the 7 Oct 2026 incident, where the infrastructure looked healthy while the
    /// data quietly went wrong: classes sat InProgress for weeks because auto-completion threw on
    /// every run (no teacher payout accrued), and a second backend on a stale database marked 107
    /// real classes as teacher no-shows in an evening — neither was visible anywhere.
    /// </summary>
    public static class DataHealthAlerts
    {
        /// <summary>A class still InProgress this long after its scheduled end was not completed by the teacher or by AbandonedClassCompletionBackgroundService.</summary>
        public static readonly TimeSpan StuckAfter = TimeSpan.FromHours(1);

        /// <summary>Window and threshold for "too many no-shows": a normal day has a handful; 10 in 3 hours means something is marking them wrongly.</summary>
        public static readonly TimeSpan NoShowWindow = TimeSpan.FromHours(3);
        public const int NoShowThreshold = 10;

        public static List<AlertDto> Build(int stuckCount, DateTime? oldestStuckEndUtc, int recentNoShows, DateTime nowUtc)
        {
            var alerts = new List<AlertDto>();
            if (stuckCount > 0)
            {
                alerts.Add(new AlertDto
                {
                    Name = "ClassesStuckInProgress",
                    Severity = "warning",
                    State = "firing",
                    ActiveSince = (oldestStuckEndUtc ?? nowUtc).Add(StuckAfter),
                    Summary = stuckCount == 1
                        ? "1 class ended over an hour ago but is still \"In Progress\""
                        : $"{stuckCount} classes ended over an hour ago but are still \"In Progress\"",
                    Description = "These classes were never completed, so no teacher pay has been added for them. "
                        + "Complete or cancel each one from Sessions; if new ones keep appearing, the automatic class completion is failing.",
                });
            }
            if (recentNoShows >= NoShowThreshold)
            {
                alerts.Add(new AlertDto
                {
                    Name = "UnusualNoShowCount",
                    Severity = "critical",
                    State = "firing",
                    ActiveSince = nowUtc.Subtract(NoShowWindow),
                    Summary = $"{recentNoShows} classes were marked no-show in the last {NoShowWindow.TotalHours:0} hours",
                    Description = "Far more than normal. Check that only one backend is running against the live database "
                        + "and that classes really were missed before the make-up classes and pay deductions stand.",
                });
            }
            return alerts;
        }
    }
}
