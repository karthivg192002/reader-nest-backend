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
    /// <para>
    /// A demo with no <see cref="DemoBooking"/> linked to it at all is the one case that
    /// doesn't fit that shape: nobody was ever going to attend, so it's a misconfigured/orphaned
    /// slot rather than a genuine no-show, and <see cref="ISessionService.FlagOrphanedDemoSessionAsync"/>
    /// alerts an admin without touching status or payout — which means it never leaves
    /// Scheduled/CarriedForward on its own, so <see cref="ClassSession.OrphanedDemoAlertSentAtUtc"/>
    /// does the de-duplication instead, the same role <see cref="ClassSession.RecordingMissingAlertSentAtUtc"/>
    /// plays for <see cref="RecordingReconciliationBackgroundService"/>.
    /// </para>
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
                            && s.ScheduledStartAtUtc <= cutoff
                            // An orphaned demo (no DemoBooking at all) never changes status — see
                            // the else-branch below — so without this exclusion it would keep
                            // matching here and re-alert admins every cycle instead of once.
                            && s.OrphanedDemoAlertSentAtUtc == null)
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

                    if (session.BatchId.HasValue)
                    {
                        var studentPresent = await unitOfWork.Repository<SessionAttendance>().ExistsAsync(
                            a => a.ClassSessionId == session.Id && a.ChildId != null,
                            cancellationToken);
                        if (!studentPresent)
                        {
                            await sessionService.MarkNoShowSystemAsync(
                                session.Id, NoShowParty.Student,
                                $"Auto-detected: no student/parent joined within {gracePeriod.TotalMinutes:0} minutes of the scheduled start.",
                                cancellationToken);
                            studentNoShows++;
                        }
                    }
                    else
                    {
                        // A demo has no batch/attendance row to check — its only link to a student
                        // is DemoBooking. "No DemoBooking row at all" and "a DemoBooking exists but
                        // nobody joined" look identical from this query's shape alone, but they are
                        // not the same event: the first is a misconfigured/orphaned slot nobody was
                        // ever going to attend, not a genuine no-show. Conflating them used to flag
                        // it StudentNoShow anyway — wrongly docking the teacher a no-show-waiting
                        // payout and carrying the (still bookingless) slot forward, where it would
                        // just repeat the same false no-show every week (see MarkNoShowCoreAsync's
                        // carry-forward comment for the incident this caused).
                        var demoBooking = await unitOfWork.Repository<DemoBooking>().Query()
                            .Include(b => b.Participants)
                            .FirstOrDefaultAsync(b => b.ClassSessionId == session.Id, cancellationToken);
                        if (demoBooking is null)
                        {
                            await sessionService.FlagOrphanedDemoSessionAsync(session.Id, cancellationToken);
                        }
                        else if (demoBooking.ParentJoinedAtUtc is null && !demoBooking.Participants.Any(p => p.HasJoined))
                        {
                            await sessionService.MarkNoShowSystemAsync(
                                session.Id, NoShowParty.Student,
                                $"Auto-detected: no student/parent joined within {gracePeriod.TotalMinutes:0} minutes of the scheduled start.",
                                cancellationToken);
                            studentNoShows++;
                        }
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
