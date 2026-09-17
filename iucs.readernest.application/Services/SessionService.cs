using iucs.readernest.application.Common;
using iucs.readernest.application.Common.Exceptions;
using iucs.readernest.application.Common.Interfaces;
using iucs.readernest.application.Dto.Common;
using iucs.readernest.application.Dto.Sessions;
using iucs.readernest.application.Helper;
using iucs.readernest.application.Mappings;
using iucs.readernest.domain.Common;
using iucs.readernest.domain.Entities.Academics;
using iucs.readernest.domain.Entities.Admission;
using iucs.readernest.domain.Entities.Billing;
using iucs.readernest.domain.Entities.Integrations;
using iucs.readernest.domain.Entities.Sessions;
using iucs.readernest.domain.Entities.Users;
using iucs.readernest.domain.Enums;
using iucs.readernest.domain.Repository;
using Microsoft.EntityFrameworkCore;

namespace iucs.readernest.application.Services
{
    public class SessionService : ISessionService
    {
        /// <summary>How many times an unresolved no-show is allowed to silently reschedule
        /// itself one week later before the chain stops and a human gets asked to look at it
        /// instead — see MarkNoShowCoreAsync's own comment for the incident that motivated this.</summary>
        private const int MaxAutoCarryForwards = 3;

        private static readonly SessionStatus[] TerminalStatuses =
        [
            SessionStatus.Completed,
            SessionStatus.Cancelled,
            SessionStatus.Rescheduled,
            SessionStatus.TeacherNoShow,
            SessionStatus.StudentNoShow,
        ];

        private readonly IUnitOfWork _unitOfWork;
        private readonly IAuditLogService _auditLog;
        private readonly IPayoutService _payoutService;
        private readonly INotificationService _notificationService;
        private readonly ICurrentUserService _currentUser;
        private readonly IJitsiTokenService _jitsiTokenService;
        private readonly IClassSessionEventLogService _eventLog;
        private readonly ITokenService _tokenService;

        public SessionService(
            IUnitOfWork unitOfWork,
            IAuditLogService auditLog,
            IPayoutService payoutService,
            INotificationService notificationService,
            ICurrentUserService currentUser,
            IJitsiTokenService jitsiTokenService,
            IClassSessionEventLogService eventLog,
            ITokenService tokenService)
        {
            _unitOfWork = unitOfWork;
            _auditLog = auditLog;
            _payoutService = payoutService;
            _notificationService = notificationService;
            _currentUser = currentUser;
            _jitsiTokenService = jitsiTokenService;
            _eventLog = eventLog;
            _tokenService = tokenService;
        }

        public async Task<IReadOnlyList<ClassSessionDto>> ListAsync(
            DateTime fromUtc,
            DateTime toUtc,
            Guid? teacherProfileId,
            Guid? batchId,
            CancellationToken cancellationToken = default)
        {
            var query = BaseQuery()
                .Where(s => s.ScheduledStartAtUtc < toUtc && s.ScheduledEndAtUtc > fromUtc);

            if (teacherProfileId.HasValue)
            {
                query = query.Where(s => s.TeacherProfileId == teacherProfileId.Value);
            }

            if (batchId.HasValue)
            {
                query = query.Where(s => s.BatchId == batchId.Value);
            }

            var sessions = await query.OrderBy(s => s.ScheduledStartAtUtc).ToListAsync(cancellationToken);
            var activeRecordings = await SessionRecordingLookup.ActiveRecordingsBySessionAsync(
                _unitOfWork, sessions.Select(s => s.Id), cancellationToken);
            return sessions
                .Select(s => s.ToDto(activeRecordings.GetValueOrDefault(s.Id), activeRecordings.ContainsKey(s.Id)))
                .ToList();
        }

        public async Task<IReadOnlyList<ClassSessionDto>> ListForTeacherUserAsync(
            Guid userId,
            DateTime fromUtc,
            DateTime toUtc,
            CancellationToken cancellationToken = default)
        {
            var teacher = await _unitOfWork.Repository<TeacherProfile>()
                .FirstOrDefaultAsync(t => t.UserId == userId, cancellationToken)
                ?? throw new NotFoundException("No teacher profile is linked to the current account.");

            return await ListAsync(fromUtc, toUtc, teacher.Id, null, cancellationToken);
        }

        public async Task<ClassSessionDto> GetAsync(Guid id, CancellationToken cancellationToken = default)
        {
            var session = await BaseQuery().FirstOrDefaultAsync(s => s.Id == id, cancellationToken)
                ?? throw new NotFoundException(nameof(ClassSession), id);

            var activeRecordings = await SessionRecordingLookup.ActiveRecordingsBySessionAsync(_unitOfWork, [id], cancellationToken);
            return session.ToDto(activeRecordings.GetValueOrDefault(id), activeRecordings.ContainsKey(id));
        }

