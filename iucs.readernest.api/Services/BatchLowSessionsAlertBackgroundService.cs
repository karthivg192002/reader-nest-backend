using iucs.readernest.application.Services;
using iucs.readernest.domain.Entities.Academics;
using iucs.readernest.domain.Entities.Communication;
using iucs.readernest.domain.Entities.Sessions;
using iucs.readernest.domain.Entities.Users;
using iucs.readernest.domain.Enums;
using iucs.readernest.domain.Repository;
using Microsoft.EntityFrameworkCore;

namespace iucs.readernest.api.Services
{
    /// <summary>
    /// Live, 4 Nov 2026: several children "lost" today's class -- it was never hidden, the batch's
    /// scheduled block had simply run out (parent cancellations and leave days use up classes
    /// without adding any) and nobody noticed until the family logged in. Once a day at 9 AM IST
    /// this emails every active Admin and RM (Sub Admin) one list of active batches with 2 or
    /// fewer upcoming classes, so the next set gets scheduled in time. One digest per day: a
    /// restart can't resend it, because the day's sent notification is checked first.
    /// </summary>
    public class BatchLowSessionsAlertBackgroundService : BackgroundService
    {
        public const string TemplateKey = "batch-low-sessions-digest";
        public const int Threshold = 2;
        private static readonly TimeSpan Interval = TimeSpan.FromMinutes(15);
        private static readonly TimeSpan Ist = TimeSpan.FromMinutes(330);

        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<BatchLowSessionsAlertBackgroundService> _logger;

        public BatchLowSessionsAlertBackgroundService(IServiceScopeFactory scopeFactory, ILogger<BatchLowSessionsAlertBackgroundService> logger)
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
                    var istNow = DateTime.UtcNow + Ist;
                    if (istNow.Hour == 9)
                    {
                        using var scope = _scopeFactory.CreateScope();
                        await SendDigestAsync(
                            scope.ServiceProvider.GetRequiredService<IUnitOfWork>(),
                            scope.ServiceProvider.GetRequiredService<INotificationService>(),
                            DateTime.UtcNow,
                            stoppingToken);
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogError(ex, "Low-sessions batch digest failed; retrying next interval.");
                }

                await Task.Delay(Interval, stoppingToken);
            }
        }

        /// <summary>Active batches with at least one active student and at most
        /// <see cref="Threshold"/> upcoming classes, as "name — N left" lines, oldest-running-out first.</summary>
        public static async Task<List<string>> FindRunningOutAsync(IUnitOfWork unitOfWork, DateTime nowUtc, CancellationToken cancellationToken)
        {
            var batches = await unitOfWork.Repository<Batch>().Query()
                .Where(b => b.Status == BatchStatus.Active
                    && b.Enrollments.Any(e => e.Status == EnrollmentStatus.Active))
                .Select(b => new { b.Id, b.Name })
                .ToListAsync(cancellationToken);
            var batchIds = batches.Select(b => b.Id).ToList();
            var upcoming = await unitOfWork.Repository<ClassSession>().Query()
                .Where(s => s.BatchId != null && batchIds.Contains(s.BatchId.Value)
                    && (s.Status == SessionStatus.Scheduled || s.Status == SessionStatus.CarriedForward)
                    && s.ScheduledStartAtUtc > nowUtc)
                .GroupBy(s => s.BatchId!.Value)
                .Select(g => new { BatchId = g.Key, Count = g.Count(), Last = g.Max(s => s.ScheduledStartAtUtc) })
                .ToListAsync(cancellationToken);
            var byBatch = upcoming.ToDictionary(u => u.BatchId);

            var rows = batches
                .Select(b => new
                {
                    b.Name,
                    Upcoming = byBatch.TryGetValue(b.Id, out var u) ? u.Count : 0,
                    Last = byBatch.TryGetValue(b.Id, out var u2) ? u2.Last : (DateTime?)null,
                })
                .Where(x => x.Upcoming <= Threshold)
                .ToList();

            return rows
                .OrderBy(r => r.Upcoming).ThenBy(r => r.Last)
                .Select(r => r.Upcoming == 0
                    ? $"{r.Name.Trim()} — no classes left"
                    : $"{r.Name.Trim()} — {r.Upcoming} left, last on {(r.Last!.Value + Ist):ddd d MMM}")
                .ToList();
        }

        /// <summary>Sends today's digest unless it already went out today (IST) or nothing is running out.</summary>
        public static async Task<int> SendDigestAsync(IUnitOfWork unitOfWork, INotificationService notifications, DateTime nowUtc, CancellationToken cancellationToken)
        {
            var istMidnightUtc = (nowUtc + Ist).Date - Ist;
            var alreadySent = await unitOfWork.Repository<Notification>().Query()
                .AnyAsync(n => n.TemplateKey == TemplateKey && n.CreatedAtUtc >= istMidnightUtc, cancellationToken);
            if (alreadySent)
            {
                return 0;
            }

            var lines = await FindRunningOutAsync(unitOfWork, nowUtc, cancellationToken);
            if (lines.Count == 0)
            {
                return 0;
            }

            var recipients = await unitOfWork.Repository<User>().Query()
                .Where(u => (u.Role == UserRole.Admin || u.Role == UserRole.SubAdmin) && u.Status == UserStatus.Active)
                .Select(u => new { u.Id, u.Email })
                .ToListAsync(cancellationToken);
            var tokens = new Dictionary<string, string>
            {
                ["Count"] = lines.Count.ToString(),
                ["BatchList"] = string.Join("\n", lines),
            };
            foreach (var r in recipients)
            {
                await notifications.SendTemplatedEmailAsync(r.Id, r.Email, NotificationType.General, TemplateKey, tokens, cancellationToken);
            }

            return recipients.Count;
        }
    }
}
