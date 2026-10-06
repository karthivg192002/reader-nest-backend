namespace iucs.readernest.application.Services
{
    /// <summary>
    /// Turns rn-job's raw numbers for one cron task into the status the dashboard shows. Kept apart
    /// from the Prometheus plumbing so it is unit-testable; the "late" rule matches the
    /// ScheduledTaskNotRunning alert in alert.rules.yml (twice the interval plus 5 minutes).
    /// </summary>
    public static class ScheduledTaskStatus
    {
        public const string Ok = "ok";
        public const string Failed = "failed";
        public const string Late = "late";

        public static string Classify(DateTime lastRunUtc, int lastExitCode, double expectedIntervalSeconds, DateTime nowUtc)
        {
            var overdueAfter = TimeSpan.FromSeconds(2 * expectedIntervalSeconds + 300);
            if (nowUtc - lastRunUtc > overdueAfter)
            {
                return Late;
            }
            return lastExitCode == 0 ? Ok : Failed;
        }
    }
}
