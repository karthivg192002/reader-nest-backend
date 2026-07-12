using iucs.readernest.application.Services;
using iucs.readernest.domain.Entities.Users;
using iucs.readernest.domain.Enums;
using iucs.readernest.domain.Repository;
using Microsoft.EntityFrameworkCore;

namespace iucs.readernest.api.Services
{
    /// <summary>
    /// Automated reports: every Monday (07:00 UTC cycle) the admin team receives a
    /// KPI digest email generated from the live dashboard aggregates.
    /// </summary>
    public class ReportsDigestBackgroundService : BackgroundService
    {
        private static readonly TimeSpan Interval = TimeSpan.FromHours(1);

        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<ReportsDigestBackgroundService> _logger;

        public ReportsDigestBackgroundService(IServiceScopeFactory scopeFactory, ILogger<ReportsDigestBackgroundService> logger)
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
                    var now = DateTime.UtcNow;
                    if (now.DayOfWeek == DayOfWeek.Monday && now.Hour == 7)
                    {
                        await SendDigestAsync(stoppingToken);
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogError(ex, "Weekly report digest failed; retrying next cycle.");
                }

                await Task.Delay(Interval, stoppingToken);
            }
        }

        private async Task SendDigestAsync(CancellationToken cancellationToken)
        {
            using var scope = _scopeFactory.CreateScope();
            var reports = scope.ServiceProvider.GetRequiredService<IReportsService>();
            var notifications = scope.ServiceProvider.GetRequiredService<INotificationService>();
            var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

            var summary = await reports.GetDashboardSummaryAsync(cancellationToken);
            var body =
                $"Weekly Reader Nest KPI digest\n\n" +
                $"Students: {summary.TotalStudents} total / {summary.ActiveStudents} active\n" +
                $"Revenue: {summary.RevenueCollected:0.00} collected, {summary.RevenuePending:0.00} pending\n" +
                $"Enrollments: {summary.TotalEnrollments}\n" +
                $"Batches: {summary.ActiveBatches} active / {summary.DormantBatches} dormant " +
                $"({summary.BatchOccupancyPercent}% occupancy)\n" +
                $"Conversion rate: {summary.ConversionRatePercent}% · Refund rate: {summary.RefundRatePercent}%\n" +
                $"Teacher utilization: {summary.TeacherUtilizationSessionsPerTeacher} sessions/teacher (30d)";

            var admins = await unitOfWork.Repository<User>().Query()
                .Where(u => u.Role == UserRole.Admin && u.Status == UserStatus.Active)
                .ToListAsync(cancellationToken);
            foreach (var admin in admins)
            {
                await notifications.SendEmailAsync(
                    admin.Id, admin.Email, NotificationType.General,
                    "Weekly KPI digest", body, cancellationToken);
            }

            _logger.LogInformation("Weekly KPI digest sent to {Count} admin(s).", admins.Count);
        }
    }
}
