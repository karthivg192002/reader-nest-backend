using System.Globalization;
using System.Text.Json;
using iucs.readernest.application.Common.Interfaces;

namespace iucs.readernest.api.Services
{
    /// <summary>
    /// Thin client for Prometheus's instant-query HTTP API (GET /api/v1/query?query=...).
    /// Handles both response shapes an instant query can return: "vector" (the normal case —
    /// value comes from result[0].value[1]) and "scalar" (a bare numeric expression — value
    /// comes straight from data.value[1]).
    /// </summary>
    public class PrometheusClient : IPrometheusClient
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILogger<PrometheusClient> _logger;

        public PrometheusClient(IHttpClientFactory httpClientFactory, ILogger<PrometheusClient> logger)
        {
            _httpClientFactory = httpClientFactory;
            _logger = logger;
        }

        public async Task<double?> QueryScalarAsync(string baseUrl, string promql, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(baseUrl) || string.IsNullOrWhiteSpace(promql))
            {
                return null;
            }

            try
            {
                var client = _httpClientFactory.CreateClient("Prometheus");
                var url = $"{baseUrl.TrimEnd('/')}/api/v1/query?query={Uri.EscapeDataString(promql)}";

                using var response = await client.GetAsync(url, cancellationToken);
                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning("Prometheus query returned {StatusCode} for: {Query}", (int)response.StatusCode, promql);
                    return null;
                }

                await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
                using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
                var root = doc.RootElement;

                if (!root.TryGetProperty("status", out var status) || status.GetString() != "success")
                {
                    return null;
                }

                var data = root.GetProperty("data");
                var resultType = data.GetProperty("resultType").GetString();

                JsonElement valueArray;
                if (resultType == "scalar")
                {
                    valueArray = data.GetProperty("value");
                }
                else
                {
                    var result = data.GetProperty("result");
                    if (result.GetArrayLength() == 0)
                    {
                        return null;
                    }
                    valueArray = result[0].GetProperty("value");
                }

