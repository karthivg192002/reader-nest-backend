namespace iucs.readernest.application.Dto.Monitoring
{
    /// <summary>One named process/container this server's agent was asked to watch.</summary>
    public class MonitoredServiceDto
    {
        public string Name { get; set; } = string.Empty;
        public bool Active { get; set; }
    }

    /// <summary>
    /// A point-in-time `docker stats` sample for one container, published by the
    /// rn-container-stats.sh textfile-collector script (chosen over cAdvisor, which hit an
    /// unresolved Docker overlay2 layer-ID lookup bug in this environment). Not a Prometheus
    /// counter, so it can't be rate()'d over time -- it's "what is this container using right
    /// now," refreshed every minute.
    /// </summary>
    public class ContainerMetricDto
    {
        public string Name { get; set; } = string.Empty;
        public double CpuPercent { get; set; }
        public double MemoryMb { get; set; }
    }

    /// <summary>Live conference/participant counts, populated only for the Jitsi server.</summary>
    public class LiveCallSummaryDto
    {
        public int ActiveConferences { get; set; }
        public int TotalParticipants { get; set; }
    }

    /// <summary>
    /// Jibri (recording) fleet status, populated only for the Jitsi server. Jibri handles one
    /// recording per instance, so <see cref="BusyInstances"/> == <see cref="TotalInstances"/>
    /// means the next concurrent recording request will fail with "all Jibris were busy" until
    /// the autoscaler (see /opt/rn-monitoring/jibri-autoscale.sh on the Jitsi box) adds capacity.
    /// </summary>
    public class RecorderStatusDto
    {
        public int TotalInstances { get; set; }
        public int BusyInstances { get; set; }
        public int IdleInstances => Math.Max(0, TotalInstances - BusyInstances);
        /// <summary>Configured floor of always-warm instances -- the one admin-adjustable knob (see IServerControlService).</summary>
        public int MinInstances { get; set; }
        /// <summary>Ceiling derived from the Jitsi box's actual CPU/RAM, not a fixed number -- grows on its own if the box is resized.</summary>
        public int MaxInstances { get; set; }
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
        /// <summary>0 when the server has no swap configured -- the UI should hide the gauge rather than show a meaningless 0%.</summary>
        public double SwapUsedPercent { get; set; }
        public double SwapTotalMb { get; set; }
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
        public RecorderStatusDto? RecorderStatus { get; set; }
        /// <summary>Last hour of CPU/memory usage, ~2-minute steps — populated only when Reachable.</summary>
        public List<TimeSeriesPointDto> CpuHistory { get; set; } = new();
        public List<TimeSeriesPointDto> MemoryHistory { get; set; } = new();
        public CallQualityDto? CallQuality { get; set; }
        public CapacityForecastDto? DiskForecast { get; set; }
        /// <summary>Per-container CPU/memory snapshot, sorted by CPU descending — empty if the rn-container-stats.sh script hasn't published on this box yet.</summary>
        public List<ContainerMetricDto> ContainerMetrics { get; set; } = new();
        /// <summary>Week-over-week load trend — null while there isn't yet 14 days of Prometheus history to compare against.</summary>
        public CapacityTrendDto? CapacityTrend { get; set; }
    }

    /// <summary>
    /// Honest load-trend reporting instead of a fake day-countdown: CPU and (for the Jitsi
    /// server) recording load are bursty, real-time signals, not the smoothly-accumulating
    /// kind deriv() forecasts well (unlike disk fill -- see CapacityForecastDto). This reports
    /// direction and how close to the ceiling things got, not a projected "days until full."
    /// </summary>
    public class CapacityTrendDto
    {
        public double CpuAvg7dPercent { get; set; }
        public double CpuPeak7dPercent { get; set; }
        /// <summary>Positive = busier than the prior 7 days, negative = quieter.</summary>
        public double CpuWeekOverWeekChangePercent { get; set; }
        /// <summary>Percent of the last 7 days spent with every Jibri instance busy (i.e. the next recording would have failed) -- null for a server with no Jibri fleet.</summary>
        public double? RecordingAtCapacityPercent7d { get; set; }
    }

    /// <summary>One connected user in a live class, for the admin "who's live right now" view.</summary>
    public class LiveParticipantDto
    {
        public Guid UserId { get; set; } = Guid.Empty;
        public string Name { get; set; } = string.Empty;
        public string Role { get; set; } = string.Empty;
        public DateTime JoinedAtUtc { get; set; }
        /// <summary>How many simultaneous connections this person currently holds (2+ tabs/devices) -- surfaced explicitly rather than silently collapsed, since it can also indicate a connection struggling to stay up.</summary>
        public int ConnectionCount { get; set; } = 1;
        /// <summary>
        /// Whether a SessionAttendance row already reflects this person's presence (Present/Late,
        /// not Absent). False can mean attendance capture is still catching up (it fires
        /// asynchronously on join) or that it genuinely failed -- either way, a live participant
        /// with no attendance record after they've clearly been in a while is worth an admin's
        /// attention. For a parent/student connection this checks whether ANY of that parent's
        /// actively-enrolled children in this batch has a recorded row, mirroring
        /// AcademicOpsService's own "mark every enrolled child present" resolution -- the system
        /// has no record of which specific child is physically on the call, only that the parent
        /// joined on their behalf.
        /// </summary>
        public bool AttendanceRecorded { get; set; }
    }

    /// <summary>One live class session with everyone currently connected to it, enriched with the human-readable context a raw session Guid doesn't carry on its own.</summary>
    public class LiveClassSessionDto
    {
        public string SessionId { get; set; } = string.Empty;
        public string CourseName { get; set; } = string.Empty;
        /// <summary>Null for a demo session, which has no Batch.</summary>
        public string? BatchName { get; set; }
        public string TeacherName { get; set; } = string.Empty;
        public DateTime? StartedAtUtc { get; set; }
        /// <summary>Null for a demo session (no fixed schedule). Running past this is worth flagging, not just informational.</summary>
        public DateTime? ScheduledEndAtUtc { get; set; }
        public List<LiveParticipantDto> Participants { get; set; } = new();
    }

    /// <summary>One of today's class sessions, live or not -- for the admin "today's sessions" timeline. Deliberately NOT limited to currently-live ones, unlike LiveClassSessionDto.</summary>
    public class SessionHistoryEntryDto
    {
        public Guid SessionId { get; set; }
        public string CourseName { get; set; } = string.Empty;
        public string? BatchName { get; set; }
        public string TeacherName { get; set; } = string.Empty;
        public DateTime ScheduledStartAtUtc { get; set; }
        public DateTime ScheduledEndAtUtc { get; set; }
        public DateTime? ActualStartAtUtc { get; set; }
        public DateTime? ActualEndAtUtc { get; set; }
        public string Status { get; set; } = string.Empty;
        /// <summary>Distinct people (teacher + students) with a Present/Late SessionAttendance row -- not a live headcount, a durable record.</summary>
        public int AttendedCount { get; set; }
        /// <summary>Teacher (1) + actively-enrolled batch students at the time of this query -- the roster this session was expected to draw from.</summary>
        public int ExpectedCount { get; set; }
    }

    /// <summary>Historical CPU/memory usage for one server over an admin-selected window (see GetHistoryAsync).</summary>
    public class HistoryRangeDto
    {
        public List<TimeSeriesPointDto> CpuHistory { get; set; } = new();
        public List<TimeSeriesPointDto> MemoryHistory { get; set; } = new();
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

    /// <summary>Request body for POST .../jibri/min-replicas.</summary>
    public class SetJibriMinReplicasRequest
    {
        public int MinReplicas { get; set; }
    }

    /// <summary>Result of an on-demand Jibri fleet action (rescale-now, or a min-replicas change) -- see IServerControlService.</summary>
    public class JibriControlResultDto
    {
        public string Server { get; set; } = string.Empty;
        public string Action { get; set; } = string.Empty;
        /// <summary>Tail of the autoscaler's own log right after the action ran, so the admin sees what it actually did.</summary>
        public List<string> LogTail { get; set; } = new();
        public DateTime PerformedAtUtc { get; set; }
    }

    /// <summary>Result of restarting one container on one monitored server (see IServerControlService).</summary>
    public class ServiceRestartResultDto
    {
        public string Server { get; set; } = string.Empty;
        public string Container { get; set; } = string.Empty;
        public DateTime PerformedAtUtc { get; set; }
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
