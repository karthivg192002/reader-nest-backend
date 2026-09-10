using iucs.readernest.application.Helper;
using iucs.readernest.application.Services;
using iucs.readernest.domain.Entities.Sessions;
using iucs.readernest.domain.Entities.Users;
using iucs.readernest.domain.Enums;
using iucs.readernest.domain.Repository;
using Microsoft.EntityFrameworkCore;

namespace iucs.readernest.api.Services
{
    /// <summary>
    /// Catches a gap nothing else does: auto-recording can succeed at *starting* with no error
    /// (the teacher sees the REC indicator, JitsiLive's own failed-to-start warning never fires)
    /// and still never produce a recording — Jibri crashing mid-capture, an upload failure, the
    /// finalize webhook never reaching this app. Confirmed as a real, silent gap: a parent found
    /// no recording for a class with nothing anywhere having flagged it. Runs every 30 minutes;
    /// each cycle looks at Completed sessions whose scheduled end is 1-48 hours in the past
    /// (past normal Jibri upload/finalize latency, but not so old the alert is useless), have no
    /// registered SessionRecording, and haven't already been alerted on — one alert per session,
    /// ever, via RecordingMissingAlertSentAtUtc.
    /// </summary>
    public class RecordingReconciliationBackgroundService : BackgroundService
    {
        private static readonly TimeSpan Interval = TimeSpan.FromMinutes(30);
        private static readonly TimeSpan MinAge = TimeSpan.FromHours(1);
        private static readonly TimeSpan MaxAge = TimeSpan.FromHours(48);

        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<RecordingReconciliationBackgroundService> _logger;

        public RecordingReconciliationBackgroundService(
            IServiceScopeFactory scopeFactory,
            ILogger<RecordingReconciliationBackgroundService> logger)
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
                    _logger.LogError(ex, "Recording reconciliation cycle failed; retrying next interval.");
                }

                await Task.Delay(Interval, stoppingToken);
            }
        }

        private async Task RunCycleAsync(CancellationToken cancellationToken)
        {
            using var scope = _scopeFactory.CreateScope();
            var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            var sessionService = scope.ServiceProvider.GetRequiredService<ISessionService>();
            var notificationService = scope.ServiceProvider.GetRequiredService<INotificationService>();

            // Best-effort proxy for "a recording was actually expected": if the deployment
            // currently has auto-record off, most Completed sessions genuinely have no
            // recording by design, and flagging every one of them would bury the real gaps in
            // noise. Not perfect (the setting could have changed since a given class ran), but
            // matches this app's own working assumption everywhere else auto-record is read.
            var settings = await sessionService.GetClassroomSettingsAsync(cancellationToken);
            if (!settings.AutoRecordEnabled)
            {
                return;
            }

            var now = DateTime.UtcNow;
            var windowEnd = now.Subtract(MinAge);
            var windowStart = now.Subtract(MaxAge);
            var candidates = await unitOfWork.Repository<ClassSession>().Query()
                .Where(s => s.Status == SessionStatus.Completed
                            && s.RecordingMissingAlertSentAtUtc == null
                            && s.ScheduledEndAtUtc >= windowStart
                            && s.ScheduledEndAtUtc <= windowEnd)
                .ToListAsync(cancellationToken);

            if (candidates.Count == 0)
            {
                return;
            }

            var flagged = 0;
            foreach (var session in candidates)
            {
                try
                {
                    var hasRecording = await unitOfWork.Repository<SessionRecording>()
                        .ExistsAsync(r => r.ClassSessionId == session.Id, cancellationToken);
                    if (hasRecording)
                    {
                        continue;
                    }

                    var teacher = await unitOfWork.Repository<TeacherProfile>().Query()
                        .Include(t => t.User)
                        .FirstOrDefaultAsync(t => t.Id == session.TeacherProfileId, cancellationToken);
                    var admins = await unitOfWork.Repository<User>().Query()
                        .Where(u => u.Role == UserRole.Admin && u.Status == UserStatus.Active)
                        .ToListAsync(cancellationToken);

                    foreach (var admin in admins)
                    {
                        await notificationService.SendTemplatedEmailAsync(
                            admin.Id,
                            admin.Email,
                            NotificationType.NoShowAlert,
                            "recording-missing-alert",
                            new Dictionary<string, string>
                            {
                                ["TeacherName"] = teacher?.User is { } u ? $"{u.FirstName} {u.LastName}".Trim() : "Unknown teacher",
                                ["StartAtLocal"] = DateTimeDisplay.ToLocal(session.ScheduledStartAtUtc, admin.TimeZoneId),
                            },
                            cancellationToken);
                    }

                    // Tracked directly on the entity (not the tracked-repository/SaveChanges
                    // batch below) via ExecuteUpdateAsync — a set-based write that never attaches
                    // the entity to this scope's change tracker, matching this codebase's own
                    // established pattern (see EnrollmentService.RemoveChildAsync) for avoiding a
                    // stray tracked reference outliving this one write.
                    await unitOfWork.Repository<ClassSession>().Query()
                        .Where(s => s.Id == session.Id)
                        .ExecuteUpdateAsync(s => s.SetProperty(x => x.RecordingMissingAlertSentAtUtc, now), cancellationToken);
                    flagged++;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogWarning(ex, "Recording reconciliation check failed for session {SessionId}; continuing with the rest of the cycle.", session.Id);
                }
            }

            if (flagged > 0)
            {
                _logger.LogInformation(
                    "Recording reconciliation: {Flagged} completed session(s) had no recording, out of {Candidates} checked.",
                    flagged, candidates.Count);
            }
        }
    }
}
