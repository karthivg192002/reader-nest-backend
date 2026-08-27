using System.Diagnostics;
using iucs.readernest.application.Common.Interfaces;
using iucs.readernest.application.Common.Options;
using iucs.readernest.application.Dto.Monitoring;
using iucs.readernest.domain.Entities.Settings;
using iucs.readernest.domain.Repository;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace iucs.readernest.application.Services
{
    /// <summary>
    /// Reads live server health from the Prometheus instance already running on the app server
    /// (node-exporter for OS metrics + a small per-server textfile-collector cron script for
    /// service up/down and Jitsi live-call counts — see docs/PROVISIONING.md). Deliberately
    /// PromQL-shaped rather than a bespoke per-server agent: one node-exporter deployment per
    /// box, one scrape config, no custom HTTP endpoints to secure.
    /// </summary>
    public class MonitoringService : IMonitoringService
    {
        private readonly IPrometheusClient _prometheus;
        private readonly IUnitOfWork _unitOfWork;
        private readonly IClassroomPresenceTracker _presenceTracker;
        private readonly MonitoringOptions _options;

        public MonitoringService(
            IPrometheusClient prometheus,
            IUnitOfWork unitOfWork,
            IClassroomPresenceTracker presenceTracker,
            IOptions<MonitoringOptions> options)
        {
            _prometheus = prometheus;
            _unitOfWork = unitOfWork;
            _presenceTracker = presenceTracker;
            _options = options.Value;
        }

        public async Task<MonitoringSummaryDto> GetSummaryAsync(CancellationToken cancellationToken = default)
        {
            var serverTasks = _options.Servers
                .Select(server => GetServerStatusAsync(server, cancellationToken))
                .ToList();
            var databaseTask = CheckDatabaseAsync(cancellationToken);
            var insightsTask = GetDatabaseInsightsAsync(cancellationToken);
            var alertsTask = _prometheus.GetActiveAlertsAsync(_options.PrometheusBaseUrl, cancellationToken);

            await Task.WhenAll(serverTasks.Cast<Task>().Append(databaseTask).Append(insightsTask).Append(alertsTask));
            var (dbHealthy, dbLatencyMs) = await databaseTask;

            return new MonitoringSummaryDto
            {
                Servers = serverTasks.Select(t => t.Result).ToList(),
                // Reaching this line at all means the API process is up and answering requests.
                ApiHealthy = true,
                DatabaseHealthy = dbHealthy,
                DatabaseLatencyMs = dbLatencyMs,
                DatabaseInsights = await insightsTask,
                ConcurrentClassroomUsers = _presenceTracker.TotalConnectedUsers,
                ActiveClassCount = _presenceTracker.ActiveClassCount,
                ActiveAlerts = (await alertsTask)
                    .Select(a => new AlertDto
                    {
                        Name = a.Name,
                        Severity = a.Severity,
                        Summary = a.Summary,
                        Description = a.Description,
                        State = a.State,
                        ActiveSince = a.ActiveSince,
                        Instance = a.Labels.TryGetValue("instance", out var instance) ? instance : null,
                    })
                    .OrderByDescending(a => a.Severity == "critical")
                    .ThenBy(a => a.ActiveSince)
                    .ToList(),
                GeneratedAtUtc = DateTime.UtcNow,
            };
        }

        private async Task<DatabaseInsightsDto?> GetDatabaseInsightsAsync(CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(_options.DatabaseName))
            {
                return null;
            }

            var baseUrl = _options.PrometheusBaseUrl;
            var db = EscapeLabelValue(_options.DatabaseName);

            var connectionsTask = _prometheus.QueryScalarAsync(baseUrl, $"pg_stat_database_numbackends{{datname=\"{db}\"}}", cancellationToken);
            var maxConnectionsTask = _prometheus.QueryScalarAsync(baseUrl, "pg_settings_max_connections", cancellationToken);
            var commitsTask = _prometheus.QueryScalarAsync(baseUrl, $"rate(pg_stat_database_xact_commit{{datname=\"{db}\"}}[5m])", cancellationToken);
            var rollbacksTask = _prometheus.QueryScalarAsync(baseUrl, $"rate(pg_stat_database_xact_rollback{{datname=\"{db}\"}}[5m])", cancellationToken);
            var cacheHitTask = _prometheus.QueryScalarAsync(
                baseUrl,
                $"100 * pg_stat_database_blks_hit{{datname=\"{db}\"}} / (pg_stat_database_blks_hit{{datname=\"{db}\"}} + pg_stat_database_blks_read{{datname=\"{db}\"}})",
                cancellationToken);
            var sizeTask = _prometheus.QueryScalarAsync(baseUrl, $"pg_database_size_bytes{{datname=\"{db}\"}} / 1048576", cancellationToken);
            var deadlocksTask = _prometheus.QueryScalarAsync(baseUrl, $"pg_stat_database_deadlocks{{datname=\"{db}\"}}", cancellationToken);
            var locksTask = _prometheus.QueryScalarAsync(baseUrl, "sum(pg_locks_count)", cancellationToken);

            await Task.WhenAll(connectionsTask, maxConnectionsTask, commitsTask, rollbacksTask, cacheHitTask, sizeTask, deadlocksTask, locksTask);

            var connections = await connectionsTask;
            if (connections is null)
            {
                // No data at all for this datname -- postgres-exporter unreachable or DB name misconfigured.
                return null;
            }

            return new DatabaseInsightsDto
            {
                ActiveConnections = (int)connections,
                MaxConnections = (int)(await maxConnectionsTask ?? 0),
                CommitsPerSecond = await commitsTask ?? 0,
                RollbacksPerSecond = await rollbacksTask ?? 0,
                CacheHitRatioPercent = Math.Clamp(await cacheHitTask ?? 0, 0, 100),
                DatabaseSizeMb = await sizeTask ?? 0,
                DeadlocksTotal = (long)(await deadlocksTask ?? 0),
                LocksHeld = (int)(await locksTask ?? 0),
            };
        }

        private async Task<ServerStatusDto> GetServerStatusAsync(MonitoredServerOptions server, CancellationToken cancellationToken)
        {
            var baseUrl = _options.PrometheusBaseUrl;
            var instanceLabel = EscapeLabelValue(server.Instance);

            var upTask = _prometheus.QueryScalarAsync(baseUrl, $"up{{instance=\"{instanceLabel}\"}}", cancellationToken);
            var freshnessTask = _prometheus.QueryScalarAsync(baseUrl, $"time() - timestamp(up{{instance=\"{instanceLabel}\"}})", cancellationToken);
            var cpuCoresTask = _prometheus.QueryScalarAsync(baseUrl, $"count(node_cpu_seconds_total{{instance=\"{instanceLabel}\",mode=\"idle\"}})", cancellationToken);
            var cpuUsageTask = _prometheus.QueryScalarAsync(baseUrl, $"100 - (avg(rate(node_cpu_seconds_total{{instance=\"{instanceLabel}\",mode=\"idle\"}}[2m])) * 100)", cancellationToken);
            var memUsedPercentTask = _prometheus.QueryScalarAsync(baseUrl, $"100 * (1 - node_memory_MemAvailable_bytes{{instance=\"{instanceLabel}\"}} / node_memory_MemTotal_bytes{{instance=\"{instanceLabel}\"}})", cancellationToken);
            var memTotalTask = _prometheus.QueryScalarAsync(baseUrl, $"node_memory_MemTotal_bytes{{instance=\"{instanceLabel}\"}} / 1048576", cancellationToken);
            var diskUsedPercentTask = _prometheus.QueryScalarAsync(baseUrl, $"100 * (1 - node_filesystem_avail_bytes{{instance=\"{instanceLabel}\",mountpoint=\"/\",fstype!=\"tmpfs\"}} / node_filesystem_size_bytes{{instance=\"{instanceLabel}\",mountpoint=\"/\",fstype!=\"tmpfs\"}})", cancellationToken);
            var diskTotalTask = _prometheus.QueryScalarAsync(baseUrl, $"node_filesystem_size_bytes{{instance=\"{instanceLabel}\",mountpoint=\"/\",fstype!=\"tmpfs\"}} / 1073741824", cancellationToken);
            var loadTask = _prometheus.QueryScalarAsync(baseUrl, $"node_load1{{instance=\"{instanceLabel}\"}}", cancellationToken);
            var uptimeTask = _prometheus.QueryScalarAsync(baseUrl, $"time() - node_boot_time_seconds{{instance=\"{instanceLabel}\"}}", cancellationToken);
            var servicesTask = _prometheus.QueryVectorAsync(baseUrl, $"rn_service_active{{instance=\"{instanceLabel}\"}}", cancellationToken);
            // eth0: the single external NIC on both boxes today -- summing every interface would double-count
            // traffic that also passes through docker0/br-*/veth* as it's routed into containers.
            var netRxTask = _prometheus.QueryScalarAsync(baseUrl, $"rate(node_network_receive_bytes_total{{instance=\"{instanceLabel}\",device=\"eth0\"}}[5m]) * 8 / 1000000", cancellationToken);
            var netTxTask = _prometheus.QueryScalarAsync(baseUrl, $"rate(node_network_transmit_bytes_total{{instance=\"{instanceLabel}\",device=\"eth0\"}}[5m]) * 8 / 1000000", cancellationToken);
            // Summed across block devices instead of a named one -- disk naming (sda/vda/nvme0n1) isn't
            // consistent across providers/servers, and unlike NICs there's no double-counting risk here.
            var diskReadTask = _prometheus.QueryScalarAsync(baseUrl, $"sum(rate(node_disk_read_bytes_total{{instance=\"{instanceLabel}\"}}[5m])) / 1048576", cancellationToken);
            var diskWriteTask = _prometheus.QueryScalarAsync(baseUrl, $"sum(rate(node_disk_written_bytes_total{{instance=\"{instanceLabel}\"}}[5m])) / 1048576", cancellationToken);

            // jitsi_jvb_conferences/jitsi_jvb_current_endpoints come straight from JVB's own
            // native Prometheus endpoint (see rn-jvb-metrics-proxy in the Prometheus scrape
            // config) -- real counts, not a derived/custom metric.
            // Last hour of CPU/memory, ~2-minute steps -- for the trend chart. Only worth the extra
            // round trip once we know the server is even up, so these fire after the up check below
            // rather than joining the big WhenAll batch.
            var conferencesTask = server.TracksLiveCalls
                ? _prometheus.QueryScalarAsync(baseUrl, $"jitsi_jvb_conferences{{instance=\"{instanceLabel}\"}}", cancellationToken)
                : Task.FromResult<double?>(null);
            var participantsTask = server.TracksLiveCalls
                ? _prometheus.QueryScalarAsync(baseUrl, $"jitsi_jvb_current_endpoints{{instance=\"{instanceLabel}\"}}", cancellationToken)
                : Task.FromResult<double?>(null);

            // Same JVB endpoint as above -- call quality, not just up/down.
            Task<double?> jvbMetric(string name) => server.TracksLiveCalls
                ? _prometheus.QueryScalarAsync(baseUrl, $"{name}{{instance=\"{instanceLabel}\"}}", cancellationToken)
                : Task.FromResult<double?>(null);
            var rttTask = jvbMetric("jitsi_jvb_average_rtt");
            var lossInTask = jvbMetric("jitsi_jvb_incoming_loss_fraction");
            var lossOutTask = jvbMetric("jitsi_jvb_outgoing_loss_fraction");
            var bitrateInTask = jvbMetric("jitsi_jvb_incoming_bitrate");
            var bitrateOutTask = jvbMetric("jitsi_jvb_outgoing_bitrate");
            var sendingAudioTask = jvbMetric("jitsi_jvb_endpoints_sending_audio");
            var sendingVideoTask = jvbMetric("jitsi_jvb_endpoints_sending_video");
            var stressTask = jvbMetric("jitsi_jvb_stress");
            var jvbHealthyTask = jvbMetric("jitsi_jvb_healthy");

            await Task.WhenAll(
                upTask, freshnessTask, cpuCoresTask, cpuUsageTask, memUsedPercentTask, memTotalTask,
                diskUsedPercentTask, diskTotalTask, loadTask, uptimeTask, servicesTask, conferencesTask, participantsTask,
                netRxTask, netTxTask, diskReadTask, diskWriteTask,
                rttTask, lossInTask, lossOutTask, bitrateInTask, bitrateOutTask, sendingAudioTask, sendingVideoTask, stressTask, jvbHealthyTask);

            var up = await upTask;
            if (up is not 1)
            {
                return new ServerStatusDto
                {
                    Name = server.Name,
                    Hostname = server.Hostname,
                    Reachable = false,
                    Error = up is null
                        ? "No data — this server isn't being scraped yet (check the Prometheus target)."
                        : "node-exporter on this server is down or unreachable.",
                };
            }

            var services = (await servicesTask)
                .Select(s => new MonitoredServiceDto
                {
                    Name = s.Labels.TryGetValue("name", out var name) ? name : "unknown",
                    Active = s.Value == 1,
                })
                .OrderBy(s => s.Name)
                .ToList();

            var conferences = await conferencesTask;
            var participants = await participantsTask;
            var jvbHealthy = await jvbHealthyTask;
            CallQualityDto? callQuality = server.TracksLiveCalls && jvbHealthy is not null
                ? new CallQualityDto
                {
                    AverageRttMs = await rttTask ?? 0,
                    IncomingLossPercent = Math.Clamp((await lossInTask ?? 0) * 100, 0, 100),
                    OutgoingLossPercent = Math.Clamp((await lossOutTask ?? 0) * 100, 0, 100),
                    IncomingBitrateKbps = (await bitrateInTask ?? 0) / 1000,
                    OutgoingBitrateKbps = (await bitrateOutTask ?? 0) / 1000,
                    EndpointsSendingAudio = (int)(await sendingAudioTask ?? 0),
                    EndpointsSendingVideo = (int)(await sendingVideoTask ?? 0),
                    JvbStressPercent = Math.Clamp((await stressTask ?? 0) * 100, 0, 100),
                    JvbHealthy = jvbHealthy == 1,
                }
                : null;

            var now = DateTime.UtcNow;
            var historyStart = now.AddHours(-1);
            var historyStep = TimeSpan.FromMinutes(2);
            var cpuHistoryTask = _prometheus.QueryRangeAsync(
                baseUrl, $"100 - (avg(rate(node_cpu_seconds_total{{instance=\"{instanceLabel}\",mode=\"idle\"}}[5m])) * 100)",
                historyStart, now, historyStep, cancellationToken);
            var memHistoryTask = _prometheus.QueryRangeAsync(
                baseUrl, $"100 * (1 - node_memory_MemAvailable_bytes{{instance=\"{instanceLabel}\"}} / node_memory_MemTotal_bytes{{instance=\"{instanceLabel}\"}})",
                historyStart, now, historyStep, cancellationToken);
            // deriv() is a real linear-regression rate over the window, not a naive two-point
            // delta -- exactly Prometheus's own tool for "is this trending toward a problem."
            var diskAvailBytesTask = _prometheus.QueryScalarAsync(
                baseUrl, $"node_filesystem_avail_bytes{{instance=\"{instanceLabel}\",mountpoint=\"/\",fstype!=\"tmpfs\"}}", cancellationToken);
            var diskTrendTask = _prometheus.QueryScalarAsync(
                baseUrl, $"deriv(node_filesystem_avail_bytes{{instance=\"{instanceLabel}\",mountpoint=\"/\",fstype!=\"tmpfs\"}}[6h])", cancellationToken);
            await Task.WhenAll(cpuHistoryTask, memHistoryTask, diskAvailBytesTask, diskTrendTask);

            var diskAvailBytes = await diskAvailBytesTask;
            var diskTrendBytesPerSec = await diskTrendTask;
            var diskForecast = new CapacityForecastDto
            {
                IsFilling = diskTrendBytesPerSec is < 0 && diskAvailBytes is > 0,
                TrendGbPerDay = (diskTrendBytesPerSec ?? 0) * 86400 / 1_073_741_824,
            };
            if (diskForecast.IsFilling)
            {
                diskForecast.DaysUntilFull = Math.Round(diskAvailBytes!.Value / -diskTrendBytesPerSec!.Value / 86400, 1);
            }

            return new ServerStatusDto
            {
                Name = server.Name,
                Hostname = server.Hostname,
                Reachable = true,
                UptimeSeconds = (long)(await uptimeTask ?? 0),
                LoadAverage1m = await loadTask ?? 0,
                CpuCores = (int)(await cpuCoresTask ?? 0),
                CpuUsagePercent = Clamp(await cpuUsageTask ?? 0),
                MemoryUsedPercent = Clamp(await memUsedPercentTask ?? 0),
                MemoryTotalMb = await memTotalTask ?? 0,
                DiskUsedPercent = Clamp(await diskUsedPercentTask ?? 0),
                DiskTotalGb = await diskTotalTask ?? 0,
                NetworkRxMbps = Math.Max(0, await netRxTask ?? 0),
                NetworkTxMbps = Math.Max(0, await netTxTask ?? 0),
                DiskReadMbps = Math.Max(0, await diskReadTask ?? 0),
                DiskWriteMbps = Math.Max(0, await diskWriteTask ?? 0),
                Services = services,
                AgentDataAgeSeconds = await freshnessTask ?? 0,
                CpuHistory = (await cpuHistoryTask).Select(p => new TimeSeriesPointDto { Timestamp = p.Timestamp, Value = Clamp(p.Value) }).ToList(),
                MemoryHistory = (await memHistoryTask).Select(p => new TimeSeriesPointDto { Timestamp = p.Timestamp, Value = Clamp(p.Value) }).ToList(),
                CallQuality = callQuality,
                DiskForecast = diskForecast,
                LiveCalls = server.TracksLiveCalls
                    ? new LiveCallSummaryDto
                    {
                        ActiveConferences = (int)(conferences ?? 0),
                        TotalParticipants = (int)(participants ?? 0),
                    }
                    : null,
            };
        }

        private async Task<(bool Healthy, double LatencyMs)> CheckDatabaseAsync(CancellationToken cancellationToken)
        {
            var stopwatch = Stopwatch.StartNew();
            try
            {
                await _unitOfWork.Repository<AppSetting>().Query().Take(1).AnyAsync(cancellationToken);
                stopwatch.Stop();
                return (true, stopwatch.Elapsed.TotalMilliseconds);
            }
            catch
            {
                stopwatch.Stop();
                return (false, stopwatch.Elapsed.TotalMilliseconds);
            }
        }

        private static double Clamp(double percent) => Math.Clamp(percent, 0, 100);

        /// <summary>PromQL label values are double-quoted strings — escape any embedded quote/backslash so a stray character in config can't break the query.</summary>
        private static string EscapeLabelValue(string value) => value.Replace("\\", "\\\\").Replace("\"", "\\\"");
    }
}
