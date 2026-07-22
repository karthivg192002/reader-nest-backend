using iucs.readernest.application.Common.Interfaces;
using iucs.readernest.application.Helper;
using iucs.readernest.application.Services;
using iucs.readernest.domain.Entities.Academics;
using iucs.readernest.domain.Entities.Admission;
using iucs.readernest.domain.Entities.Integrations;
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
            var emailTemplates = scope.ServiceProvider.GetRequiredService<IEmailTemplateService>();
            var emailSender = scope.ServiceProvider.GetRequiredService<IEmailSender>();

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

            string? jitsiConfigJson = null;
            if (upcoming.Count > 0)
            {
                jitsiConfigJson = await unitOfWork.Repository<Integration>().Query()
                    .Where(i => i.Key == "jitsi")
                    .Select(i => i.ConfigJson)
                    .FirstOrDefaultAsync(cancellationToken);
            }

            foreach (var session in upcoming)
            {
                var teacherUser = session.TeacherProfile.User;
                await notifications.SendTemplatedEmailAsync(
                    teacherUser.Id, teacherUser.Email, NotificationType.SessionReminder,
                    "session-reminder-teacher",
                    new Dictionary<string, string>
                    {
                        ["TeacherFirstName"] = teacherUser.FirstName,
                        ["SessionType"] = session.Type.ToString(),
                        ["StartLocal"] = FormatLocal(session.ScheduledStartAtUtc, teacherUser.TimeZoneId),
                    },
                    cancellationToken);

                var joinUrl = JitsiLinkBuilder.BuildJoinUrl(session.MeetingRoomId, jitsiConfigJson) ?? "#";

                if (session.BatchId is null)
                {
                    // Demo sessions have no batch — the lead is tracked via DemoBooking.ParentEmail,
                    // which may not correspond to a real account yet, so this bypasses the
                    // user-bound notification log the same way the initial confirmation email does.
                    var demoBooking = await unitOfWork.Repository<DemoBooking>().Query()
                        .FirstOrDefaultAsync(b => b.ClassSessionId == session.Id, cancellationToken);
                    if (demoBooking is not null)
                    {
                        var (subject, body) = await emailTemplates.RenderAsync(
                            "session-reminder-parent",
                            new Dictionary<string, string>
                            {
                                ["StartLocal"] = FormatLocal(session.ScheduledStartAtUtc, "Asia/Kolkata"),
                                ["JoinUrl"] = joinUrl,
                            },
                            cancellationToken);
                        await emailSender.SendAsync(demoBooking.ParentEmail, subject, body, cancellationToken);
                    }

                    continue;
                }

                var parentUsers = await unitOfWork.Repository<BatchEnrollment>().Query()
                    .Where(e => e.BatchId == session.BatchId && e.Status == EnrollmentStatus.Active)
                    .Select(e => e.Child.ParentProfile.User)
                    .Distinct()
                    .ToListAsync(cancellationToken);
                foreach (var parent in parentUsers)
                {
                    await notifications.SendTemplatedEmailAsync(
                        parent.Id, parent.Email, NotificationType.SessionReminder,
                        "session-reminder-parent",
                        new Dictionary<string, string>
                        {
                            ["StartLocal"] = FormatLocal(session.ScheduledStartAtUtc, parent.TimeZoneId),
                            ["JoinUrl"] = joinUrl,
                        },
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
                        await notifications.SendTemplatedEmailAsync(
                            admin.Id, admin.Email, NotificationType.DelayedSessionAlert,
                            "delayed-session-alert",
                            new Dictionary<string, string>
                            {
                                ["StartLocal"] = FormatLocal(session.ScheduledStartAtUtc, admin.TimeZoneId),
                                ["TeacherName"] = $"{session.TeacherProfile.User.FirstName} {session.TeacherProfile.User.LastName}",
                            },
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

        /// <summary>Multi-timezone support: renders a UTC instant in the recipient's own zone.</summary>
        private static string FormatLocal(DateTime utc, string timeZoneId)
        {
            try
            {
                var zone = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
                var local = TimeZoneInfo.ConvertTimeFromUtc(utc, zone);
                return $"{local:ddd, dd MMM yyyy h:mm tt} ({timeZoneId})";
            }
            catch (TimeZoneNotFoundException)
            {
                return $"{utc:u} (UTC)";
            }
        }
    }
}
