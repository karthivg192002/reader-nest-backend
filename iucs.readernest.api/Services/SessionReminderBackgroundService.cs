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
            var jitsiTokens = scope.ServiceProvider.GetRequiredService<IJitsiTokenService>();

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
            var demoBookingsBySessionId = new Dictionary<Guid, DemoBooking>();
            var parentUsersByBatchId = new Dictionary<Guid, List<User>>();
            if (upcoming.Count > 0)
            {
                jitsiConfigJson = await unitOfWork.Repository<Integration>().Query()
                    .Where(i => i.Key == "jitsi")
                    .Select(i => i.ConfigJson)
                    .FirstOrDefaultAsync(cancellationToken);

                // Both recipient lookups are resolved for the whole window up front. Done
                // inside the loop they cost one query per session, so a busy hour's reminder
                // fan-out scaled its round trips with the number of classes starting at once.
                var demoSessionIds = upcoming.Where(s => s.BatchId is null).Select(s => s.Id).ToList();
                if (demoSessionIds.Count > 0)
                {
                    demoBookingsBySessionId = await unitOfWork.Repository<DemoBooking>().Query()
                        .Where(b => b.ClassSessionId != null && demoSessionIds.Contains(b.ClassSessionId.Value))
                        .ToDictionaryAsync(b => b.ClassSessionId!.Value, cancellationToken);
                }

                var batchIds = upcoming.Where(s => s.BatchId is not null)
                    .Select(s => s.BatchId!.Value).Distinct().ToList();
                if (batchIds.Count > 0)
                {
                    var enrolments = await unitOfWork.Repository<BatchEnrollment>().Query()
                        .Where(e => batchIds.Contains(e.BatchId) && e.Status == EnrollmentStatus.Active)
                        .Select(e => new { e.BatchId, User = e.Child.ParentProfile.User })
                        .ToListAsync(cancellationToken);
                    parentUsersByBatchId = enrolments
                        .GroupBy(e => e.BatchId)
                        .ToDictionary(
                            g => g.Key,
                            g => g.Select(e => e.User).DistinctBy(u => u.Id).ToList());
                }
            }

            var failedReminderCount = 0;
            foreach (var session in upcoming)
            {
                // The reminder window is stateless de-duplication: each session falls into
                // exactly one 10-minute window, and the next cycle has moved past it. So an
                // exception here does not just delay the remaining sessions' reminders, it
                // loses them permanently — hence per-session isolation rather than letting
                // one bad recipient abort the batch.
                try
                {
                    await SendRemindersForSessionAsync(
                        session, jitsiConfigJson, demoBookingsBySessionId, parentUsersByBatchId,
                        notifications, emailTemplates, emailSender, jitsiTokens, cancellationToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    failedReminderCount++;
                    _logger.LogWarning(
                        ex, "Session reminder failed for session {SessionId}; continuing with the rest of the window.",
                        session.Id);
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
                        try
                        {
                            await notifications.SendTemplatedEmailAsync(
                                admin.Id, admin.Email, NotificationType.DelayedSessionAlert,
                                "delayed-session-alert",
                                new Dictionary<string, string>
                                {
                                    ["StartLocal"] = FormatLocal(session.ScheduledStartAtUtc, admin.TimeZoneId),
                                    ["TeacherName"] = $"{session.TeacherProfile.User.FirstName} {session.TeacherProfile.User.LastName}".Trim(),
                                },
                                cancellationToken);
                        }
                        catch (Exception ex) when (ex is not OperationCanceledException)
                        {
                            _logger.LogWarning(
                                ex, "Delayed-session alert failed for session {SessionId} to {AdminId}; continuing.",
                                session.Id, admin.Id);
                        }
                    }
                }
            }

            if (upcoming.Count > 0 || delayed.Count > 0)
            {
                _logger.LogInformation(
                    "Reminders: {ReminderCount} session reminder group(s) ({FailedCount} failed), {DelayedCount} delayed alert(s).",
                    upcoming.Count, failedReminderCount, delayed.Count);
            }
        }

        private static async Task SendRemindersForSessionAsync(
            ClassSession session,
            string? jitsiConfigJson,
            Dictionary<Guid, DemoBooking> demoBookingsBySessionId,
            Dictionary<Guid, List<User>> parentUsersByBatchId,
            INotificationService notifications,
            IEmailTemplateService emailTemplates,
            IEmailSender emailSender,
            IJitsiTokenService jitsiTokens,
            CancellationToken cancellationToken)
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

            var domain = JitsiLinkBuilder.ResolveDomain(jitsiConfigJson);
            // Each recipient gets their own token, scoped to this room and expiring a
            // couple of hours past the class — never a bare, forever-reusable room name.
            string JoinUrlFor(string participantName, string? participantEmail) =>
                JitsiLinkBuilder.BuildJoinUrl(
                    session.MeetingRoomId,
                    jitsiConfigJson,
                    jitsiTokens.CreateToken(
                        domain, jitsiConfigJson, session.MeetingRoomId!, participantName, participantEmail,
                        moderator: false, session.ScheduledEndAtUtc.AddHours(2)),
                    participantName)
                ?? "#";

            if (session.BatchId is null)
            {
                // Demo sessions have no batch — the lead is tracked via DemoBooking.ParentEmail,
                // which may not correspond to a real account yet, so this bypasses the
                // user-bound notification log the same way the initial confirmation email does.
                demoBookingsBySessionId.TryGetValue(session.Id, out var demoBooking);
                if (demoBooking is not null)
                {
                    var (subject, body) = await emailTemplates.RenderAsync(
                        "session-reminder-parent",
                        new Dictionary<string, string>
                        {
                            ["StartLocal"] = FormatLocal(session.ScheduledStartAtUtc, "Asia/Kolkata"),
                            ["JoinUrl"] = JoinUrlFor(demoBooking.ParentName, demoBooking.ParentEmail),
                        },
                        cancellationToken);
                    await emailSender.SendAsync(demoBooking.ParentEmail, subject, body, cancellationToken);
                }

                return;
            }

            if (!parentUsersByBatchId.TryGetValue(session.BatchId.Value, out var parentUsers))
            {
                return;
            }

            foreach (var parent in parentUsers)
            {
                await notifications.SendTemplatedEmailAsync(
                    parent.Id, parent.Email, NotificationType.SessionReminder,
                    "session-reminder-parent",
                    new Dictionary<string, string>
                    {
                        ["StartLocal"] = FormatLocal(session.ScheduledStartAtUtc, parent.TimeZoneId),
                        ["JoinUrl"] = JoinUrlFor($"{parent.FirstName} {parent.LastName}".Trim(), parent.Email),
                    },
                    cancellationToken);
            }
        }

        /// <summary>Multi-timezone support: renders a UTC instant in the recipient's own zone.</summary>
        private static string FormatLocal(DateTime utc, string timeZoneId) => DateTimeDisplay.ToLocal(utc, timeZoneId);
    }
}
