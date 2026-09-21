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
        /// <summary>Last ~1h, ~2-minute steps -- the same window as CpuHistory/MemoryHistory.
        /// Empty when this server has no Jibri fleet. Pair with TotalHistory (rather than the
        /// single current TotalInstances) since the fleet autoscales -- "3 busy" only reads as
        /// "at capacity" against however many were actually online at that same moment.</summary>
        public IReadOnlyList<TimeSeriesPointDto> BusyHistory { get; set; } = [];
        public IReadOnlyList<TimeSeriesPointDto> TotalHistory { get; set; } = [];
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
    /// Health of the Cloudflare Calls TURN fallback (see docs/JITSI_ARCHITECTURE.md) that lets
    /// clients on UDP-blocking networks still reach the JVB. Populated only for the Jitsi server,
    /// and only once /opt/rn-monitoring/turn-credentials-refresh.sh has published its textfile
    /// metric at least once.
    /// </summary>
    public class TurnStatusDto
    {
        /// <summary>False means the last refresh is older than its own requested TTL — the credentials Prosody is
        /// currently advertising may have expired, most likely because the refresh cron job stopped running.</summary>
        public bool CredentialsHealthy { get; set; }
        public DateTime? LastRefreshedAtUtc { get; set; }
        public double SecondsSinceRefresh { get; set; }
        /// <summary>TTL the refresh script requested from Cloudflare for the current credentials (seconds).</summary>
        public double CredentialsTtlSeconds { get; set; }
        /// <summary>Total ICE negotiations JVB has completed successfully since it last started.</summary>
        public long IceSucceededTotal { get; set; }
        /// <summary>Of those, how many selected a relayed (TURN) candidate pair -- i.e. actually needed the
        /// fallback because a direct UDP path to the JVB wasn't available for that participant.</summary>
        public long IceSucceededRelayedTotal { get; set; }
        /// <summary>0 when IceSucceededTotal is 0 (no data yet), not a divide-by-zero NaN.</summary>
        public double RelayedUsagePercent { get; set; }
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
        /// <summary>
        /// True for a server that's expected to not exist most of the time (e.g. the Hetzner
        /// burst-worker). The UI should render <see cref="Reachable"/> false + this true as a
        /// calm "Standby" state, not the same alarming "unreachable" treatment as an always-on
        /// server that's actually down.
        /// </summary>
        public bool IsOnDemand { get; set; }
        /// <summary>
        /// This server's configured container whitelist (see MonitoredServerOptions.Services),
        /// always populated regardless of <see cref="Reachable"/> -- unlike <see cref="Services"/>
        /// (live rn_service_active facts, empty when unreachable), log fetching only needs to
        /// know which container NAMES are valid to ask for, which is static config, not live data.
        /// Lets the log viewer stay usable for an on-demand server while it's in standby.
        /// </summary>
        public List<string> ConfiguredServices { get; set; } = new();
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
        public TurnStatusDto? TurnStatus { get; set; }
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
        /// <summary>True once a session_recordings row exists for this session. Only meaningful for a real batch class that has actually started -- always false for demo/personal-link sessions, which are never recorded by design.</summary>
        public bool HasRecording { get; set; }
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
    /// Disk-fill projection from the last 24 hours' trend (Prometheus's own deriv() function —
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

    /// <summary>
    /// Today's (IST) real batch classes vs. how many actually have a registered recording --
    /// same "started, not still live, no session_recordings row" definition used to trace
    /// individual sync failures by hand. StillProcessing is deliberately not counted as a
    /// failure: a class that ended a minute ago hasn't failed, it just hasn't synced yet.
    /// </summary>
    public class RecordingSummaryDto
    {
        public int Started { get; set; }
        public int Succeeded { get; set; }
        public int Failed { get; set; }
        public int StillProcessing { get; set; }
    }

    /// <summary>One create-to-delete (or still-running) lifecycle of the Hetzner burst worker.</summary>
    public class BurstWorkerEpisodeDto
    {
        public long ServerId { get; set; }
        public DateTime CreatedAtUtc { get; set; }
        /// <summary>Null means still running right now.</summary>
        public DateTime? DeletedAtUtc { get; set; }
        public double DurationHours { get; set; }
        /// <summary>At the configured Hetzner hourly rate, per minute actually run -- an estimate from our own tracked timestamps, not a real Hetzner invoice line.</summary>
        public double EstimatedCostUsd { get; set; }
        /// <summary>Whole hours, rounded UP (minimum 1) -- what this episode would cost if Hetzner bills every started hour in full. Unverified against a real invoice, so shown as an upper bound alongside the per-minute estimate.</summary>
        public int BilledHours { get; set; }
        public double MaxBilledCostUsd { get; set; }
        /// <summary>"auto" (created by the capacity check) or "manual" (someone pressed Start now on the dashboard).</summary>
        public string Trigger { get; set; } = "auto";
        /// <summary>Who pressed the button, for manual starts.</summary>
        public string? RequestedBy { get; set; }
        /// <summary>Recordings copied off this server to main while it was running -- what the spend actually bought.</summary>
        public int RecordingsHandled { get; set; }
    }

    /// <summary>The burst worker's state right now, read live from the cloud API (not inferred from the usage log).</summary>
    public class BurstWorkerStatusDto
    {
        /// <summary>False means the cloud API couldn't be read -- the UI must show "unknown", never assume standby.</summary>
        public bool Known { get; set; }
        public bool Exists { get; set; }
        public long? ServerId { get; set; }
        public DateTime? CreatedAtUtc { get; set; }
        /// <summary>Set while a manual "Start now" hold is active: the automatic teardown will not delete the server before this time, even if it is idle.</summary>
        public DateTime? HoldUntilUtc { get; set; }
    }

    /// <summary>Result of pressing Start now / Stop on the dashboard -- the last lines of the scale log so the admin sees what really happened.</summary>
    public class BurstWorkerControlResultDto
    {
        public string Action { get; set; } = string.Empty;
        public List<string> LogTail { get; set; } = new();
        public DateTime PerformedAtUtc { get; set; }
    }

    /// <summary>
    /// How often and for how long the on-demand Hetzner burst worker has actually been used,
    /// derived from burst-scale-up.sh/burst-scale-down.sh's own create/delete event log --
    /// there's no billing API call involved, just our own tracked timestamps at a known
    /// hourly rate, so treat the cost figures as an estimate, not an invoice.
    /// </summary>
    public class BurstWorkerUsageDto
    {
        public int EpisodesToday { get; set; }
        public double HoursToday { get; set; }
        public double EstimatedCostTodayUsd { get; set; }
        public double MaxBilledCostTodayUsd { get; set; }
        public int RecordingsHandledToday { get; set; }
        /// <summary>Current calendar month, IST.</summary>
        public int EpisodesThisMonth { get; set; }
        public double HoursThisMonth { get; set; }
        public double EstimatedCostThisMonthUsd { get; set; }
        public double MaxBilledCostThisMonthUsd { get; set; }
        public int RecordingsHandledThisMonth { get; set; }
        public int EpisodesAllTime { get; set; }
        public double HoursAllTime { get; set; }
        public double EstimatedCostAllTimeUsd { get; set; }
        public double MaxBilledCostAllTimeUsd { get; set; }
        /// <summary>The configured Hetzner hourly rate the cost figures use.</summary>
        public double HourlyRateUsd { get; set; }
        public bool CurrentlyActive { get; set; }
        public BurstWorkerStatusDto Status { get; set; } = new();
        /// <summary>The most recent scale-up / scale-down / copy / registration log lines, oldest first -- why it started, why it hasn't stopped, what it rescued.</summary>
        public List<string> RecentActivity { get; set; } = new();
        /// <summary>Most recent first, capped to a reasonable number for the dashboard -- not the full history.</summary>
        public List<BurstWorkerEpisodeDto> RecentEpisodes { get; set; } = new();
    }

    /// <summary>A recording that finished today but was deliberately not attached to any class (finalize hook got HTTP 204: personal/demo/ad-hoc room with no ClassSession).</summary>
    public class UnattachedRecordingDto
    {
        public string Room { get; set; } = "";
        public DateTime AtUtc { get; set; }
        public int DurationSeconds { get; set; }
    }

    /// <summary>
    /// Health of the recording pipeline on the video server: did today's finished recordings reach the
    /// portal, is anything stuck, and how much disk headroom is left.
    /// </summary>
    public class RecordingPipelineDto
    {
        /// <summary>Recordings attached to a class today (finalize hook returned 200).</summary>
        public int RegisteredToday { get; set; }
        /// <summary>Finished recordings not attached to any class today (HTTP 204) -- on disk, but will never show under a class.</summary>
        public int UnattachedToday { get; set; }
        /// <summary>Finalize calls today that ended in neither 200 nor 204 (last outcome per file).</summary>
        public int FailedToday { get; set; }
        /// <summary>Recordings copied to main whose registration failed and is still being retried.</summary>
        public int PendingRegistration { get; set; }
        public DateTime? NewestRecordingUtc { get; set; }
        public double DiskTotalGb { get; set; }
        public double DiskFreeGb { get; set; }
        public double DiskFreePercent { get; set; }
        public double GrowthLast24hGb { get; set; }
        /// <summary>Free disk divided by last-24h growth; null when nothing was written.</summary>
        public double? DaysOfDiskLeft { get; set; }
        public List<UnattachedRecordingDto> Unattached { get; set; } = new();
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
        public RecordingSummaryDto TodayRecordings { get; set; } = new();
        /// <summary>Null if the usage log couldn't be fetched (e.g. main unreachable) -- absent, not zeroed out, so the UI doesn't show a false "never used."</summary>
        public BurstWorkerUsageDto? BurstWorkerUsage { get; set; }
        /// <summary>Null if main could not be reached -- unknown, not "healthy".</summary>
        public RecordingPipelineDto? RecordingPipeline { get; set; }
        public DateTime GeneratedAtUtc { get; set; }
    }
}
