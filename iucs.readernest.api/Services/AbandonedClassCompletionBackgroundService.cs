using iucs.readernest.application.Services;
using iucs.readernest.domain.Entities.Sessions;
using iucs.readernest.domain.Enums;
using iucs.readernest.domain.Repository;
using Microsoft.EntityFrameworkCore;

namespace iucs.readernest.api.Services
{
    /// <summary>
    /// Completes a class the teacher walked out of without pressing End Class. Such a class used
    /// to sit InProgress forever — never completed, never accrued, and so never flagged for
    /// payout approval even when the teacher left well before the scheduled end. Runs every 10
    /// minutes; a session qualifies once it is <see cref="Grace"/> past its scheduled end AND the
    /// teacher's own attendance shows them disconnected for at least <see cref="Grace"/> (a
    /// rejoin clears <see cref="SessionAttendance.LeftAtUtc"/>, so a teacher still teaching past
    /// the slot — the "Continue Class" flow — is never touched). The class is completed as of
    /// the moment the teacher left, which is what makes a short class land in Payout Approvals
    /// and alert Admin / Management like any other.
    /// </summary>
    public class AbandonedClassCompletionBackgroundService : BackgroundService
    {
        private static readonly TimeSpan Interval = TimeSpan.FromMinutes(10);
        private static readonly TimeSpan Grace = TimeSpan.FromMinutes(15);

        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<AbandonedClassCompletionBackgroundService> _logger;

        public AbandonedClassCompletionBackgroundService(
            IServiceScopeFactory scopeFactory,
            ILogger<AbandonedClassCompletionBackgroundService> logger)
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
                    await RunCycleAsync(stoppingToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogError(ex, "Abandoned-class completion cycle failed; retrying next interval.");
                }

                await Task.Delay(Interval, stoppingToken);
            }
        }

        private async Task RunCycleAsync(CancellationToken cancellationToken)
        {
            using var scope = _scopeFactory.CreateScope();
            var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            var sessionService = scope.ServiceProvider.GetRequiredService<ISessionService>();

            var cutoff = DateTime.UtcNow.Subtract(Grace);
            var abandoned = await unitOfWork.Repository<ClassSession>().Query()
                .Where(s => s.Status == SessionStatus.InProgress && s.ScheduledEndAtUtc < cutoff)
                .Join(unitOfWork.Repository<SessionAttendance>().Query(),
                    s => new { SessionId = s.Id, TeacherId = (Guid?)s.TeacherProfileId },
                    a => new { SessionId = a.ClassSessionId, TeacherId = a.TeacherProfileId },
                    (s, a) => new { s.Id, a.LeftAtUtc })
                .Where(x => x.LeftAtUtc != null && x.LeftAtUtc < cutoff)
                .ToListAsync(cancellationToken);

            var completed = 0;
            foreach (var item in abandoned)
            {
                // Isolated per session: a race with a teacher/admin completing it by hand must
                // not stop the rest of this cycle.
                try
                {
                    await sessionService.CompleteAbandonedAsync(item.Id, item.LeftAtUtc!.Value, cancellationToken);
                    completed++;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogWarning(ex, "Could not auto-complete abandoned session {SessionId}; continuing.", item.Id);
                }
            }

            if (completed > 0)
            {
                _logger.LogInformation("Auto-completed {Count} class(es) the teacher left without ending.", completed);
            }
        }
    }
}
