using iucs.readernest.application.Services;

namespace iucs.readernest.api.Services
{
    /// <summary>
    /// Writes the month-end leave and class-cancellation report at 23:59 IST on the last day of
    /// each month (30th/31st/28th/29th). Checks every minute; a month that already has a
    /// snapshot is never rewritten, and a missed month-end (restart, outage) is back-filled on
    /// the next run.
    /// </summary>
    public class LeaveCancellationReportBackgroundService : BackgroundService
    {
        private static readonly TimeSpan Interval = TimeSpan.FromMinutes(1);

        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<LeaveCancellationReportBackgroundService> _logger;

        public LeaveCancellationReportBackgroundService(IServiceScopeFactory scopeFactory, ILogger<LeaveCancellationReportBackgroundService> logger)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    using var scope = _scopeFactory.CreateScope();
                    var reports = scope.ServiceProvider.GetRequiredService<ILeaveCancellationReportService>();
                    var generated = await reports.EnsureSnapshotsAsync(DateTime.UtcNow, stoppingToken);
                    foreach (var (year, month) in generated)
                    {
                        _logger.LogInformation("Leave cancellation report generated for {Year}-{Month:D2}.", year, month);
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogError(ex, "Month-end leave cancellation report failed; retrying next cycle.");
                }

                await Task.Delay(Interval, stoppingToken);
            }
        }
    }
}
