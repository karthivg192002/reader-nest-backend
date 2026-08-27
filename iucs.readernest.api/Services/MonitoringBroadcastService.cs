using iucs.readernest.api.Hubs;
using iucs.readernest.application.Services;
using Microsoft.AspNetCore.SignalR;

namespace iucs.readernest.api.Services
{
    /// <summary>
    /// Pushes the same summary GET /api/monitoring/summary would return to every client
    /// connected to MonitoringHub, on a fixed cycle — replacing the frontend's old 20s poll
    /// with a server-initiated push. The interval still can't outrun Prometheus's own scrape
    /// interval (~15-30s upstream), so this doesn't make the data any fresher than polling
    /// did; it only moves who initiates the refresh. Skips the Prometheus round-trip entirely
    /// while MonitoringConnectionTracker reports nobody's connected.
    /// </summary>
    public class MonitoringBroadcastService : BackgroundService
    {
        private static readonly TimeSpan Interval = TimeSpan.FromSeconds(15);

        private readonly IServiceScopeFactory _scopeFactory;
        private readonly IHubContext<MonitoringHub> _hubContext;
        private readonly MonitoringConnectionTracker _tracker;
        private readonly ILogger<MonitoringBroadcastService> _logger;

        public MonitoringBroadcastService(
            IServiceScopeFactory scopeFactory,
            IHubContext<MonitoringHub> hubContext,
            MonitoringConnectionTracker tracker,
            ILogger<MonitoringBroadcastService> logger)
        {
            _scopeFactory = scopeFactory;
            _hubContext = hubContext;
            _tracker = tracker;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    if (_tracker.HasConnections)
                    {
                        using var scope = _scopeFactory.CreateScope();
                        var monitoring = scope.ServiceProvider.GetRequiredService<IMonitoringService>();
                        var summary = await monitoring.GetSummaryAsync(stoppingToken);
                        await _hubContext.Clients.All.SendAsync("MonitoringUpdate", summary, stoppingToken);
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogError(ex, "Monitoring live broadcast failed; retrying next cycle.");
                }

                await Task.Delay(Interval, stoppingToken);
            }
        }
    }
}