                // value is [unixTimestamp, "stringNumber"] — the second element is always a string.
                var raw = valueArray[1].GetString();
                return raw is not null && double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
                    ? parsed
                    : null;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to query Prometheus at {BaseUrl}: {Query}", baseUrl, promql);
                return null;
            }
        }

        public async Task<IReadOnlyList<PrometheusSeries>> QueryVectorAsync(string baseUrl, string promql, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(baseUrl) || string.IsNullOrWhiteSpace(promql))
            {
                return Array.Empty<PrometheusSeries>();
            }

            try
            {
                var client = _httpClientFactory.CreateClient("Prometheus");
                var url = $"{baseUrl.TrimEnd('/')}/api/v1/query?query={Uri.EscapeDataString(promql)}";

                using var response = await client.GetAsync(url, cancellationToken);
                if (!response.IsSuccessStatusCode)
                {
                    return Array.Empty<PrometheusSeries>();
                }

                await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
                using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
                var root = doc.RootElement;

                if (!root.TryGetProperty("status", out var status) || status.GetString() != "success")
                {
                    return Array.Empty<PrometheusSeries>();
                }

                var result = root.GetProperty("data").GetProperty("result");
                var series = new List<PrometheusSeries>(result.GetArrayLength());
                foreach (var item in result.EnumerateArray())
                {
                    var labels = new Dictionary<string, string>();
                    foreach (var label in item.GetProperty("metric").EnumerateObject())
                    {
                        labels[label.Name] = label.Value.GetString() ?? string.Empty;
                    }

                    var raw = item.GetProperty("value")[1].GetString();
                    if (raw is not null && double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
                    {
                        series.Add(new PrometheusSeries(labels, value));
                    }
                }

                return series;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to query Prometheus at {BaseUrl}: {Query}", baseUrl, promql);
                return Array.Empty<PrometheusSeries>();
            }
        }

        public async Task<IReadOnlyList<(DateTime Timestamp, double Value)>> QueryRangeAsync(
            string baseUrl, string promql, DateTime start, DateTime end, TimeSpan step, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(baseUrl) || string.IsNullOrWhiteSpace(promql))
            {
                return Array.Empty<(DateTime, double)>();
            }

            try
            {
                var client = _httpClientFactory.CreateClient("Prometheus");
                var startUnix = new DateTimeOffset(start, TimeSpan.Zero).ToUnixTimeSeconds();
                var endUnix = new DateTimeOffset(end, TimeSpan.Zero).ToUnixTimeSeconds();
                var url =
                    $"{baseUrl.TrimEnd('/')}/api/v1/query_range?query={Uri.EscapeDataString(promql)}" +
                    $"&start={startUnix}&end={endUnix}&step={(int)step.TotalSeconds}";

                using var response = await client.GetAsync(url, cancellationToken);
                if (!response.IsSuccessStatusCode)
                {
                    return Array.Empty<(DateTime, double)>();
                }

                await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
                using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
                var root = doc.RootElement;

                if (!root.TryGetProperty("status", out var status) || status.GetString() != "success")
                {
                    return Array.Empty<(DateTime, double)>();
                }

                var result = root.GetProperty("data").GetProperty("result");
                if (result.GetArrayLength() == 0)
                {
                    return Array.Empty<(DateTime, double)>();
                }

                var values = result[0].GetProperty("values");
                var points = new List<(DateTime, double)>(values.GetArrayLength());
                foreach (var sample in values.EnumerateArray())
                {
                    var timestamp = DateTimeOffset.FromUnixTimeSeconds((long)sample[0].GetDouble()).UtcDateTime;
                    var raw = sample[1].GetString();
                    if (raw is not null && double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
                    {
                        points.Add((timestamp, value));
                    }
                }

                return points;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to range-query Prometheus at {BaseUrl}: {Query}", baseUrl, promql);
                return Array.Empty<(DateTime, double)>();
            }
        }

        public async Task<IReadOnlyList<PrometheusAlert>> GetActiveAlertsAsync(string baseUrl, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(baseUrl))
            {
                return Array.Empty<PrometheusAlert>();
            }

            try
            {
                var client = _httpClientFactory.CreateClient("Prometheus");
                var url = $"{baseUrl.TrimEnd('/')}/api/v1/alerts";

                using var response = await client.GetAsync(url, cancellationToken);
                if (!response.IsSuccessStatusCode)
                {
                    return Array.Empty<PrometheusAlert>();
                }

                await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
                using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
                var root = doc.RootElement;

                if (!root.TryGetProperty("status", out var status) || status.GetString() != "success")
                {
                    return Array.Empty<PrometheusAlert>();
                }

                var alertsElement = root.GetProperty("data").GetProperty("alerts");
                var alerts = new List<PrometheusAlert>(alertsElement.GetArrayLength());
                foreach (var item in alertsElement.EnumerateArray())
                {
                    var labels = new Dictionary<string, string>();
                    if (item.TryGetProperty("labels", out var labelsElement))
                    {
                        foreach (var label in labelsElement.EnumerateObject())
                        {
                            labels[label.Name] = label.Value.GetString() ?? string.Empty;
                        }
                    }

                    string? summary = null;
                    string? description = null;
                    if (item.TryGetProperty("annotations", out var annotationsElement))
                    {
                        if (annotationsElement.TryGetProperty("summary", out var s)) summary = s.GetString();
                        if (annotationsElement.TryGetProperty("description", out var d)) description = d.GetString();
                    }

                    var activeAt = item.TryGetProperty("activeAt", out var activeAtElement) && activeAtElement.GetDateTime() is var parsed
                        ? parsed
                        : DateTime.UtcNow;

                    alerts.Add(new PrometheusAlert(
                        Name: labels.TryGetValue("alertname", out var name) ? name : "Alert",
                        Severity: labels.TryGetValue("severity", out var severity) ? severity : "warning",
                        Summary: summary ?? string.Empty,
                        Description: description ?? string.Empty,
                        State: item.TryGetProperty("state", out var stateElement) ? stateElement.GetString() ?? "pending" : "pending",
                        ActiveSince: activeAt,
                        Labels: labels));
                }

                return alerts;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to fetch active alerts from Prometheus at {BaseUrl}", baseUrl);
                return Array.Empty<PrometheusAlert>();
            }
        }
    }
}
