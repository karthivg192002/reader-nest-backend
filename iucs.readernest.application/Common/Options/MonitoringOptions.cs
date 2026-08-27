namespace iucs.readernest.application.Common.Options
{
    /// <summary>
    /// Binds the "Monitoring" config section. Most of the dashboard reads from the Prometheus
    /// instance already running on the app server (see docs/PROVISIONING.md's monitoring
    /// section) — node-exporter for OS metrics on every server, plus a small textfile-collector
    /// cron script per server that publishes service up/down and (on the Jitsi box) live-
    /// conference counts as plain Prometheus metrics; Prometheus itself has no auth in front of
    /// it today (internal network only). The one exception is each server's Ssh* fields, used
    /// only to fetch docker container logs on demand (see IServerLogService) — SshPassword must
    /// never be set in appsettings.json (it's committed to git); it's supplied purely via the
    /// Monitoring__Servers__{index}__SshPassword environment variable at deploy time.
    /// </summary>
    public class MonitoringOptions
    {
        public const string SectionName = "Monitoring";

        /// <summary>Base URL of the Prometheus HTTP API, e.g. "http://prometheus:9090" (Docker network name) or "http://204.168.140.222:9090".</summary>
        public string PrometheusBaseUrl { get; set; } = string.Empty;

        /// <summary>Postgres datname to scope the Database Insights panel to (postgres-exporter also sees the UAT and system databases sharing this instance).</summary>
        public string DatabaseName { get; set; } = string.Empty;

        public List<MonitoredServerOptions> Servers { get; set; } = new();
    }

    public class MonitoredServerOptions
    {
        /// <summary>Display name shown on the dashboard, e.g. "Jitsi / Video".</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>Display hostname, e.g. "thereadernest.co.in".</summary>
        public string Hostname { get; set; } = string.Empty;

        /// <summary>The Prometheus "instance" label this server's node-exporter (and textfile facts) are scraped under.</summary>
        public string Instance { get; set; } = string.Empty;

        /// <summary>rn_service_active{name=...} values published by this server's textfile-collector script.</summary>
        public List<string> Services { get; set; } = new();

        /// <summary>True only for the Jitsi box — queries rn_jitsi_conferences/rn_jitsi_participants for this instance.</summary>
        public bool TracksLiveCalls { get; set; }

        /// <summary>SSH-reachable address for this server, e.g. "204.168.140.222" — NOT necessarily the same as <see cref="Hostname"/> (a public domain) or <see cref="Instance"/> (a Prometheus scrape label, which for a self-scraped box is "host.docker.internal" and isn't externally reachable at all).</summary>
        public string SshHost { get; set; } = string.Empty;

        public int SshPort { get; set; } = 22;

        public string SshUsername { get; set; } = string.Empty;

        /// <summary>Never set this in appsettings.json — see the class-level remark. Empty means log fetching is unavailable for this server.</summary>
        public string SshPassword { get; set; } = string.Empty;
    }
}
