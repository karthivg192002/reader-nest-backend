using iucs.readernest.application.Common;
using iucs.readernest.application.Dto.Sessions;
using iucs.readernest.application.Services;
using iucs.readernest.domain.Entities.Admission;
using iucs.readernest.domain.Entities.Sessions;
using iucs.readernest.domain.Enums;
using iucs.readernest.domain.Repository;
using Microsoft.EntityFrameworkCore;

namespace iucs.readernest.api.Services
{
    /// <summary>
    /// Automatic counterpart to the manual "Mark No-Show" action (SessionsController /
    /// admin Sessions screen): the WBS's "No-Show Handling" (Teacher/Student no-show
    /// capture) is meant to sit alongside "Attendance Automation" as a system-driven
    /// step, not require an admin to notice and click a button. Runs every 10 minutes;
    /// each cycle looks at every still-<see cref="SessionStatus.Scheduled"/> or
    /// <see cref="SessionStatus.CarriedForward"/> session whose scheduled start is more
    /// than the configured grace period (Settings → Payroll, <see cref="PayrollSettings.GetNoShowGraceAsync"/>)
    /// in the past and — going only by who has actually
    /// been captured present (join-based <see cref="SessionAttendance"/> for a regular
    /// class, <see cref="DemoBooking.ParentJoinedAtUtc"/>/<see cref="DemoParticipant.HasJoined"/>
    /// for a demo) — flags whichever side never showed via
    /// <see cref="ISessionService.MarkNoShowSystemAsync"/>, which carries the class
    /// forward and accrues the same payout impact a human marking it by hand would.
    /// Self-limiting: marking a session moves it out of Scheduled/CarriedForward, so
    /// there is no separate de-duplication window like <see cref="SessionReminderBackgroundService"/>
    /// needs — a session simply stops matching the query once it's been handled, and a
    /// missed cycle (a crash, a slow run) is safely picked up by the next one instead of
    /// being lost.
    /// </summary>
    public class NoShowDetectionBackgroundService : BackgroundService
    {
        private static readonly TimeSpan Interval = TimeSpan.FromMinutes(10);

        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<NoShowDetectionBackgroundService> _logger;

        public NoShowDetectionBackgroundService(
            IServiceScopeFactory scopeFactory,
            ILogger<NoShowDetectionBackgroundService> logger)
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
                    _logger.LogError(ex, "No-show detection cycle failed; retrying next interval.");
                }

                await Task.Delay(Interval, stoppingToken);
            }
        }

        private async Task RunCycleAsync(CancellationToken cancellationToken)
        {
            using var scope = _scopeFactory.CreateScope();
            var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            var sessionService = scope.ServiceProvider.GetRequiredService<ISessionService>();

            // Deliberately more generous by default than SessionReminderBackgroundService's
            // "delayed session" alert (fired ~10-20 minutes after start): that alert already
            // gives a human the chance to step in on a merely-late teacher/student before this
            // job ever treats the gap as a genuine no-show and triggers the payout/carry-forward
            // side effects. Admin-configurable (Settings → Payroll) rather than fixed in code.
            var gracePeriod = await PayrollSettings.GetNoShowGraceAsync(unitOfWork, cancellationToken);
            var cutoff = DateTime.UtcNow.Subtract(gracePeriod);
            var candidates = await unitOfWork.Repository<ClassSession>().Query()
                .Where(s => (s.Status == SessionStatus.Scheduled || s.Status == SessionStatus.CarriedForward)
                            && s.ScheduledStartAtUtc <= cutoff)
                .ToListAsync(cancellationToken);

            if (candidates.Count == 0)
            {
                return;
            }

            var teacherNoShows = 0;
            var studentNoShows = 0;
            foreach (var session in candidates)
            {
                // Isolated per session: one bad record (e.g. a race with a human marking the
                // same session by hand between the query above and this call) must not stop
                // the rest of the cycle's genuinely overdue sessions from being processed.
                try
                {
                    var teacherPresent = await unitOfWork.Repository<SessionAttendance>().ExistsAsync(
                        a => a.ClassSessionId == session.Id && a.TeacherProfileId == session.TeacherProfileId,
                        cancellationToken);
                    if (!teacherPresent)
                    {
                        await sessionService.MarkNoShowSystemAsync(
                            session.Id, NoShowParty.Teacher,
                            $"Auto-detected: teacher never joined within {gracePeriod.TotalMinutes:0} minutes of the scheduled start.",
                            cancellationToken);
                        teacherNoShows++;
                        continue;
                    }

                    var studentPresent = session.BatchId.HasValue
                        ? await unitOfWork.Repository<SessionAttendance>().ExistsAsync(
                            a => a.ClassSessionId == session.Id && a.ChildId != null,
                            cancellationToken)
                        : await unitOfWork.Repository<DemoBooking>().ExistsAsync(
                            b => b.ClassSessionId == session.Id
                                && (b.ParentJoinedAtUtc != null || b.Participants.Any(p => p.HasJoined)),
                            cancellationToken);
                    if (!studentPresent)
                    {
                        await sessionService.MarkNoShowSystemAsync(
                            session.Id, NoShowParty.Student,
                            $"Auto-detected: no student/parent joined within {gracePeriod.TotalMinutes:0} minutes of the scheduled start.",
                            cancellationToken);
                        studentNoShows++;
                    }

                    // Both sides present: leave it running, this job has nothing to do here —
                    // the teacher completes it manually from the live classroom as normal.
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogWarning(ex, "Auto no-show check failed for session {SessionId}; continuing with the rest of the cycle.", session.Id);
                }
            }

            if (teacherNoShows > 0 || studentNoShows > 0)
            {
                _logger.LogInformation(
                    "Auto no-show: {TeacherNoShows} teacher, {StudentNoShows} student, out of {Candidates} overdue session(s) checked.",
                    teacherNoShows, studentNoShows, candidates.Count);
            }
        }
    }
}
