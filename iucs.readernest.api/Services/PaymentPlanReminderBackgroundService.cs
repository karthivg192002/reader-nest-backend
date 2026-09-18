using iucs.readernest.application.Common;
using iucs.readernest.application.Common.Interfaces;
using iucs.readernest.application.Services;
using iucs.readernest.domain.Entities.Academics;
using iucs.readernest.domain.Entities.Sessions;
using iucs.readernest.domain.Enums;
using iucs.readernest.domain.Repository;
using Microsoft.EntityFrameworkCore;

namespace iucs.readernest.api.Services
{
    /// <summary>
    /// Drives the batch-level "Payment plan" (Full payment done / Payment after N sessions /
    /// Payment due on a specific date — see Batch.PaymentPlanType): once a day, finds every
    /// AfterSessions/DueOnDate batch whose trigger has now been reached and hasn't already sent
    /// its one-shot reminder, and emails every actively-enrolled parent. Runs on the same hourly
    /// tick + 08:00 UTC gate as BillingBackgroundService's own SendPaymentRemindersAsync, so
    /// every payment-related email goes out in the same daily window.
    /// </summary>
    public class PaymentPlanReminderBackgroundService : BackgroundService
    {
        private static readonly TimeSpan Interval = TimeSpan.FromHours(1);

        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<PaymentPlanReminderBackgroundService> _logger;

        public PaymentPlanReminderBackgroundService(IServiceScopeFactory scopeFactory, ILogger<PaymentPlanReminderBackgroundService> logger)
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
                    if (DateTime.UtcNow.Hour == 8)
                    {
                        await RunCycleAsync(stoppingToken);
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogError(ex, "Payment plan reminder cycle failed; retrying next interval.");
                }

                await Task.Delay(Interval, stoppingToken);
            }
        }

        private async Task RunCycleAsync(CancellationToken cancellationToken)
        {
            using var scope = _scopeFactory.CreateScope();
            var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            var notifications = scope.ServiceProvider.GetRequiredService<INotificationService>();

            if (!await NotificationToggles.IsEnabledAsync(unitOfWork, NotificationToggles.FeeReminders, cancellationToken))
            {
                return;
            }

            var today = DateOnly.FromDateTime(DateTime.UtcNow);

            // Archived batches are done with — nobody's tracking their collections here anymore;
            // Active and Dormant (just-finished) batches both still matter.
            var pending = await unitOfWork.Repository<Batch>().TrackedQuery()
                .Where(b => b.Status != BatchStatus.Archived
                    && b.PaymentReminderSentAtUtc == null
                    && ((b.PaymentPlanType == BatchPaymentPlanType.AfterSessions && b.PaymentAfterSessionsCount != null)
                        || (b.PaymentPlanType == BatchPaymentPlanType.DueOnDate && b.PaymentDueDate != null && b.PaymentDueDate <= today)))
                .ToListAsync(cancellationToken);

            if (pending.Count == 0)
            {
                return;
            }

            // AfterSessions candidates need each batch's own completed-session count, which
            // DueOnDate candidates don't — one grouped query for the whole cycle instead of one
            // per batch.
            var sessionPlanBatchIds = pending
                .Where(b => b.PaymentPlanType == BatchPaymentPlanType.AfterSessions)
                .Select(b => b.Id)
                .ToList();
            var completedCountByBatch = new Dictionary<Guid, int>();
            if (sessionPlanBatchIds.Count > 0)
            {
                completedCountByBatch = await unitOfWork.Repository<ClassSession>().Query()
                    .Where(s => s.BatchId.HasValue && sessionPlanBatchIds.Contains(s.BatchId.Value) && s.Status == SessionStatus.Completed)
                    .GroupBy(s => s.BatchId!.Value)
                    .Select(g => new { BatchId = g.Key, Count = g.Count() })
                    .ToDictionaryAsync(g => g.BatchId, g => g.Count, cancellationToken);
            }

            var due = pending
                .Where(b => b.PaymentPlanType == BatchPaymentPlanType.DueOnDate
                    || completedCountByBatch.GetValueOrDefault(b.Id) >= b.PaymentAfterSessionsCount!.Value)
                .ToList();

            if (due.Count == 0)
            {
                return;
            }

            var batchIds = due.Select(b => b.Id).ToList();
            var parentUsersByBatchId = (await unitOfWork.Repository<BatchEnrollment>().Query()
                    .Where(e => batchIds.Contains(e.BatchId) && e.Status == EnrollmentStatus.Active)
                    .Select(e => new { e.BatchId, User = e.Child.ParentProfile.User })
                    .ToListAsync(cancellationToken))
                .GroupBy(e => e.BatchId)
                .ToDictionary(g => g.Key, g => g.Select(e => e.User).DistinctBy(u => u.Id).ToList());

            var remindedCount = 0;
            foreach (var batch in due)
            {
                var parents = parentUsersByBatchId.GetValueOrDefault(batch.Id, []);
                var templateKey = batch.PaymentPlanType == BatchPaymentPlanType.AfterSessions
                    ? "payment-plan-reminder-sessions"
                    : "payment-plan-reminder-date";
                var placeholders = batch.PaymentPlanType == BatchPaymentPlanType.AfterSessions
                    ? new Dictionary<string, string>
                    {
                        ["BatchName"] = batch.Name,
                        ["SessionCount"] = batch.PaymentAfterSessionsCount!.Value.ToString(),
                    }
                    : new Dictionary<string, string>
                    {
                        ["BatchName"] = batch.Name,
                        ["DueDate"] = batch.PaymentDueDate!.Value.ToString("yyyy-MM-dd"),
                    };

                foreach (var parent in parents)
                {
                    try
                    {
                        await notifications.SendTemplatedEmailAsync(
                            parent.Id, parent.Email, NotificationType.PaymentReminder, templateKey, placeholders, cancellationToken);
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        _logger.LogWarning(ex, "Payment plan reminder failed for batch {BatchId}, parent {ParentUserId}; continuing.", batch.Id, parent.Id);
                    }
                }

                // One-shot: marked sent once every parent's been attempted, even if a recipient's
                // own send failed — otherwise a single bad address would re-alert every one of
                // this batch's other parents daily forever. A corrected plan (BatchService.UpdateAsync)
                // clears this back to null and re-arms it.
                batch.PaymentReminderSentAtUtc = DateTime.UtcNow;
                remindedCount++;
            }

            await unitOfWork.SaveChangesAsync(cancellationToken);
            _logger.LogInformation("Payment plan reminders: sent for {BatchCount} batch(es).", remindedCount);
        }
    }
}
