using iucs.readernest.application.Services;
using iucs.readernest.domain.Entities.Academics;
using iucs.readernest.domain.Entities.Sessions;
using iucs.readernest.domain.Entities.Users;
using iucs.readernest.domain.Enums;
using iucs.readernest.domain.Repository;
using Microsoft.EntityFrameworkCore;

namespace iucs.readernest.api.Services
{
    /// <summary>
    /// Email reminders and alerts around class time. Runs every 10 minutes; each cycle
    /// reminds for sessions starting inside the next 10-minute-wide window one hour out
    /// (stateless de-duplication: a session falls into exactly one window), and raises
    /// delayed-session alerts for classes that never started.
    /// </summary>
    public class SessionReminderBackgroundService : BackgroundService
    {
        private static readonly TimeSpan Interval = TimeSpan.FromMinutes(10);
        private static readonly TimeSpan ReminderLead = TimeSpan.FromHours(1);

        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<SessionReminderBackgroundService> _logger;

        public SessionReminderBackgroundService(
            IServiceScopeFactory scopeFactory,
            ILogger<SessionReminderBackgroundService> logger)
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
                    _logger.LogError(ex, "Session reminder cycle failed; retrying next interval.");
                }

                await Task.Delay(Interval, stoppingToken);
            }
        }

        private async Task RunCycleAsync(CancellationToken cancellationToken)
        {
            using var scope = _scopeFactory.CreateScope();
            var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            var notifications = scope.ServiceProvider.GetRequiredService<INotificationService>();

            var now = DateTime.UtcNow;

            // One-hour reminders: sessions starting in [lead, lead + interval)
            var windowStart = now.Add(ReminderLead);
            var windowEnd = windowStart.Add(Interval);
            var upcoming = await unitOfWork.Repository<ClassSession>().Query()
                .Include(s => s.TeacherProfile).ThenInclude(t => t.User)
                .Where(s => (s.Status == SessionStatus.Scheduled || s.Status == SessionStatus.CarriedForward)
                            && s.ScheduledStartAtUtc >= windowStart
                            && s.ScheduledStartAtUtc < windowEnd)
                .ToListAsync(cancellationToken);

            foreach (var session in upcoming)
            {
                var teacherUser = session.TeacherProfile.User;
                await notifications.SendEmailAsync(
                    teacherUser.Id, teacherUser.Email, NotificationType.SessionReminder,
                    "Class starts in 1 hour",
                    $"Your {session.Type} session starts at {session.ScheduledStartAtUtc:u}.",
                    cancellationToken);

                if (session.BatchId is null)
                {
                    continue;
                }

                var parentUsers = await unitOfWork.Repository<BatchEnrollment>().Query()
                    .Where(e => e.BatchId == session.BatchId && e.Status == EnrollmentStatus.Active)
                    .Select(e => e.Child.ParentProfile.User)
                    .Distinct()
                    .ToListAsync(cancellationToken);
                foreach (var parent in parentUsers)
                {
                    await notifications.SendEmailAsync(
                        parent.Id, parent.Email, NotificationType.SessionReminder,
                        "Class starts in 1 hour",
                        $"Your child's class starts at {session.ScheduledStartAtUtc:u}. Join from the parent dashboard.",
                        cancellationToken);
                }
            }

            // Delayed-session alerts: still Scheduled although the start fell in the last window
            var delayed = await unitOfWork.Repository<ClassSession>().Query()
                .Include(s => s.TeacherProfile).ThenInclude(t => t.User)
                .Where(s => s.Status == SessionStatus.Scheduled
                            && s.ActualStartAtUtc == null
                            && s.ScheduledStartAtUtc < now.Subtract(Interval)
                            && s.ScheduledStartAtUtc >= now.Subtract(Interval + Interval))
                .ToListAsync(cancellationToken);

            if (delayed.Count > 0)
            {
                var admins = await unitOfWork.Repository<User>().Query()
                    .Where(u => u.Role == UserRole.Admin && u.Status == UserStatus.Active)
                    .ToListAsync(cancellationToken);
                foreach (var session in delayed)
                {
                    foreach (var admin in admins)
                    {
                        await notifications.SendEmailAsync(
                            admin.Id, admin.Email, NotificationType.DelayedSessionAlert,
                            "Session has not started",
                            $"The session scheduled at {session.ScheduledStartAtUtc:u} (teacher {session.TeacherProfile.User.FirstName} {session.TeacherProfile.User.LastName}) has not started.",
                            cancellationToken);
                    }
                }
            }

            if (upcoming.Count > 0 || delayed.Count > 0)
            {
                _logger.LogInformation(
                    "Reminders: {ReminderCount} session reminder group(s), {DelayedCount} delayed alert(s).",
                    upcoming.Count, delayed.Count);
            }
        }
    }
}
