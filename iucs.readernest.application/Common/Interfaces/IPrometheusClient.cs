namespace iucs.readernest.application.Common.Interfaces
{
    /// <summary>
    /// Runs instant PromQL queries against the Prometheus HTTP API. Implemented in the API layer
    /// (needs HttpClient infrastructure the application layer deliberately doesn't reference).
    /// </summary>
    public interface IPrometheusClient
    {
        /// <summary>
        /// Returns the first result's value, or null on any failure (unreachable Prometheus,
        /// bad query, no matching series) — one missing metric must never take the whole
        /// dashboard call down with it.
        /// </summary>
        Task<double?> QueryScalarAsync(string baseUrl, string promql, CancellationToken cancellationToken = default);

        /// <summary>
        /// Same as <see cref="QueryScalarAsync"/> but keeps every result series (with its labels)
        /// instead of just the first — for queries like `rn_service_active{instance="X"}` where
        /// each service is its own labeled series and the caller needs all of them in one round trip.
        /// </summary>
        Task<IReadOnlyList<PrometheusSeries>> QueryVectorAsync(string baseUrl, string promql, CancellationToken cancellationToken = default);

        /// <summary>
        /// Runs a range query (/api/v1/query_range) for a trend chart — the first result series's
        /// full set of (timestamp, value) samples between start and end, at the given step. Empty
        /// list on any failure, same "never take the dashboard down" contract as the other methods.
        /// </summary>
        Task<IReadOnlyList<(DateTime Timestamp, double Value)>> QueryRangeAsync(
            string baseUrl, string promql, DateTime start, DateTime end, TimeSpan step, CancellationToken cancellationToken = default);

        /// <summary>
        /// Currently pending/firing alerts from Prometheus's own rule evaluation (/api/v1/alerts) —
        /// the alert.rules.yml already loaded into this Prometheus instance (InstanceDown,
        /// HighCpuLoad, HighMemoryUsage, LowDiskSpace). Empty list on any failure, not an
        /// exception — an unreachable Prometheus must never look like "no alerts."
        /// </summary>
        Task<IReadOnlyList<PrometheusAlert>> GetActiveAlertsAsync(string baseUrl, CancellationToken cancellationToken = default);
    }

    public record PrometheusSeries(IReadOnlyDictionary<string, string> Labels, double Value);

    public record PrometheusAlert(
        string Name,
        string Severity,
        string Summary,
        string Description,
        string State,
        DateTime ActiveSince,
        IReadOnlyDictionary<string, string> Labels);
}