        public async Task<ClassSessionDto> ScheduleAsync(ScheduleSessionRequest request, CancellationToken cancellationToken = default)
        {
            ValidateWindow(request.ScheduledStartAtUtc, request.ScheduledEndAtUtc);
            await EnsureNotHolidayAsync(request.ScheduledStartAtUtc, cancellationToken);

            if (request.Type == SessionType.Regular && request.BatchId is null)
            {
                throw new DomainValidationException("A regular session must belong to a batch.");
            }

            if (request.BatchId.HasValue)
            {
                var batchExists = await _unitOfWork.Repository<Batch>()
                    .ExistsAsync(b => b.Id == request.BatchId.Value, cancellationToken);
                if (!batchExists)
                {
                    throw new NotFoundException(nameof(Batch), request.BatchId.Value);
                }
            }

            var teacherExists = await _unitOfWork.Repository<TeacherProfile>()
                .ExistsAsync(t => t.Id == request.TeacherProfileId, cancellationToken);
            if (!teacherExists)
            {
                throw new NotFoundException(nameof(TeacherProfile), request.TeacherProfileId);
            }

            await EnsureTeacherIsFreeAsync(
                request.TeacherProfileId, request.ScheduledStartAtUtc, request.ScheduledEndAtUtc, cancellationToken);

            // Confirmed live via a client screen recording: a Demo scheduled from this generic
            // Sessions-page dialog (as opposed to Admission's dedicated Demo Booking flow, which
            // already does this — see DemoBookingService.EnsureTeacherMeetingRoomAsync) got a
            // brand-new random room here, unrelated to the teacher's own fixed personal meeting
            // room. The client has since been trained to always share that one stable personal
            // link for a demo (WBS "One teacher -> one fixed demo link"), so whenever a demo was
            // instead scheduled from here, the teacher joined the (correct, freshly-generated)
            // session room while the parent — handed the teacher's personal link, the only link
            // this dialog ever gave anyone a reason to share — landed in a completely different,
            // empty room and sat on Jitsi's own "waiting for a moderator" screen forever, with no
            // knock/notification ever reaching the teacher (she was never in that room to see one).
            // Routing every Demo through the same fixed personal room this teacher already shares
            // for every other demo means whichever screen scheduled it, the link she hands out
            // always points at wherever she's actually going to be.
            var meetingRoomId = request.Type == SessionType.Demo
                ? (await EnsureTeacherMeetingRoomAsync(request.TeacherProfileId, cancellationToken)).User.PersonalMeetingRoomId!
                : $"trn-{Guid.NewGuid():N}";

            var session = new ClassSession
            {
                BatchId = request.BatchId,
                TeacherProfileId = request.TeacherProfileId,
                Type = request.Type,
                ScheduledStartAtUtc = request.ScheduledStartAtUtc,
                ScheduledEndAtUtc = request.ScheduledEndAtUtc,
                MeetingRoomId = meetingRoomId,
            };
            await _unitOfWork.Repository<ClassSession>().AddAsync(session, cancellationToken);
            await _auditLog.StageAsync(AuditAction.Create, nameof(ClassSession), session.Id.ToString(), cancellationToken: cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            await SendBookingConfirmationAsync(session, cancellationToken);

            return await GetAsync(session.Id, cancellationToken);
        }

        public async Task<ClassSessionDto> RescheduleAsync(
            Guid id,
            RescheduleSessionRequest request,
            CancellationToken cancellationToken = default)
        {
            ValidateWindow(request.ScheduledStartAtUtc, request.ScheduledEndAtUtc);
            // Same rule ScheduleAsync enforces — a reschedule is a new calendar entry too,
            // and was previously the one path that could land a class on a holiday.
            await EnsureNotHolidayAsync(request.ScheduledStartAtUtc, cancellationToken);

            var original = await _unitOfWork.Repository<ClassSession>().GetByIdAsync(id, cancellationToken)
                ?? throw new NotFoundException(nameof(ClassSession), id);

            if (TerminalStatuses.Contains(original.Status))
            {
                throw new DomainValidationException($"A session in status '{original.Status}' cannot be rescheduled.");
            }

            // "Edit session" (WBS Round 2 feedback): a reschedule was time-only before — moving
            // a session to a different teacher or batch meant cancelling and rebooking from
            // scratch, losing the link to the original. Both are optional overrides on top of
            // the same reschedule flow rather than a separate action, since this already creates
            // a fresh linked calendar entry either way (see the comment below).
            var newTeacherId = request.TeacherProfileId ?? original.TeacherProfileId;
            var newBatchId = request.BatchId ?? original.BatchId;

            if (original.Type == SessionType.Regular && newBatchId is null)
            {
                throw new DomainValidationException("A regular session must belong to a batch.");
            }

            if (newBatchId.HasValue && newBatchId != original.BatchId)
            {
                var batchExists = await _unitOfWork.Repository<Batch>()
                    .ExistsAsync(b => b.Id == newBatchId.Value, cancellationToken);
                if (!batchExists)
                {
                    throw new NotFoundException(nameof(Batch), newBatchId.Value);
                }
            }

            if (newTeacherId != original.TeacherProfileId)
            {
                var teacherExists = await _unitOfWork.Repository<TeacherProfile>()
                    .ExistsAsync(t => t.Id == newTeacherId, cancellationToken);
                if (!teacherExists)
                {
                    throw new NotFoundException(nameof(TeacherProfile), newTeacherId);
                }
            }

            // Checked against whichever teacher the session will actually end up with —
            // the original teacher's own free/busy slot is irrelevant once they're being
            // swapped out.
            await EnsureTeacherIsFreeAsync(
                newTeacherId, request.ScheduledStartAtUtc, request.ScheduledEndAtUtc,
                cancellationToken, excludeSessionId: original.Id);

            original.Status = SessionStatus.Rescheduled;

            // A Demo's room is the assigned teacher's own fixed personal room (see ScheduleAsync
            // and DemoBookingService's own reassignment path), so it has to follow a teacher swap
            // here too — otherwise the parent's already-shared link keeps pointing at the OLD
            // teacher's room while the new teacher joins from her own, and nobody ends up in the
            // same place. A Regular session's room has no such meaning (just a one-off generated
            // id), so it always just carries over unchanged.
            var meetingRoomId = original.Type == SessionType.Demo && newTeacherId != original.TeacherProfileId
                ? (await EnsureTeacherMeetingRoomAsync(newTeacherId, cancellationToken)).User.PersonalMeetingRoomId!
                : original.MeetingRoomId;

            // A reschedule is a new calendar entry linked to the original,
            // so history and colour coding stay traceable.
            var replacement = new ClassSession
            {
                BatchId = newBatchId,
                TeacherProfileId = newTeacherId,
                Type = original.Type,
                ScheduledStartAtUtc = request.ScheduledStartAtUtc,
                ScheduledEndAtUtc = request.ScheduledEndAtUtc,
                MeetingRoomId = meetingRoomId,
                RescheduledFromSessionId = original.Id,
            };
            await _unitOfWork.Repository<ClassSession>().AddAsync(replacement, cancellationToken);

            // Confirmed live: a Demo rescheduled from this generic Sessions-page dialog (as
            // opposed to Admission's own Demo Booking reschedule, which already keeps this in
            // sync) left the DemoBooking's ClassSessionId pointing at `original` — a row that's
            // now terminal (Status.Rescheduled) and never shown as joinable again. The parent
            // side of IsSessionParticipantAsync (and ParentPortalService.GetScheduleAsync's own
            // demo-session filter) both key off THIS field, not the session's own
            // RescheduledFromSessionId chain, so the parent could only ever see/join the stale
            // original — while the teacher, matched directly via TeacherProfileId, correctly
            // joined the new `replacement`. Both landed in the same Jitsi room (MeetingRoomId
            // carries over) so the call itself looked fine, but two different session ids meant
            // two different ClassroomHub groups: neither side's roster or whiteboard ever synced
            // with the other. Following the booking to the new row fixes both at once.
            if (original.Type == SessionType.Demo)
            {
                var demoBooking = await _unitOfWork.Repository<DemoBooking>().TrackedQuery()
                    .FirstOrDefaultAsync(b => b.ClassSessionId == original.Id, cancellationToken);
                if (demoBooking is not null)
                {
                    demoBooking.ClassSessionId = replacement.Id;
                }
            }

            await _auditLog.StageAsync(AuditAction.Update, nameof(ClassSession), original.Id.ToString(),
                changesJson: $"{{\"rescheduledTo\":\"{replacement.Id}\"}}", cancellationToken: cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return await GetAsync(replacement.Id, cancellationToken);
        }

        public async Task<ClassSessionDto> CancelAsync(
            Guid id,
            CancelSessionRequest request,
            CancellationToken cancellationToken = default)
        {
            var session = await _unitOfWork.Repository<ClassSession>().GetByIdAsync(id, cancellationToken)
                ?? throw new NotFoundException(nameof(ClassSession), id);

            if (TerminalStatuses.Contains(session.Status))
            {
                throw new DomainValidationException($"A session in status '{session.Status}' cannot be cancelled.");
            }

            session.Status = SessionStatus.Cancelled;
            session.CancellationReason = request.Reason;

            await _auditLog.StageAsync(AuditAction.Update, nameof(ClassSession), session.Id.ToString(), cancellationToken: cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return await GetAsync(session.Id, cancellationToken);
        }

        public async Task<ClassSessionDto> CompleteAsync(
            Guid id,
            CompleteSessionRequest? request = null,
            CancellationToken cancellationToken = default)
        {
            var session = await _unitOfWork.Repository<ClassSession>().GetByIdAsync(id, cancellationToken)
                ?? throw new NotFoundException(nameof(ClassSession), id);

            // Completing a class accrues that session's teacher payout, so being *a* teacher
            // is not enough — the caller must be this session's own teacher (or an Admin).
            await EnsureSessionParticipantAsync(session, cancellationToken);

            if (TerminalStatuses.Contains(session.Status))
            {
                throw new DomainValidationException($"A session in status '{session.Status}' cannot be completed.");
            }

            session.Status = SessionStatus.Completed;
            session.ActualStartAtUtc ??= session.ScheduledStartAtUtc;
            session.ActualEndAtUtc ??= DateTime.UtcNow;
            if (!string.IsNullOrWhiteSpace(request?.Summary))
            {
                session.Summary = request.Summary.Trim();
            }
            else
            {
                // PDF's "Session Summary Generated" (p.19) is an unconditional step — a teacher
                // who completes a class without typing notes still gets a real summary, built
                // from the same engagement data GetEngagementSummaryAsync already computes.
                var engagement = await GetEngagementSummaryAsync(session.Id, cancellationToken);
                session.Summary = BuildAutoSummary(engagement);
            }

            if (session.BatchId.HasValue)
            {
                await MoveBatchToDormantIfCourseCompletedAsync(session, cancellationToken);
            }

            // Auto payout calculation post-class: the earning accrues in the same unit of work
            await _payoutService.AccrueForSessionAsync(
                session, PayoutItemType.SessionEarning,
                session.Type == SessionType.Demo ? "Demo session" : null,
                cancellationToken);

            await _auditLog.StageAsync(AuditAction.Update, nameof(ClassSession), session.Id.ToString(), cancellationToken: cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            // The definitive "End Class" timestamp for the Class Session Logs screen — best
            // effort, after the real completion has already durably saved.
            await _eventLog.LogClassEndedAsync(session, cancellationToken);

            // Performance summary: the teacher's class notes go straight to the batch's parents
            if (!string.IsNullOrWhiteSpace(session.Summary) && session.BatchId.HasValue)
            {
                await SendSummaryToParentsAsync(session, cancellationToken);
            }

            return await GetAsync(session.Id, cancellationToken);
        }

        /// <summary>Teacher feedback: "the report-writing option is also not visible after the
        /// session if we do not complete the report immediately at the end of the class." A
        /// completed session's Summary could only ever be set once, at CompleteAsync time -- a
        /// teacher who skipped it there (or only got the auto-generated engagement-stats
        /// fallback) had no way back in. This is the way back in: same session-participant
        /// ownership check as CompleteAsync, but only touches Summary, and re-emails it to the
        /// batch's parents the same way a same-time summary already does -- a class's real notes
        /// showing up a day late is still far more useful to a parent than never.</summary>
        public async Task<ClassSessionDto> UpdateSummaryAsync(
            Guid id,
            string summary,
            CancellationToken cancellationToken = default)
        {
            var session = await _unitOfWork.Repository<ClassSession>().TrackedQuery()
                .FirstOrDefaultAsync(s => s.Id == id, cancellationToken)
                ?? throw new NotFoundException(nameof(ClassSession), id);

            await EnsureSessionParticipantAsync(session, cancellationToken);

            if (session.Status != SessionStatus.Completed)
            {
                throw new DomainValidationException("Only a completed session's notes can be edited this way — use Complete Class to end and record notes for an in-progress one.");
            }

            session.Summary = summary.Trim();
            await _auditLog.StageAsync(AuditAction.Update, nameof(ClassSession), session.Id.ToString(), cancellationToken: cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            if (session.BatchId.HasValue)
            {
                await SendSummaryToParentsAsync(session, cancellationToken);
            }

            return await GetAsync(session.Id, cancellationToken);
        }

        private async Task SendSummaryToParentsAsync(ClassSession session, CancellationToken cancellationToken)
        {
            var parents = await _unitOfWork.Repository<BatchEnrollment>().Query()
                .Where(e => e.BatchId == session.BatchId && e.Status == EnrollmentStatus.Active)
                .Select(e => new
                {
                    ChildName = e.Child.FirstName,
                    ParentUserId = e.Child.ParentProfile.User.Id,
                    ParentEmail = e.Child.ParentProfile.User.Email,
                })
                .ToListAsync(cancellationToken);

            foreach (var parent in parents)
            {
                await _notificationService.SendTemplatedEmailAsync(
                    parent.ParentUserId,
                    parent.ParentEmail,
                    NotificationType.PerformanceSummary,
                    "class-summary",
                    new Dictionary<string, string>
                    {
                        ["ChildName"] = parent.ChildName,
                        ["SessionDate"] = DateTimeDisplay.ToLocalDate(session.ScheduledStartAtUtc),
                        ["Summary"] = session.Summary ?? string.Empty,
                    },
                    cancellationToken);
            }
        }

        public async Task<ClassSessionDto> MarkNoShowAsync(
            Guid id,
            MarkNoShowRequest request,
            CancellationToken cancellationToken = default)
        {
            var session = await _unitOfWork.Repository<ClassSession>().GetByIdAsync(id, cancellationToken)
                ?? throw new NotFoundException(nameof(ClassSession), id);

            // A teacher no-show is a payout deduction against this session's teacher — any
            // teacher being able to file one on someone else's class is a direct financial
            // attack on a colleague, so the caller must own this session (or be an Admin).
            await EnsureSessionParticipantAsync(session, cancellationToken);

            return await MarkNoShowCoreAsync(session, request.Party, request.Note, cancellationToken);
        }

        /// <summary>
        /// System-initiated equivalent of <see cref="MarkNoShowAsync"/> — identical carry-forward
        /// and payout behaviour, but skips <see cref="EnsureSessionParticipantAsync"/> since there
        /// is no signed-in caller to check: this exists solely for
        /// <c>NoShowDetectionBackgroundService</c>, which flags a session once its grace period
        /// has elapsed with one side never having joined. Not exposed on any controller — nothing
        /// but the background job may call this, or any authenticated user could no-show any
        /// class and trigger its payout/carry-forward side effects for free.
        /// </summary>
        public async Task<ClassSessionDto> MarkNoShowSystemAsync(
            Guid id,
            NoShowParty party,
            string note,
            CancellationToken cancellationToken = default)
        {
            var session = await _unitOfWork.Repository<ClassSession>().GetByIdAsync(id, cancellationToken)
                ?? throw new NotFoundException(nameof(ClassSession), id);

            return await MarkNoShowCoreAsync(session, party, note, cancellationToken);
        }

        public async Task FlagOrphanedDemoSessionAsync(Guid id, CancellationToken cancellationToken = default)
        {
            var session = await _unitOfWork.Repository<ClassSession>().GetByIdAsync(id, cancellationToken)
                ?? throw new NotFoundException(nameof(ClassSession), id);

            var admins = await _unitOfWork.Repository<User>().Query()
                .Where(u => u.Role == UserRole.Admin && u.Status == UserStatus.Active)
                .ToListAsync(cancellationToken);

            foreach (var admin in admins)
            {
                await _notificationService.SendTemplatedEmailAsync(
                    admin.Id,
                    admin.Email,
                    NotificationType.NoShowAlert,
                    "demo-orphaned-noshow-alert",
                    new Dictionary<string, string> { ["StartAtLocal"] = DateTimeDisplay.ToLocal(session.ScheduledStartAtUtc, admin.TimeZoneId) },
                    cancellationToken);
            }

            // De-duplicates like RecordingMissingAlertSentAtUtc does for the recording-gap alert:
            // this session's status never changes (nobody was ever booked into it, so there is no
            // no-show/carry-forward to apply), so without this it would keep matching the
            // background service's query and re-alert admins every 10-minute cycle forever.
            session.OrphanedDemoAlertSentAtUtc = DateTime.UtcNow;
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }

        private async Task<ClassSessionDto> MarkNoShowCoreAsync(
            ClassSession session,
            NoShowParty party,
            string? note,
            CancellationToken cancellationToken)
        {
            if (TerminalStatuses.Contains(session.Status))
            {
                throw new DomainValidationException($"A session in status '{session.Status}' cannot be marked as a no-show.");
            }

            session.Status = party == NoShowParty.Teacher
                ? SessionStatus.TeacherNoShow
                : SessionStatus.StudentNoShow;

            // Best-effort; covers both exit paths below (capped chain vs. normal carry-forward).
            await _eventLog.LogNoShowAsync(session, party, cancellationToken);

            if (party == NoShowParty.Student)
            {
                // Teacher waited for the student: the waiting amount still accrues
                await _payoutService.AccrueForSessionAsync(
                    session, PayoutItemType.StudentNoShowWaiting,
                    note ?? "Student no-show waiting amount", cancellationToken);
            }
            else
            {
                await _payoutService.AccrueForSessionAsync(
                    session, PayoutItemType.TeacherNoShowDeduction,
                    note ?? "Teacher no-show deduction", cancellationToken);
                await NotifyAdminsOfTeacherNoShowAsync(session, cancellationToken);
            }

            // An abandoned booking (a stale/orphaned lead, a batch nobody ever pulled off the
            // calendar) previously carried itself forward one week later, forever — confirmed
            // live: one stale demo booking auto-rescheduled itself as a fresh no-show every
            // single week for 9 straight weeks with nobody ever noticing, since each occurrence
            // looked like an unremarkable, isolated miss rather than part of a chain. Past this
            // cap, stop silently rescheduling and hand it to a human instead: the class stays
            // in its terminal no-show status (still fully visible/actionable from Sessions) and
            // admins get a one-time "this needs a decision" alert rather than another identical
            // weekly email indistinguishable from the previous eight.
            if (session.CarryForwardCount >= MaxAutoCarryForwards)
            {
                await NotifyAdminsOfStalledNoShowChainAsync(session, cancellationToken);
                await _auditLog.StageAsync(AuditAction.Update, nameof(ClassSession), session.Id.ToString(),
                    changesJson: "{\"noShow\":\"" + party + "\",\"carryForwardCapped\":true}",
                    cancellationToken: cancellationToken);
                await _unitOfWork.SaveChangesAsync(cancellationToken);
                return await GetAsync(session.Id, cancellationToken);
            }

            // The missed class is never lost: a carried-forward session is placed one week
            // later at the same slot, keeping the traceability link for calendar and payouts.
            // Unlike fresh scheduling, marking a no-show must never hard-fail, so this never
            // throws — but it does still honour the "no class on a holiday" rule (walking
            // forward a day at a time, bounded, to the next non-holiday date) and it checks
            // for a teacher-schedule collision so that risk is recorded rather than silently
            // invisible, even though it doesn't block the placement.
            var duration = session.ScheduledEndAtUtc - session.ScheduledStartAtUtc;
            var carriedForwardStart = await NextNonHolidayDateAsync(session.ScheduledStartAtUtc.AddDays(7), cancellationToken);
            var carriedForwardEnd = carriedForwardStart.Add(duration);
            var carriedForwardHasConflict = await _unitOfWork.Repository<ClassSession>().ExistsAsync(
                s => s.TeacherProfileId == session.TeacherProfileId
                    && (s.Status == SessionStatus.Scheduled || s.Status == SessionStatus.InProgress || s.Status == SessionStatus.CarriedForward)
                    && s.ScheduledStartAtUtc < carriedForwardEnd
                    && s.ScheduledEndAtUtc > carriedForwardStart,
                cancellationToken);

            var carriedForward = new ClassSession
            {
                BatchId = session.BatchId,
                TeacherProfileId = session.TeacherProfileId,
                Type = session.Type,
                Status = SessionStatus.CarriedForward,
                ScheduledStartAtUtc = carriedForwardStart,
                ScheduledEndAtUtc = carriedForwardEnd,
                MeetingRoomId = session.MeetingRoomId,
                CarriedForwardFromSessionId = session.Id,
                CarryForwardCount = session.CarryForwardCount + 1,
            };
            await _unitOfWork.Repository<ClassSession>().AddAsync(carriedForward, cancellationToken);

            // A demo has no batch to fall back on — its only link to a student is the
            // DemoBooking row, and that row still points at the now-terminal original session
            // unless it's moved here. Miss this and the carried-forward slot looks exactly like
            // the original (Teacher set, Type Demo) but with "No students assigned": nobody was
            // ever going to join it, so NoShowDetectionBackgroundService flags it a no-show
            // again next cycle regardless of who actually shows up, repeating weekly until
            // MaxAutoCarryForwards silently caps it — confirmed live as the "9 straight weeks"
            // incident referenced above. Reset the per-occurrence join flags too: they describe
            // whether this parent/participant joined the OLD session, which says nothing about
            // the new one.
            if (session.Type == SessionType.Demo)
            {
                var demoBooking = await _unitOfWork.Repository<DemoBooking>().TrackedQuery()
                    .Include(b => b.Participants)
                    .FirstOrDefaultAsync(b => b.ClassSessionId == session.Id, cancellationToken);
                if (demoBooking is not null)
                {
                    demoBooking.ClassSessionId = carriedForward.Id;
                    demoBooking.ParentJoinedAtUtc = null;
                    foreach (var participant in demoBooking.Participants)
                    {
                        participant.HasJoined = false;
                    }
                }
            }

            await _auditLog.StageAsync(AuditAction.Update, nameof(ClassSession), session.Id.ToString(),
                changesJson: "{\"noShow\":\"" + party + "\",\"carriedForwardTo\":\"" + carriedForward.Id + "\""
                    + (carriedForwardHasConflict ? ",\"carriedForwardScheduleConflict\":true" : "") + "}",
                cancellationToken: cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return await GetAsync(carriedForward.Id, cancellationToken);
        }

        /// <summary>Walks forward a day at a time (bounded) to the next date that isn't a holiday.</summary>
        private async Task<DateTime> NextNonHolidayDateAsync(DateTime candidateUtc, CancellationToken cancellationToken)
        {
            // The whole 14-day window is fetched in one query rather than probed a day at a
            // time — the walk itself is unchanged, but it no longer costs up to 14 sequential
            // round trips (one per candidate day) to find a date that is usually the first one.
            var windowStart = DateOnly.FromDateTime(candidateUtc);
            var windowEnd = DateOnly.FromDateTime(candidateUtc.AddDays(13));
            var holidayDates = (await _unitOfWork.Repository<Holiday>().Query()
                    .Where(h => h.Date >= windowStart && h.Date <= windowEnd)
                    .Select(h => h.Date)
                    .ToListAsync(cancellationToken))
                .ToHashSet();

            for (var i = 0; i < 14; i++)
            {
                var candidate = candidateUtc.AddDays(i);
                if (!holidayDates.Contains(DateOnly.FromDateTime(candidate)))
                {
                    return candidate;
                }
            }

            // 14 consecutive holidays isn't realistic — fall back rather than search forever.
            return candidateUtc;
        }

        public async Task<SessionRecordingDto> AddRecordingAsync(
            Guid sessionId,
            RegisterRecordingRequest request,
            CancellationToken cancellationToken = default)
        {
            var session = await _unitOfWork.Repository<ClassSession>().GetByIdAsync(sessionId, cancellationToken)
                ?? throw new NotFoundException(nameof(ClassSession), sessionId);

            // A recording is served on to the batch's parents, so only this session's own
            // teacher (or an Admin) may attach one — not any teacher who knows a session id.
            await EnsureSessionParticipantAsync(session, cancellationToken);

            var recording = new SessionRecording
            {
                ClassSessionId = sessionId,
                StorageUrl = request.StorageUrl,
                DurationSeconds = request.DurationSeconds,
                // Parent access is view-only for 15 days; the expiry job hides it afterwards
                ExpiresAtUtc = DateTime.UtcNow.AddDays(15),
            };
            await _unitOfWork.Repository<SessionRecording>().AddAsync(recording, cancellationToken);
            await _auditLog.StageAsync(AuditAction.Create, nameof(SessionRecording), recording.Id.ToString(), cancellationToken: cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return ToRecordingDto(recording);
        }

        public async Task<SessionRecordingDto?> FinalizeJibriRecordingAsync(
            string roomName,
            string? bearerToken,
            string storageUrl,
            int? durationSeconds,
            CancellationToken cancellationToken = default)
        {
            var jitsiConfigJson = await _unitOfWork.Repository<Integration>().Query()
                .Where(i => i.Key == "jitsi")
                .Select(i => i.ConfigJson)
                .FirstOrDefaultAsync(cancellationToken);

            if (!_jitsiTokenService.ValidateFinalizeToken(bearerToken, jitsiConfigJson, roomName))
            {
                throw new UnauthorizedException("Invalid or missing recording-finalize token.");
            }

            // Not every room maps to a ClassSession (personal rooms, demo bookings) — Jibri
            // records those the same as any other room, but there's no session row here to
            // attach the recording to, so this is a no-op rather than a NotFoundException: the
            // finalize script has no session id to have gotten wrong, only a room name that's
            // legitimately outside this feature's scope.
            //
            // A personal room's MeetingRoomId is a permanent per-teacher identifier reused
            // across every demo/1-on-1 class that teacher ever runs -- NOT unique per occurrence
            // the way a batch's room id effectively is. Matching on MeetingRoomId alone with
            // FirstOrDefault silently piled every one of that teacher's recordings, forever,
            // onto whichever session row happened to sort first -- confirmed in production as
            // multiple unrelated recordings stacked on one old session while every later class
            // using that room showed "no recording". Disambiguate by picking the candidate whose
            // scheduled end is closest to now: finalize always fires within minutes of the class
            // that was actually recorded ending, so that's the one this recording belongs to.
            var candidates = await _unitOfWork.Repository<ClassSession>().Query()
                .Where(s => s.MeetingRoomId == roomName)
                .ToListAsync(cancellationToken);
            if (candidates.Count == 0)
            {
                return null;
            }

            var now = DateTime.UtcNow;
            var session = candidates.Count == 1
                ? candidates[0]
                : candidates.MinBy(s => Math.Abs(((s.ActualEndAtUtc ?? s.ScheduledEndAtUtc) - now).Ticks))!;

            var recording = new SessionRecording
            {
                ClassSessionId = session.Id,
                StorageUrl = storageUrl,
                DurationSeconds = durationSeconds,
                // Same 15-day parent visibility window as AddRecordingAsync (the teacher-facing
                // manual-upload path) — one policy regardless of how the recording got attached.
                ExpiresAtUtc = DateTime.UtcNow.AddDays(15),
            };
            await _unitOfWork.Repository<SessionRecording>().AddAsync(recording, cancellationToken);
            await _auditLog.StageAsync(AuditAction.Create, nameof(SessionRecording), recording.Id.ToString(), cancellationToken: cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return ToRecordingDto(recording);
        }

        public async Task<IReadOnlyList<SessionRecordingDto>> ListRecordingsAsync(
            Guid sessionId,
            CancellationToken cancellationToken = default)
        {
            var session = await _unitOfWork.Repository<ClassSession>().GetByIdAsync(sessionId, cancellationToken)
                ?? throw new NotFoundException(nameof(ClassSession), sessionId);

            // Recordings show real children in a live class; scoping to this session's own
            // participants keeps one teacher out of another teacher's classroom footage.
            await EnsureSessionParticipantAsync(session, cancellationToken);

            var now = DateTime.UtcNow;
            var recordings = await _unitOfWork.Repository<SessionRecording>().Query()
                .Where(r => r.ClassSessionId == sessionId && (r.ExpiresAtUtc == null || r.ExpiresAtUtc > now))
                .OrderByDescending(r => r.CreatedAtUtc)
                .ToListAsync(cancellationToken);

            return recordings.Select(ToRecordingDto).ToList();
        }

        /// <summary>Admin-wide (teacherUserId null) or one teacher's own (teacherUserId set)
        /// Recordings page: every registered recording in scope, one query instead of a
        /// completed-session list plus one ListRecordingsAsync call per session (confirmed live
        /// as the admin page's actual "Loading recordings..." bottleneck once there were enough
        /// completed classes -- TeacherRecordings.tsx had the identical N+1 shape, just scoped
        /// to "my classes", so it was only ever a matter of time before the same slowdown showed
        /// up there too as any one teacher's own history grew). Unlike ListRecordingsAsync
        /// (parent-facing), this deliberately does NOT filter out expired recordings -- both an
        /// admin and a teacher managing their own past classes need to find one regardless of
        /// whether a parent could still view it.</summary>
        public async Task<PagedResult<RecordingListItemDto>> ListAllRecordingsAsync(
            int page,
            int pageSize,
            DateOnly? date,
            Guid? teacherUserId,
            CancellationToken cancellationToken = default)
        {
            page = Math.Max(1, page);
            pageSize = Math.Clamp(pageSize, 1, 100);

            var query = _unitOfWork.Repository<SessionRecording>().Query()
                .Include(r => r.ClassSession).ThenInclude(s => s.Batch)
                .Include(r => r.ClassSession).ThenInclude(s => s.TeacherProfile).ThenInclude(t => t.User)
                .AsQueryable();

            if (teacherUserId is { } tuid)
            {
                query = query.Where(r => r.ClassSession.TeacherProfile.UserId == tuid);
            }

            if (date is { } d)
            {
                var startUtc = d.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
                var endUtc = startUtc.AddDays(1);
                query = query.Where(r => r.ClassSession.ScheduledStartAtUtc >= startUtc && r.ClassSession.ScheduledStartAtUtc < endUtc);
            }

            query = query.OrderByDescending(r => r.CreatedAtUtc);

            var totalCount = await query.CountAsync(cancellationToken);
            var page_ = await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(cancellationToken);

            return new PagedResult<RecordingListItemDto>
            {
                Items = page_.Select(r => new RecordingListItemDto
                {
                    Id = r.Id,
                    ClassSessionId = r.ClassSessionId,
                    StorageUrl = r.StorageUrl,
                    DurationSeconds = r.DurationSeconds,
                    ExpiresAtUtc = r.ExpiresAtUtc,
                    CreatedAtUtc = r.CreatedAtUtc,
                    BatchName = r.ClassSession.Batch?.Name,
                    SessionType = r.ClassSession.Type,
                    TeacherName = $"{r.ClassSession.TeacherProfile.User.FirstName} {r.ClassSession.TeacherProfile.User.LastName}".Trim(),
                    ScheduledStartAtUtc = r.ClassSession.ScheduledStartAtUtc,
                }).ToList(),
                TotalCount = totalCount,
                Page = page,
                PageSize = pageSize,
            };
        }

        public async Task DeleteRecordingAsync(
            Guid sessionId,
            Guid recordingId,
            CancellationToken cancellationToken = default)
        {
            // Admin-only is enforced by the controller's [Authorize(Roles)] — this just has to
            // exist and belong to the session named in the route.
            var recording = await _unitOfWork.Repository<SessionRecording>().TrackedQuery()
                .FirstOrDefaultAsync(r => r.Id == recordingId && r.ClassSessionId == sessionId, cancellationToken)
                ?? throw new NotFoundException(nameof(SessionRecording), recordingId);

            // Only unregisters the DB row — IFileStorage has no delete operation, and a
            // recording may live in storage the Jibri pipeline wrote to directly rather than
            // through this app's own upload path, so there's nothing safe to reach in and purge.
            _unitOfWork.Repository<SessionRecording>().Remove(recording);
            await _auditLog.StageAsync(AuditAction.Delete, nameof(SessionRecording), recording.Id.ToString(), cancellationToken: cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }

        public async Task<IReadOnlyList<ClassSessionDto>> GenerateScheduleAsync(
            Guid batchId,
            GenerateScheduleRequest request,
            CancellationToken cancellationToken = default)
        {
            var batch = await _unitOfWork.Repository<Batch>().GetByIdAsync(batchId, cancellationToken)
                ?? throw new NotFoundException(nameof(Batch), batchId);
            var course = await _unitOfWork.Repository<Course>().GetByIdAsync(batch.CourseId, cancellationToken)
                ?? throw new NotFoundException(nameof(Course), batch.CourseId);

            var hasSessions = await _unitOfWork.Repository<ClassSession>()
                .ExistsAsync(s => s.BatchId == batchId, cancellationToken);
            if (hasSessions)
            {
                throw new DomainValidationException("This batch already has scheduled sessions; reschedule or cancel them individually.");
            }

            var slotDays = request.Slots.Select(s => s.DayOfWeek).ToList();
            if (slotDays.Count != slotDays.Distinct().Count())
            {
                throw new DomainValidationException("Each weekday can only have one time — remove the duplicate before generating.");
            }
            // Each weekday runs at its own time — e.g. Monday/Wednesday at 5 PM but Friday at
            // noon — rather than one time applying to every selected day.
            var timeByDay = request.Slots.ToDictionary(s => s.DayOfWeek, s => s.StartTimeUtc);
            var holidays = (await _unitOfWork.Repository<Holiday>().Query()
                    .Select(h => h.Date)
                    .ToListAsync(cancellationToken))
                .ToHashSet();

            var sessionRepository = _unitOfWork.Repository<ClassSession>();
            var date = request.StartDate;
            var created = 0;
            DateOnly? lastDate = null;
            var durationMinutes = batch.DurationMinutesOverride ?? course.DurationMinutes;

            // Defaults to every session of the course (the only behaviour before SessionCount
            // existed); overridable for a batch that's continuing mid-course under this portal
            // (e.g. migrated from another system with some sessions already delivered there) —
            // there was previously no way to generate only the sessions actually still owed.
            var targetSessionCount = request.SessionCount ?? course.TotalSessions;

            // Walk the calendar until every target session is placed; hard cap
            // of two years guards against a weekday set that never matches.
            var safetyLimit = request.StartDate.AddYears(2);
            while (created < targetSessionCount && date < safetyLimit)
            {
                if (timeByDay.TryGetValue(date.DayOfWeek, out var timeForDay) && !holidays.Contains(date))
                {
                    var startUtc = date.ToDateTime(timeForDay, DateTimeKind.Utc);
                    await EnsureTeacherIsFreeAsync(
                        batch.TeacherProfileId, startUtc, startUtc.AddMinutes(durationMinutes), cancellationToken);
                    await sessionRepository.AddAsync(
                        new ClassSession
                        {
                            BatchId = batch.Id,
                            TeacherProfileId = batch.TeacherProfileId,
                            ScheduledStartAtUtc = startUtc,
                            ScheduledEndAtUtc = startUtc.AddMinutes(durationMinutes),
                            MeetingRoomId = $"trn-{Guid.NewGuid():N}",
                        },
                        cancellationToken);
                    created++;
                    lastDate = date;
                }

                date = date.AddDays(1);
            }

            if (created < targetSessionCount)
            {
                throw new DomainValidationException("Could not place all sessions within two years; check the selected weekdays.");
            }

            batch.StartDate ??= request.StartDate;
            batch.EndDate = lastDate;

            await _auditLog.StageAsync(AuditAction.Create, nameof(ClassSession),
                changesJson: $"{{\"batchId\":\"{batch.Id}\",\"generated\":{created}}}",
                entityId: batch.Id.ToString(), cancellationToken: cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return await ListAsync(DateTime.MinValue, DateTime.MaxValue, null, batch.Id, cancellationToken);
        }

        private async Task MoveBatchToDormantIfCourseCompletedAsync(ClassSession current, CancellationToken cancellationToken)
        {
            var batch = await _unitOfWork.Repository<Batch>().GetByIdAsync(current.BatchId!.Value, cancellationToken);
            if (batch is null || batch.Status != BatchStatus.Active)
            {
                return;
            }

            var course = await _unitOfWork.Repository<Course>().GetByIdAsync(batch.CourseId, cancellationToken);
            if (course is null)
            {
                return;
            }

            var completedBefore = await _unitOfWork.Repository<ClassSession>().Query()
                .CountAsync(s => s.BatchId == batch.Id && s.Status == SessionStatus.Completed, cancellationToken);

            // +1 for the session being completed in this unit of work (not yet saved)
            if (completedBefore + 1 >= course.TotalSessions)
            {
                batch.Status = BatchStatus.Dormant;
                batch.CompletedAtUtc = DateTime.UtcNow;
                await ExpireSubscriptionsForCompletedBatchAsync(batch, cancellationToken);
            }
        }

        /// <summary>
        /// A Subscription has no direct link to a Batch — only ChildId + PackagePlanId — so
        /// there's no FK this could join on directly. Instead: find the children who were
        /// actively enrolled in this (now finished) batch, then find their Active subscriptions
        /// whose plan is for the SAME course this batch just ran. Those subscriptions were
        /// paying for a course that has now finished, so leaving them Active would let
        /// BillingBackgroundService keep invoicing for it indefinitely.
        /// Edge case accepted: a child enrolled in two concurrent batches of the same course
        /// would have that subscription expired when either batch finishes first.
        /// </summary>
        private async Task ExpireSubscriptionsForCompletedBatchAsync(Batch batch, CancellationToken cancellationToken)
        {
            var childIds = await _unitOfWork.Repository<BatchEnrollment>().Query()
                .Where(e => e.BatchId == batch.Id && e.Status == EnrollmentStatus.Active)
                .Select(e => e.ChildId)
                .ToListAsync(cancellationToken);
            if (childIds.Count == 0)
            {
                return;
            }

            var subscriptions = await _unitOfWork.Repository<Subscription>().TrackedQuery()
                .Where(s => childIds.Contains(s.ChildId)
                    && s.Status == SubscriptionStatus.Active
                    && s.PackagePlan.CourseId == batch.CourseId)
                .ToListAsync(cancellationToken);

            foreach (var subscription in subscriptions)
            {
                subscription.Status = SubscriptionStatus.Expired;
                subscription.NextBillingAtUtc = null;
            }
        }

        /// <summary>
        /// Every teacher's fixed demo room: their permanent personal meeting room (the same one
        /// GET /api/users/me/meeting-room mints), so a demo's join link is stable regardless of
        /// which screen scheduled it, rather than a new random room each time. Mints the room on
        /// first use, same convention as UsersController.MyMeetingRoom and
        /// DemoBookingService.EnsureTeacherMeetingRoomAsync (this is that same helper,
        /// duplicated rather than shared — the two services don't have a common base to hang a
        /// shared helper off of). Caller is responsible for SaveChangesAsync.
        /// </summary>
        private async Task<TeacherProfile> EnsureTeacherMeetingRoomAsync(Guid teacherProfileId, CancellationToken cancellationToken)
        {
            var teacher = await _unitOfWork.Repository<TeacherProfile>().TrackedQuery()
                .Include(t => t.User)
                .FirstOrDefaultAsync(t => t.Id == teacherProfileId, cancellationToken)
                ?? throw new NotFoundException(nameof(TeacherProfile), teacherProfileId);

            if (string.IsNullOrEmpty(teacher.User.PersonalMeetingRoomId))
            {
                teacher.User.PersonalMeetingRoomId = $"trn-personal-{Guid.NewGuid():N}";
                _unitOfWork.Repository<User>().Update(teacher.User);
            }

            return teacher;
        }

        /// <summary>
        /// Scheduling conflict / availability check: blocks double-booking a teacher across
        /// batches, and blocks slots inside an approved leave window.
        /// </summary>
        private async Task EnsureTeacherIsFreeAsync(
            Guid teacherProfileId,
            DateTime startUtc,
            DateTime endUtc,
            CancellationToken cancellationToken,
            Guid? excludeSessionId = null)
        {
            var conflict = await _unitOfWork.Repository<ClassSession>().Query()
                .Where(s => s.TeacherProfileId == teacherProfileId
                            && s.Id != excludeSessionId
                            && (s.Status == SessionStatus.Scheduled
                                || s.Status == SessionStatus.InProgress
                                || s.Status == SessionStatus.CarriedForward)
                            && s.ScheduledStartAtUtc < endUtc
                            && s.ScheduledEndAtUtc > startUtc)
                .OrderBy(s => s.ScheduledStartAtUtc)
                .FirstOrDefaultAsync(cancellationToken);
            if (conflict is not null)
            {
                throw new DomainValidationException(
                    $"The teacher already has a session from {DateTimeDisplay.ToLocal(conflict.ScheduledStartAtUtc)} to {DateTimeDisplay.ToLocal(conflict.ScheduledEndAtUtc)}.");
            }

            var onLeave = await _unitOfWork.Repository<LeaveRequest>().ExistsAsync(
                l => l.TeacherProfileId == teacherProfileId
                     && l.Status == LeaveStatus.Approved
                     && l.StartAtUtc < endUtc
                     && l.EndAtUtc > startUtc,
                cancellationToken);
            if (onLeave)
            {
                throw new DomainValidationException("The teacher is on approved leave during this slot.");
            }
        }

        /// <summary>Booking confirmation email to the teacher (and demo parents get theirs via DemoBookingService).</summary>
        private async Task SendBookingConfirmationAsync(ClassSession session, CancellationToken cancellationToken)
        {
            var teacher = await _unitOfWork.Repository<TeacherProfile>().Query()
                .Include(t => t.User)
                .FirstOrDefaultAsync(t => t.Id == session.TeacherProfileId, cancellationToken);
            if (teacher is null)
            {
                return;
            }

            await _notificationService.SendTemplatedEmailAsync(
                teacher.User.Id,
                teacher.User.Email,
                NotificationType.BookingConfirmation,
                "class-scheduled",
                new Dictionary<string, string>
                {
                    ["TeacherFirstName"] = teacher.User.FirstName,
                    ["SessionType"] = session.Type.ToString(),
                    ["StartAtLocal"] = DateTimeDisplay.ToLocal(session.ScheduledStartAtUtc, teacher.User.TimeZoneId),
                    ["EndAtLocal"] = DateTimeDisplay.ToLocal(session.ScheduledEndAtUtc, teacher.User.TimeZoneId),
                },
                cancellationToken);
        }

        public async Task RecordEngagementAsync(
            Guid sessionId,
            RecordEngagementRequest request,
            CancellationToken cancellationToken = default)
        {
            var session = await _unitOfWork.Repository<ClassSession>().GetByIdAsync(sessionId, cancellationToken)
                ?? throw new NotFoundException(nameof(ClassSession), sessionId);

            await EnsureSessionParticipantAsync(session, cancellationToken);

            var repository = _unitOfWork.Repository<EngagementEvent>();
            foreach (var entry in request.Events)
            {
                await repository.AddAsync(
                    new EngagementEvent
                    {
                        ClassSessionId = sessionId,
                        ChildId = entry.ChildId,
                        ParticipantName = entry.ParticipantName.Trim(),
                        Type = entry.Type,
                        Value = entry.Value,
                    },
                    cancellationToken);
            }

            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }

        /// <summary>
        /// Engagement is posted by whoever is actually in the live class (teacher or
        /// a parent/student), so this endpoint stays open to any signed-in role —
        /// but the caller must genuinely belong to this specific session, or anyone
        /// could spam engagement history for a class they have no part in.
        /// </summary>
        private async Task EnsureSessionParticipantAsync(ClassSession session, CancellationToken cancellationToken)
        {
            var userId = _currentUser.UserId
                ?? throw new UnauthorizedException("Not signed in.");

            if (!await IsSessionParticipantAsync(session, userId, cancellationToken))
            {
                throw new ForbiddenException("You do not have access to this session.");
            }
        }

        public async Task<bool> IsSessionParticipantAsync(Guid sessionId, Guid userId, CancellationToken cancellationToken = default)
        {
            var session = await _unitOfWork.Repository<ClassSession>().GetByIdAsync(sessionId, cancellationToken);
            return session is not null && await IsSessionParticipantAsync(session, userId, cancellationToken);
        }

        private async Task<bool> IsSessionParticipantAsync(ClassSession session, Guid userId, CancellationToken cancellationToken)
        {
            var user = await _unitOfWork.Repository<User>().GetByIdAsync(userId, cancellationToken);
            if (user is null)
            {
                return false;
            }

            if (user.Role == UserRole.Admin)
            {
                return true;
            }

            if (user.Role == UserRole.Teacher)
            {
                return await _unitOfWork.Repository<TeacherProfile>()
                    .ExistsAsync(t => t.Id == session.TeacherProfileId && t.UserId == userId, cancellationToken);
            }

            if (user.Role == UserRole.Parent && session.BatchId.HasValue)
            {
                // Active only: a withdrawn/completed enrollment must not keep live-room
                // access — mirrors AcademicOpsService.CaptureJoinAttendanceAsync's own filter,
                // which this check used to be looser than (attendance wasn't captured for a
                // non-active enrollment even though the room let them in).
                var enrolledChildren = await _unitOfWork.Repository<BatchEnrollment>().Query()
                    .Where(e => e.BatchId == session.BatchId.Value && e.Status == EnrollmentStatus.Active)
                    .Join(_unitOfWork.Repository<Child>().Query(), e => e.ChildId, c => c.Id, (e, c) => new { c.Id, c.ParentProfileId, ParentUserId = c.ParentProfile.UserId })
                    .Where(c => c.ParentUserId == userId)
                    .ToListAsync(cancellationToken);
                if (enrolledChildren.Count == 0)
                {
                    return false;
                }

                // FeeSuspension existed but nothing here ever checked it -- a suspended parent
                // could already join their child's live class via this exact path despite the
                // entity's own doc comment ("cannot join live sessions"). Blocked only when
                // EVERY one of this parent's enrolled children in this specific batch is
                // suspended -- a sibling sharing the batch with fees current still gets in.
                foreach (var child in enrolledChildren)
                {
                    if (!await SuspensionCheck.IsChildBlockedAsync(_unitOfWork, child.ParentProfileId, child.Id, cancellationToken))
                    {
                        return true;
                    }
                }
                return false;
            }

            // A demo session has no batch — the lead is a DemoBooking (parent may not have
            // an account yet), so a registered parent joining their own demo is matched by
            // email instead of a BatchEnrollment. Covers both the primary contact and any
            // additional invited parent/guardian on the booking (DemoParticipant.Email).
            if (user.Role == UserRole.Parent && session.BatchId is null && !string.IsNullOrWhiteSpace(user.Email))
            {
                return await _unitOfWork.Repository<DemoBooking>().Query()
                    .Where(b => b.ClassSessionId == session.Id)
                    .AnyAsync(
                        b => b.ParentEmail.ToLower() == user.Email.ToLower()
                            || b.Participants.Any(p => p.Email != null && p.Email.ToLower() == user.Email.ToLower()),
                        cancellationToken);
            }

            // Coordinator (and anyone else with the same scheduling-edit grant): "the
            // coordinator can drop into any ongoing/upcoming class or demo" is documented,
            // deliberate monitor access on the frontend (coordinator/Calendar.tsx's Join Class
            // button) — not scoped to a specific batch/session the way Parent/Teacher are,
            // since coordinating means being able to check any of them.
            if (user.Role == UserRole.SubAdmin)
            {
                return await _unitOfWork.Repository<SubAdminPermission>().ExistsAsync(
                    p => p.UserId == userId && p.Module == nameof(PermissionModule.SessionCalendarManagement) && p.CanEdit,
                    cancellationToken);
            }

            return false;
        }

        public async Task<Guid> ResolveCurrentSessionIdAsync(Guid sessionId, CancellationToken cancellationToken = default)
        {
            var currentId = sessionId;
            // Bounded, not a plain "follow until null": a data bug that somehow formed a cycle
            // must not hang this request forever — no genuine reschedule chain should ever get
            // remotely this deep.
            for (var hop = 0; hop < 50; hop++)
            {
                var status = await _unitOfWork.Repository<ClassSession>().Query()
                    .Where(s => s.Id == currentId)
                    .Select(s => (SessionStatus?)s.Status)
                    .FirstOrDefaultAsync(cancellationToken);
                if (status is null or not SessionStatus.Rescheduled)
                {
                    // Doesn't exist (let the caller's own not-found handling fire against the
                    // ORIGINAL id) or isn't rescheduled — this is the current, live session.
                    return currentId;
                }

                var next = await _unitOfWork.Repository<ClassSession>().Query()
                    .Where(s => s.RescheduledFromSessionId == currentId)
                    .Select(s => (Guid?)s.Id)
                    .FirstOrDefaultAsync(cancellationToken);
                if (next is null)
                {
                    // Rescheduled but nothing claims to be its replacement — shouldn't happen
                    // (RescheduleAsync always creates one in the same transaction) — stop on
                    // this data inconsistency rather than loop.
                    return currentId;
                }
                currentId = next.Value;
            }
            return currentId;
        }

        public async Task<JitsiJoinDto> GetJitsiJoinAsync(Guid sessionId, Guid userId, CancellationToken cancellationToken = default)
        {
            // Confirmed live: a teacher and a student, each joining from their own portal after
            // an admin edited (rescheduled) the class, ended up with two different-but-both-
            // "valid" session ids for what they both thought was the same class — most often
            // because one of them still had the page open from before the edit and never
            // reloaded the now-stale list it was showing. Both landed in the same Jitsi video
            // room regardless (MeetingRoomId carries over on a reschedule), so the call itself
            // looked completely fine to both — but the ClassroomHub group is keyed by session
            // id, so neither's People roster or whiteboard ever synced with the other. Resolving
            // to the current session before doing anything else means it no longer matters which
            // id either side started from — same fix for the exact same symptom already found
            // and shipped for one specific cause (a stale DemoBooking link) now covers every
            // cause of it, including a browser tab that's simply been open since before the edit.
            var resolvedSessionId = await ResolveCurrentSessionIdAsync(sessionId, cancellationToken);
            var session = await _unitOfWork.Repository<ClassSession>().GetByIdAsync(resolvedSessionId, cancellationToken)
                ?? throw new NotFoundException(nameof(ClassSession), sessionId);

            if (string.IsNullOrWhiteSpace(session.MeetingRoomId))
            {
                throw new DomainValidationException("This session has no meeting room yet.");
            }

            var user = await _unitOfWork.Repository<User>().GetByIdAsync(userId, cancellationToken)
                ?? throw new UnauthorizedException("Not signed in.");

            if (!await IsSessionParticipantAsync(session, userId, cancellationToken))
            {
                throw new ForbiddenException("You do not have access to this session.");
            }

            // Mirrors the frontend's own join-window rule (parent/utils.ts isJoinable: 10
            // minutes before start until the scheduled end) — only enforced client-side before
            // this, so a real, usable room + token was one direct GET away for any session at
            // any time, past or weeks out, regardless of what the join button showed.
            //
            // Admin/SubAdmin-monitor is the one exception: admin/Sessions.tsx and
            // coordinator/Calendar.tsx both deliberately show "Join" for every
            // scheduled/demo session regardless of how far out it is — "Admin had no way at
            // all to drop into a live class from this screen" is that feature's own comment —
            // so this check applying to them too silently broke the button with "This class
            // hasn't opened for joining yet." on anything more than 10 minutes away. A genuine
            // participant (Teacher/Parent) still only gets the real join window.
            var isMonitor = user.Role is UserRole.Admin or UserRole.SubAdmin;
            var now = DateTime.UtcNow;
            if (!isMonitor && now < session.ScheduledStartAtUtc.AddMinutes(-10))
            {
                throw new DomainValidationException("This class hasn't opened for joining yet.");
            }
            // This deployment's Jitsi has no duration cap (see docs/LONG_DURATION_SESSIONS.md) and
            // JitsiLive.tsx's own "Continue Class" flow exists specifically so classes can legitimately
            // run past ScheduledEndAtUtc — but this check, enforced against the wall clock alone, still
            // refused a rejoin for anyone (teacher or student) who disconnected after that instant, even
            // with the class actively InProgress. Confirmed live gap, not a Jitsi limitation. InProgress
            // sessions get no time cutoff at all now; a still-Scheduled session (never actually started)
            // keeps the original cutoff so a stale/abandoned booking can't be joined indefinitely.
            if (!isMonitor && session.Status != SessionStatus.InProgress && now > session.ScheduledEndAtUtc)
            {
                throw new DomainValidationException("This class has already ended.");
            }

            var jitsiConfigJson = await _unitOfWork.Repository<Integration>().Query()
                .Where(i => i.Key == "jitsi")
                .Select(i => i.ConfigJson)
                .FirstOrDefaultAsync(cancellationToken);
            var domain = JitsiLinkBuilder.ResolveDomain(jitsiConfigJson);
            var moderator = user.Role is UserRole.Teacher or UserRole.Admin;

            var token = _jitsiTokenService.CreateToken(
                domain,
                jitsiConfigJson,
                session.MeetingRoomId,
                $"{user.FirstName} {user.LastName}".Trim(),
                user.Email,
                moderator,
                // A couple of hours past the scheduled end covers overruns without leaving a
                // token that's valid indefinitely — it dies with the class, not with the link.
                session.ScheduledEndAtUtc.AddHours(2));

            return new JitsiJoinDto
            {
                SessionId = session.Id,
                Room = session.MeetingRoomId,
                Domain = domain,
                Token = token,
                ScheduledEndAtUtc = session.ScheduledEndAtUtc,
                IsDemo = session.Type == SessionType.Demo,
            };
        }

        public async Task<IReadOnlyList<GuestLinkStudentDto>> GetGuestLinkStudentsAsync(Guid sessionId, CancellationToken cancellationToken = default)
        {
            var resolvedSessionId = await ResolveCurrentSessionIdAsync(sessionId, cancellationToken);
            var session = await _unitOfWork.Repository<ClassSession>().GetByIdAsync(resolvedSessionId, cancellationToken)
                ?? throw new NotFoundException(nameof(ClassSession), sessionId);

            if (session.BatchId is not Guid batchId)
            {
                return Array.Empty<GuestLinkStudentDto>();
            }

            return await _unitOfWork.Repository<BatchEnrollment>().Query()
                .Where(e => e.BatchId == batchId && e.Status == EnrollmentStatus.Active)
                .Join(_unitOfWork.Repository<Child>().Query(), e => e.ChildId, c => c.Id, (e, c) => c)
                .Join(_unitOfWork.Repository<ParentProfile>().Query(), c => c.ParentProfileId, p => p.Id, (c, p) => new { Child = c, ParentUserId = p.UserId })
                .Join(_unitOfWork.Repository<User>().Query(), cp => cp.ParentUserId, u => u.Id, (cp, u) => new GuestLinkStudentDto
                {
                    ChildId = cp.Child.Id,
                    ChildName = (cp.Child.FirstName + " " + cp.Child.LastName).Trim(),
                    ParentName = (u.FirstName + " " + u.LastName).Trim(),
                    ParentPhone = u.Phone,
                })
                .OrderBy(s => s.ChildName)
                .ToListAsync(cancellationToken);
        }

        public async Task<GuestLinkDto> CreateGuestLinkAsync(Guid sessionId, Guid? childId, CancellationToken cancellationToken = default)
        {
            var resolvedSessionId = await ResolveCurrentSessionIdAsync(sessionId, cancellationToken);
            var session = await _unitOfWork.Repository<ClassSession>().GetByIdAsync(resolvedSessionId, cancellationToken)
                ?? throw new NotFoundException(nameof(ClassSession), sessionId);

            if (string.IsNullOrWhiteSpace(session.MeetingRoomId))
            {
                throw new DomainValidationException("This session has no meeting room yet.");
            }

            if (childId is Guid cid)
            {
                var isActiveEnrollment = session.BatchId is Guid batchId && await _unitOfWork.Repository<BatchEnrollment>()
                    .ExistsAsync(e => e.BatchId == batchId && e.ChildId == cid && e.Status == EnrollmentStatus.Active, cancellationToken);
                if (!isActiveEnrollment)
                {
                    throw new DomainValidationException("That student isn't enrolled in this session's batch.");
                }
            }

            // Outer safety bound only -- GetGuestJoinAsync re-checks the session's live status/
            // time on every open, so this just stops a never-used link from staying mintable
            // indefinitely rather than being the thing that actually governs reuse.
            //
            // Bug fixed 2026-09-16: for a session whose scheduled end is already more than a day
            // in the past (nothing stops an admin/RM from opening "Copy Guest Link" on an old,
            // completed session), ScheduledEndAtUtc.AddDays(1) lands BEFORE DateTime.UtcNow --
            // JwtSecurityToken's constructor requires expires > notBefore (notBefore being "now"
            // below in JwtTokenService) and throws otherwise, which surfaced as an unhandled 500
            // ("Couldn't create the guest link — An unexpected error occurred") in production.
            // Clamped to never be earlier than a short margin past now, same "still bounded, not
            // a real gate" reasoning as this comment already describes -- GetGuestJoinAsync's own
            // live-status check is what actually rejects joining a long-over class, not this.
            var guestLinkExpiresAtUtc = session.ScheduledEndAtUtc.AddDays(1);
            if (guestLinkExpiresAtUtc <= DateTime.UtcNow)
            {
                guestLinkExpiresAtUtc = DateTime.UtcNow.AddHours(1);
            }
            var guestToken = _tokenService.CreateGuestJoinToken(session.Id, childId, guestLinkExpiresAtUtc);
            return new GuestLinkDto { Token = guestToken.AccessToken };
        }

        public async Task<GuestLinkDto> CreateGuestLinkForParticipantAsync(Guid sessionId, string guestName, string? guestEmail, CancellationToken cancellationToken = default)
        {
            var resolvedSessionId = await ResolveCurrentSessionIdAsync(sessionId, cancellationToken);
            var session = await _unitOfWork.Repository<ClassSession>().GetByIdAsync(resolvedSessionId, cancellationToken)
                ?? throw new NotFoundException(nameof(ClassSession), sessionId);

            if (string.IsNullOrWhiteSpace(session.MeetingRoomId))
            {
                throw new DomainValidationException("This session has no meeting room yet.");
            }

            // A long-lived outer bound, not the AddDays(1)-past-scheduled-end one
            // CreateGuestLinkAsync above uses -- GetGuestJoinAsync deliberately skips its own
            // live-status gate for this "named guest" token shape (see that method's own
            // comment), so this outer expiry is the only thing bounding it at all, and it needs
            // to outlive "revisited weeks later" the same way the DemoBookingService redirect
            // this replaces always has (its own old JoinTokenLifetime was 5 years, for the same
            // "still bounded, not literally forever" reasoning).
            var guestToken = _tokenService.CreateGuestJoinToken(
                session.Id, childId: null, DateTime.UtcNow.AddYears(5), guestName, guestEmail);
            return new GuestLinkDto { Token = guestToken.AccessToken };
        }

        public async Task<GuestJoinDto> GetGuestJoinAsync(string token, CancellationToken cancellationToken = default)
        {
            var parsed = _tokenService.ValidateGuestJoinToken(token)
                ?? throw new DomainValidationException("This link is invalid or has expired.");

            var resolvedSessionId = await ResolveCurrentSessionIdAsync(parsed.SessionId, cancellationToken);
            var session = await _unitOfWork.Repository<ClassSession>().GetByIdAsync(resolvedSessionId, cancellationToken)
                ?? throw new NotFoundException(nameof(ClassSession), resolvedSessionId);

            if (string.IsNullOrWhiteSpace(session.MeetingRoomId))
            {
                throw new DomainValidationException("This class has no meeting room yet.");
            }

            // A "named guest" link (DemoBookingService.CreateGuestLinkForParticipantAsync -- a
            // Demo lead's own join link, no Child/BatchEnrollment row to check) deliberately
            // skips the live status/time gate below entirely: it replaces the old raw-Jitsi demo
            // redirect, which had no such gate either ("Deliberately no time-based cutoff: a demo
            // that ran long, got revisited weeks later for a recap, or whose invite just sat
            // unopened all still resolve" -- see ResolveLiveJoinUrlAsync's own doc comment). Only
            // a childId-bound or generic RM/Admin/Coordinator Guest Link -- the client's explicit
            // "expires at session end/Completed" requirement -- gets the strict check.
            var isNamedGuest = !string.IsNullOrWhiteSpace(parsed.GuestName);
            if (!isNamedGuest)
            {
                // Live status/time check -- deliberately NOT relying on the guest token's own
                // (generous) expiry alone, so a link dies the instant the class is marked
                // Completed or Cancelled even if that happens well before its outer expiry bound.
                var now = DateTime.UtcNow;
                if (session.Status == SessionStatus.Cancelled)
                {
                    throw new DomainValidationException("This class was cancelled.");
                }
                if (session.Status == SessionStatus.Completed)
                {
                    throw new DomainValidationException("This class has already ended.");
                }
                if (now < session.ScheduledStartAtUtc.AddMinutes(-10))
                {
                    throw new DomainValidationException("This class hasn't opened for joining yet.");
                }
                if (session.Status != SessionStatus.InProgress && now > session.ScheduledEndAtUtc)
                {
                    throw new DomainValidationException("This class has already ended.");
                }
            }

            var childId = parsed.ChildId;
            var displayName = "Guest";
            string? participantEmail = null;
            if (childId is Guid cid)
            {
                var stillEnrolled = session.BatchId is Guid batchId && await _unitOfWork.Repository<BatchEnrollment>()
                    .ExistsAsync(e => e.BatchId == batchId && e.ChildId == cid && e.Status == EnrollmentStatus.Active, cancellationToken);
                var child = stillEnrolled ? await _unitOfWork.Repository<Child>().GetByIdAsync(cid, cancellationToken) : null;
                if (child is not null)
                {
                    displayName = $"{child.FirstName} {child.LastName}".Trim();
                }
                else
                {
                    // Defensive: the student was un-enrolled (or their record removed) after
                    // this link was generated. Fall back to a generic guest join rather than
                    // failing outright -- the class itself is still perfectly joinable.
                    childId = null;
                }
            }
            else if (isNamedGuest)
            {
                displayName = parsed.GuestName!;
                participantEmail = parsed.GuestEmail;
            }

            var jitsiConfigJson = await _unitOfWork.Repository<Integration>().Query()
                .Where(i => i.Key == "jitsi")
                .Select(i => i.ConfigJson)
                .FirstOrDefaultAsync(cancellationToken);
            var domain = JitsiLinkBuilder.ResolveDomain(jitsiConfigJson);

            // Based on "now", not session.ScheduledEndAtUtc: a named-guest (demo) join can
            // legitimately happen weeks after that timestamp (see isNamedGuest's own comment
            // above), and a token minted with an already-past expiry would be DOA. A couple of
            // hours past the moment of THIS open still covers a live class running long, same
            // intent the old ScheduledEndAtUtc-based expiry had for the still-time-gated case.
            var perOpenExpiresAtUtc = DateTime.UtcNow.AddHours(2);

            var jitsiToken = _jitsiTokenService.CreateToken(
                domain,
                jitsiConfigJson,
                session.MeetingRoomId,
                displayName,
                participantEmail,
                moderator: false,
                perOpenExpiresAtUtc);

            // Lets the landing page join the same interactive classroom (whiteboard, quiz,
            // roster, gamification) an ordinary logged-in student would -- see
            // CreateGuestClassroomHubToken's own doc comment for why this is a second, distinct
            // token from jitsiToken above (Jitsi's own call vs. this app's ClassroomHub).
            var hubToken = _tokenService.CreateGuestClassroomHubToken(
                session.Id, childId, displayName, perOpenExpiresAtUtc);

            return new GuestJoinDto
            {
                SessionId = session.Id,
                ChildId = childId,
                Room = session.MeetingRoomId,
                Domain = domain,
                Token = jitsiToken,
                DisplayName = displayName,
                SkipPrejoin = childId is not null || isNamedGuest,
                HubToken = hubToken.AccessToken,
            };
        }

        public async Task<RecordingObserverJoinDto?> GetLiveObserverJoinAsync(string roomName, CancellationToken cancellationToken = default)
        {
            // Disambiguation is simpler than FinalizeJibriRecordingAsync's closest-scheduled-end
            // heuristic: "InProgress right now" is unambiguous for a room that can only be one
            // class at a time on this deployment, whereas finalize runs after the fact against
            // every historical session that ever used the room.
            var candidates = await _unitOfWork.Repository<ClassSession>().Query()
                .Where(s => s.MeetingRoomId == roomName && s.Status == SessionStatus.InProgress)
                .ToListAsync(cancellationToken);

            if (candidates.Count == 0)
            {
                return null;
            }

            // Shouldn't happen -- but FinalizeJibriRecordingAsync's own history shows "shouldn't
            // happen" has happened before for this same MeetingRoomId-sharing pattern. Earliest
            // start keeps this deterministic rather than throwing, since Jibri can't react to an
            // error here anyway.
            var session = candidates.Count == 1
                ? candidates[0]
                : candidates.MinBy(s => s.ActualStartAtUtc ?? s.ScheduledStartAtUtc)!;

            var jitsiConfigJson = await _unitOfWork.Repository<Integration>().Query()
                .Where(i => i.Key == "jitsi")
                .Select(i => i.ConfigJson)
                .FirstOrDefaultAsync(cancellationToken);
            var domain = JitsiLinkBuilder.ResolveDomain(jitsiConfigJson);
            // A generous ceiling rather than tied to ScheduledEndAtUtc like a real participant's
            // token -- a class running long shouldn't have its own recording observer's access
            // expire mid-capture.
            var expiresAtUtc = DateTime.UtcNow.AddHours(4);

            var token = _jitsiTokenService.CreateRecordingObserverToken(domain, jitsiConfigJson, roomName, expiresAtUtc);
            var hubToken = _tokenService.CreateRecordingObserverHubToken(session.Id, expiresAtUtc).AccessToken;

            return new RecordingObserverJoinDto
            {
                SessionId = session.Id,
                Room = roomName,
                Domain = domain,
                Token = token,
                HubToken = hubToken,
                ExpiresAtUtc = expiresAtUtc,
            };
        }

        public async Task<ClassroomSettingsDto> GetClassroomSettingsAsync(CancellationToken cancellationToken = default)
        {
            var configJson = await _unitOfWork.Repository<Integration>().Query()
                .Where(i => i.Key == "jitsi")
                .Select(i => i.ConfigJson)
                .FirstOrDefaultAsync(cancellationToken);

            return new ClassroomSettingsDto
            {
                Domain = JitsiLinkBuilder.ResolveDomain(configJson),
                AutoRecordEnabled = ReadAutoRecordEnabled(configJson),
                DefaultLobbyEnabled = ReadDefaultLobbyEnabled(configJson),
            };
        }

        /// <summary>Admin, or specifically this session's own assigned teacher — narrower than
        /// <see cref="IsSessionParticipantAsync"/>, which also lets an enrolled parent through.</summary>
        private async Task EnsurePresentationModeratorAsync(ClassSession session, Guid userId, CancellationToken cancellationToken)
        {
            var user = await _unitOfWork.Repository<User>().GetByIdAsync(userId, cancellationToken)
                ?? throw new UnauthorizedException("Not signed in.");

            if (user.Role == UserRole.Admin)
            {
                return;
            }

            var isAssignedTeacher = user.Role == UserRole.Teacher
                && await _unitOfWork.Repository<TeacherProfile>()
                    .ExistsAsync(t => t.Id == session.TeacherProfileId && t.UserId == userId, cancellationToken);
            if (!isAssignedTeacher)
            {
                throw new ForbiddenException("Only this class's own teacher can present a deck here.");
            }
        }

        public async Task<SessionPresentationDto> UploadPresentationAsync(
            Guid sessionId,
            Guid userId,
            string storageUrl,
            string originalFileName,
            CancellationToken cancellationToken = default)
        {
            var session = await _unitOfWork.Repository<ClassSession>().GetByIdAsync(sessionId, cancellationToken)
                ?? throw new NotFoundException(nameof(ClassSession), sessionId);
            await EnsurePresentationModeratorAsync(session, userId, cancellationToken);

            var repository = _unitOfWork.Repository<SessionPresentation>();
            // Replaces any prior deck for this session rather than accumulating one row per
            // upload — a teacher swapping decks mid-prep shouldn't leave orphaned old ones
            // behind, and there's only ever one "current" deck to present.
            var existing = await repository.TrackedQuery().FirstOrDefaultAsync(p => p.ClassSessionId == sessionId, cancellationToken);
            if (existing is not null)
            {
                existing.StorageUrl = storageUrl;
                existing.OriginalFileName = originalFileName;
            }
            else
            {
                existing = new SessionPresentation
                {
                    ClassSessionId = sessionId,
                    StorageUrl = storageUrl,
                    OriginalFileName = originalFileName,
                };
                await repository.AddAsync(existing, cancellationToken);
            }

            await _unitOfWork.SaveChangesAsync(cancellationToken);
            return ToPresentationDto(existing);
        }

        public async Task<SessionPresentationDto?> GetPresentationAsync(Guid sessionId, Guid userId, CancellationToken cancellationToken = default)
        {
            var session = await _unitOfWork.Repository<ClassSession>().GetByIdAsync(sessionId, cancellationToken)
                ?? throw new NotFoundException(nameof(ClassSession), sessionId);
            if (!await IsSessionParticipantAsync(session, userId, cancellationToken))
            {
                throw new ForbiddenException("You do not have access to this session.");
            }

            var presentation = await _unitOfWork.Repository<SessionPresentation>().Query()
                .FirstOrDefaultAsync(p => p.ClassSessionId == sessionId, cancellationToken);
            return presentation is null ? null : ToPresentationDto(presentation);
        }

        private static SessionPresentationDto ToPresentationDto(SessionPresentation presentation) => new()
        {
            Id = presentation.Id,
            ClassSessionId = presentation.ClassSessionId,
            OriginalFileName = presentation.OriginalFileName,
            CreatedAtUtc = presentation.CreatedAtUtc,
        };

        public async Task<SessionPresentationDownloadDto> GetPresentationForDownloadAsync(Guid sessionId, Guid userId, CancellationToken cancellationToken = default)
        {
            var session = await _unitOfWork.Repository<ClassSession>().GetByIdAsync(sessionId, cancellationToken)
                ?? throw new NotFoundException(nameof(ClassSession), sessionId);
            if (!await IsSessionParticipantAsync(session, userId, cancellationToken))
            {
                throw new ForbiddenException("You do not have access to this session.");
            }

            var presentation = await _unitOfWork.Repository<SessionPresentation>().Query()
                .FirstOrDefaultAsync(p => p.ClassSessionId == sessionId, cancellationToken)
                ?? throw new NotFoundException(nameof(SessionPresentation), sessionId);
            return new SessionPresentationDownloadDto { StorageUrl = presentation.StorageUrl, OriginalFileName = presentation.OriginalFileName };
        }

        /// <summary>Defaults to on (today's unconditional behaviour) until an admin explicitly turns it off.</summary>
        private static bool ReadAutoRecordEnabled(string? configJson)
        {
            if (string.IsNullOrWhiteSpace(configJson))
            {
                return true;
            }

            try
            {
                var config = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(configJson);
                if (config is not null && config.TryGetValue("autoRecord", out var value) && bool.TryParse(value, out var parsed))
                {
                    return parsed;
                }
            }
            catch (System.Text.Json.JsonException)
            {
                // Malformed config — keep the safe default.
            }

            return true;
        }

        /// <summary>Defaults to on: a student joining before the teacher shouldn't land straight in an
        /// empty, unattended room by default — an admin can turn this off if a centre prefers the old
        /// always-open behaviour. See DefaultLobbyEnabled's own doc comment.</summary>
        private static bool ReadDefaultLobbyEnabled(string? configJson)
        {
            if (string.IsNullOrWhiteSpace(configJson))
            {
                return true;
            }

            try
            {
                var config = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(configJson);
                if (config is not null && config.TryGetValue("defaultLobby", out var value) && bool.TryParse(value, out var parsed))
                {
                    return parsed;
                }
            }
            catch (System.Text.Json.JsonException)
            {
                // Malformed config — keep the safe default.
            }

            return true;
        }

        public async Task<IReadOnlyList<EngagementSummaryDto>> GetEngagementSummaryAsync(
            Guid sessionId,
            CancellationToken cancellationToken = default)
        {
            var session = await _unitOfWork.Repository<ClassSession>().GetByIdAsync(sessionId, cancellationToken)
                ?? throw new NotFoundException(nameof(ClassSession), sessionId);

            // Per-child engagement scores are exactly the "how is this student doing" data
            // the posting side (RecordEngagementAsync) already gates on participation.
            await EnsureSessionParticipantAsync(session, cancellationToken);

            var events = await _unitOfWork.Repository<EngagementEvent>().Query()
                .Where(e => e.ClassSessionId == sessionId)
                .ToListAsync(cancellationToken);

            return events
                .GroupBy(e => new { e.ParticipantName, e.ChildId })
                .Select(group =>
                {
                    var quizAttempts = group.Where(e => e.Type is EngagementEventType.QuizAttempt or EngagementEventType.QuizCorrect).Sum(e => e.Value);
                    var quizCorrect = group.Where(e => e.Type == EngagementEventType.QuizCorrect).Sum(e => e.Value);
                    var activity = group.Where(e => e.Type is EngagementEventType.ActivityClick or EngagementEventType.ActivityCompleted).Sum(e => e.Value);
                    var whiteboard = group.Where(e => e.Type == EngagementEventType.WhiteboardInteraction).Sum(e => e.Value);
                    var attention = group.Where(e => e.Type == EngagementEventType.AttentionPing).Sum(e => e.Value);
                    var screenShare = group.Where(e => e.Type == EngagementEventType.ScreenShareSeconds).Sum(e => e.Value);

                    var score = EngagementScoring.Score(quizCorrect, quizAttempts, activity, whiteboard, attention);

                    return new EngagementSummaryDto
                    {
                        ParticipantName = group.Key.ParticipantName,
                        ChildId = group.Key.ChildId,
                        QuizAttempts = quizAttempts,
                        QuizCorrect = quizCorrect,
                        ActivityInteractions = activity,
                        WhiteboardInteractions = whiteboard,
                        AttentionPings = attention,
                        ScreenShareSeconds = screenShare,
                        EngagementScore = score,
                        LearningOutcome = score >= 60 ? "on-track" : score >= 30 ? "needs-encouragement" : "needs-attention",
                    };
                })
                .OrderByDescending(s => s.EngagementScore)
                .ToList();
        }

        /// <summary>Builds the auto-generated fallback used by CompleteAsync when the teacher left the summary blank.</summary>
        private static string BuildAutoSummary(IReadOnlyList<EngagementSummaryDto> engagement)
        {
            if (engagement.Count == 0)
            {
                return "Class completed. No interactive engagement was recorded for this session.";
            }

            var avgScore = (int)Math.Round(engagement.Average(e => e.EngagementScore));
            var onTrack = engagement.Count(e => e.LearningOutcome == "on-track");
            var needsEncouragement = engagement.Count(e => e.LearningOutcome == "needs-encouragement");
            var needsAttention = engagement.Count(e => e.LearningOutcome == "needs-attention");

            var outcomeParts = new List<string>();
            if (onTrack > 0) outcomeParts.Add($"{onTrack} on track");
            if (needsEncouragement > 0) outcomeParts.Add($"{needsEncouragement} could use encouragement");
            if (needsAttention > 0) outcomeParts.Add($"{needsAttention} need{(needsAttention == 1 ? "s" : "")} attention");

            var summary = $"Class completed with {engagement.Count} participant{(engagement.Count == 1 ? "" : "s")} — " +
                $"average engagement score {avgScore}/100 ({string.Join(", ", outcomeParts)}).";

            var quizAttempts = engagement.Sum(e => e.QuizAttempts);
            if (quizAttempts > 0)
            {
                summary += $" {engagement.Sum(e => e.QuizCorrect)}/{quizAttempts} quiz answers correct.";
            }

            // Durable "was the whiteboard actually captured" signal (see
            // EngagementEventType.ScreenShareSeconds) -- silent when zero rather than warning,
            // since most sessions predate this feature and haven't adopted it yet; a warning
            // on every one of those would be noise, not signal.
            var screenShareSeconds = engagement.Sum(e => e.ScreenShareSeconds);
            if (screenShareSeconds > 0)
            {
                var screenShareMinutes = Math.Max(1, screenShareSeconds / 60);
                summary += $" Screen-shared for ~{screenShareMinutes} min — the recording should include the whiteboard.";
            }

            return summary;
        }

        private async Task NotifyAdminsOfTeacherNoShowAsync(ClassSession session, CancellationToken cancellationToken)
        {
            var admins = await _unitOfWork.Repository<User>().Query()
                .Where(u => u.Role == UserRole.Admin && u.Status == UserStatus.Active)
                .ToListAsync(cancellationToken);

            foreach (var admin in admins)
            {
                await _notificationService.SendTemplatedEmailAsync(
                    admin.Id,
                    admin.Email,
                    NotificationType.NoShowAlert,
                    "teacher-noshow-alert",
                    new Dictionary<string, string> { ["StartAtLocal"] = DateTimeDisplay.ToLocal(session.ScheduledStartAtUtc, admin.TimeZoneId) },
                    cancellationToken);
            }
        }

        /// <summary>One-time alert once a no-show chain hits MaxAutoCarryForwards and stops
        /// auto-rescheduling itself — sent for either party, unlike the teacher-only alert
        /// above, since a chain this long is itself the problem worth a human's attention
        /// (an abandoned lead, a batch that should have been archived) regardless of which
        /// side each individual week's no-show was attributed to.</summary>
        private async Task NotifyAdminsOfStalledNoShowChainAsync(ClassSession session, CancellationToken cancellationToken)
        {
            var admins = await _unitOfWork.Repository<User>().Query()
                .Where(u => u.Role == UserRole.Admin && u.Status == UserStatus.Active)
                .ToListAsync(cancellationToken);

            foreach (var admin in admins)
            {
                await _notificationService.SendTemplatedEmailAsync(
                    admin.Id,
                    admin.Email,
                    NotificationType.NoShowAlert,
                    "noshow-chain-stalled-alert",
                    new Dictionary<string, string>
                    {
                        ["StartAtLocal"] = DateTimeDisplay.ToLocal(session.ScheduledStartAtUtc, admin.TimeZoneId),
                        ["CarryForwardCount"] = (session.CarryForwardCount + 1).ToString(),
                    },
                    cancellationToken);
            }
        }

        private static SessionRecordingDto ToRecordingDto(SessionRecording recording)
        {
            return new SessionRecordingDto
            {
                Id = recording.Id,
                ClassSessionId = recording.ClassSessionId,
                StorageUrl = recording.StorageUrl,
                DurationSeconds = recording.DurationSeconds,
                ExpiresAtUtc = recording.ExpiresAtUtc,
                CreatedAtUtc = recording.CreatedAtUtc,
            };
        }

        private IQueryable<ClassSession> BaseQuery()
        {
            return _unitOfWork.Repository<ClassSession>().Query()
                .Include(s => s.Batch)
                .Include(s => s.TeacherProfile).ThenInclude(t => t.User);
        }

        /// <summary>
        /// Shared by ScheduleAsync and RescheduleAsync — both put a class on the calendar at
        /// a specific instant, so neither has a legitimate reason to land in the past. (Batch
        /// schedule generation doesn't go through this — it always projects forward from a
        /// start date — so a genuine historical backfill, if one is ever needed, isn't blocked
        /// by this check; it just isn't reachable through either of these two actions.)
        /// </summary>
        private static void ValidateWindow(DateTime startUtc, DateTime endUtc)
        {
            if (endUtc <= startUtc)
            {
                throw new DomainValidationException("Session end time must be after the start time.");
            }

            // A small grace window, not an exact "> now": a request that was valid when the
            // admin clicked submit shouldn't fail on submission-lag alone.
            if (startUtc < DateTime.UtcNow.AddMinutes(-5))
            {
                throw new DomainValidationException("Session start time cannot be in the past.");
            }
        }

        /// <summary>Business rule: no class is ever scheduled — or rescheduled — onto a holiday.</summary>
        private async Task EnsureNotHolidayAsync(DateTime startUtc, CancellationToken cancellationToken)
        {
            // Holiday.Date is an org-wide calendar date meant in local (DefaultTimeZoneId,
            // Asia/Kolkata) terms, not UTC — DateOnly.FromDateTime(startUtc) truncated the raw
            // UTC instant instead, which is off by a full calendar day for any session starting
            // in the 00:00-05:29 IST window (18:30-23:59 UTC the PRIOR day): a session actually
            // on the holiday would compute the day before it and slip past this check entirely.
            // Mirrors StoreService.ListAvailableDemoSlotsAsync's own correct conversion.
            var zone = TimeZoneInfo.FindSystemTimeZoneById(DateTimeDisplay.DefaultTimeZoneId);
            var sessionDate = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(startUtc, zone));
            var holiday = await _unitOfWork.Repository<Holiday>()
                .FirstOrDefaultAsync(h => h.Date == sessionDate, cancellationToken);
            if (holiday is not null)
            {
                throw new DomainValidationException(
                    $"No class can be scheduled on {sessionDate:yyyy-MM-dd} — it's a holiday ({holiday.Name}). Pick a different date.");
            }
        }
    }
}
