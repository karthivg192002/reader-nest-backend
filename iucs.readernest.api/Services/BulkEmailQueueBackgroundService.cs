using iucs.readernest.application.Services;

namespace iucs.readernest.api.Services
{
    /// <summary>
    /// Drains the Bulk Email queue: every recipient row left Pending by a send is delivered
    /// here, off the admin's HTTP request. Because the queue is the database, a restart just
    /// resumes whatever is still Pending.
    /// </summary>
    public class BulkEmailQueueBackgroundService : BackgroundService
    {
        private static readonly TimeSpan Interval = TimeSpan.FromSeconds(3);

        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<BulkEmailQueueBackgroundService> _logger;

        public BulkEmailQueueBackgroundService(IServiceScopeFactory scopeFactory, ILogger<BulkEmailQueueBackgroundService> logger)
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
                    var reports = scope.ServiceProvider.GetRequiredService<IReportsService>();
                    var sent = await reports.ProcessPendingBulkEmailAsync(stoppingToken);
                    if (sent > 0) _logger.LogInformation("Bulk email queue: attempted {Count} recipient(s).", sent);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogError(ex, "Bulk email queue cycle failed; retrying shortly.");
                }

                try { await Task.Delay(Interval, stoppingToken); }
                catch (OperationCanceledException) { break; }
            }
        }
    }
}
