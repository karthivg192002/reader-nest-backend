namespace iucs.readernest.application.Dto.Monitoring
{
    /// <summary>One named process/container this server's agent was asked to watch.</summary>
    public class MonitoredServiceDto
    {
        public string Name { get; set; } = string.Empty;
        public bool Active { get; set; }
    }

    /// <summary>Live conference/participant counts, populated only for the Jitsi server.</summary>
    public class LiveCallSummaryDto
    {
        public int ActiveConferences { get; set; }
        public int TotalParticipants { get; set; }
    }

    /// <summary>One sample of a Prometheus range query (a trend chart data point).</summary>
    public class TimeSeriesPointDto
    {
        public DateTime Timestamp { get; set; }
        public double Value { get; set; }
    }

    /// <summary>
    /// Real-time call quality straight from JVB's own native Prometheus endpoint — not just
    /// "is the bridge up," but whether the calls running on it are actually good right now.
    /// Populated only for a server with TracksLiveCalls set.
    /// </summary>
    public class CallQualityDto
    {
        public double AverageRttMs { get; set; }
        public double IncomingLossPercent { get; set; }
        public double OutgoingLossPercent { get; set; }
        public double IncomingBitrateKbps { get; set; }
        public double OutgoingBitrateKbps { get; set; }
        public int EndpointsSendingAudio { get; set; }
        public int EndpointsSendingVideo { get; set; }
        /// <summary>JVB's own load indicator, 0-1 — how close the bridge is to needing to shed load.</summary>
        public double JvbStressPercent { get; set; }
        public bool JvbHealthy { get; set; }
    }

    /// <summary>
    /// One server's point-in-time health, as reported by its own rn-status agent. <see cref="Reachable"/>
    /// false means the agent couldn't be reached at all (server down, network issue, wrong token) —
    /// every other field is then meaningless/default and the UI should show it as unknown, not "0%".
    /// </summary>
    public class ServerStatusDto
    {
        public string Name { get; set; } = string.Empty;
        public string Hostname { get; set; } = string.Empty;
        public bool Reachable { get; set; }
        public string? Error { get; set; }
        public long UptimeSeconds { get; set; }
        public double LoadAverage1m { get; set; }
        public int CpuCores { get; set; }
        public double CpuUsagePercent { get; set; }
        public double MemoryUsedPercent { get; set; }
        public double MemoryTotalMb { get; set; }
        public double DiskUsedPercent { get; set; }
        public double DiskTotalGb { get; set; }
        public double NetworkRxMbps { get; set; }
        public double NetworkTxMbps { get; set; }
        public double DiskReadMbps { get; set; }
        public double DiskWriteMbps { get; set; }
        public List<MonitoredServiceDto> Services { get; set; } = new();
        /// <summary>How long ago the agent itself last wrote its status file — a stale reading (agent stuck/cron dead) still reports <see cref="Reachable"/> true, so the UI needs this to flag it separately.</summary>
        public double AgentDataAgeSeconds { get; set; }
        public LiveCallSummaryDto? LiveCalls { get; set; }
        /// <summary>Last hour of CPU/memory usage, ~2-minute steps — populated only when Reachable.</summary>
        public List<TimeSeriesPointDto> CpuHistory { get; set; } = new();
        public List<TimeSeriesPointDto> MemoryHistory { get; set; } = new();
        public CallQualityDto? CallQuality { get; set; }
        public CapacityForecastDto? DiskForecast { get; set; }
    }

    /// <summary>
    /// Postgres internals pulled straight from postgres-exporter (pg_stat_database/pg_locks/
    /// pg_settings), scoped to the app's own database — not the whole Postgres instance,
    /// which also carries system/UAT databases with their own, unrelated activity.
    /// </summary>
    public class DatabaseInsightsDto
    {
        public int ActiveConnections { get; set; }
        public int MaxConnections { get; set; }
        public double CommitsPerSecond { get; set; }
        public double RollbacksPerSecond { get; set; }
        public double CacheHitRatioPercent { get; set; }
        public double DatabaseSizeMb { get; set; }
        public long DeadlocksTotal { get; set; }
        public int LocksHeld { get; set; }
    }

    /// <summary>
    /// Disk-fill projection from the last 6 hours' trend (Prometheus's own deriv() function —
    /// a real linear-regression rate, not a guess). Only meaningful for genuinely accumulating
    /// usage (logs, recordings, DB growth); a healthy server usually reports IsFilling=false.
    /// </summary>
    public class CapacityForecastDto
    {
        public bool IsFilling { get; set; }
        /// <summary>Null unless IsFilling is true.</summary>
        public double? DaysUntilFull { get; set; }
        /// <summary>Signed: positive means free space is growing, negative means it's shrinking.</summary>
        public double TrendGbPerDay { get; set; }
    }

    /// <summary>One currently pending/firing Prometheus alert (see IPrometheusClient.GetActiveAlertsAsync).</summary>
    public class AlertDto
    {
        public string Name { get; set; } = string.Empty;
        public string Severity { get; set; } = string.Empty;
        public string Summary { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        /// <summary>"pending" (condition met, waiting out the rule's `for` duration) or "firing".</summary>
        public string State { get; set; } = string.Empty;
        public DateTime ActiveSince { get; set; }
        public string? Instance { get; set; }
    }

    /// <summary>Error-filtered `docker logs` tail for one container on one monitored server (see IServerLogService).</summary>
    public class ServerLogsDto
    {
        public string Server { get; set; } = string.Empty;
        public string Container { get; set; } = string.Empty;
        public List<string> Lines { get; set; } = new();
        public DateTime FetchedAtUtc { get; set; }
    }

    /// <summary>Everything the Server Monitoring dashboard needs in one call.</summary>
    public class MonitoringSummaryDto
    {
        public List<ServerStatusDto> Servers { get; set; } = new();
        public bool ApiHealthy { get; set; }
        public bool DatabaseHealthy { get; set; }
        public double DatabaseLatencyMs { get; set; }
        public DatabaseInsightsDto? DatabaseInsights { get; set; }
        /// <summary>Total connections currently joined to any live class, platform-wide (from ClassroomHub) — distinct from a single Jitsi server's own participant count.</summary>
        public int ConcurrentClassroomUsers { get; set; }
        public int ActiveClassCount { get; set; }
        public List<AlertDto> ActiveAlerts { get; set; } = new();
        public DateTime GeneratedAtUtc { get; set; }
    }
}
