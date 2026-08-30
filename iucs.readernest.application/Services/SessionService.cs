using iucs.readernest.application.Common.Exceptions;
using iucs.readernest.application.Common.Interfaces;
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

        public SessionService(
            IUnitOfWork unitOfWork,
            IAuditLogService auditLog,
            IPayoutService payoutService,
            INotificationService notificationService,
            ICurrentUserService currentUser,
            IJitsiTokenService jitsiTokenService)
        {
            _unitOfWork = unitOfWork;
            _auditLog = auditLog;
            _payoutService = payoutService;
            _notificationService = notificationService;
            _currentUser = currentUser;
            _jitsiTokenService = jitsiTokenService;
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
            return sessions.Select(s => s.ToDto()).ToList();
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

            return session.ToDto();
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

            var session = new ClassSession
            {
                BatchId = request.BatchId,
                TeacherProfileId = request.TeacherProfileId,
                Type = request.Type,
                ScheduledStartAtUtc = request.ScheduledStartAtUtc,
                ScheduledEndAtUtc = request.ScheduledEndAtUtc,
                // One-click join: the room id is generated, never a manual meeting link
                MeetingRoomId = $"trn-{Guid.NewGuid():N}",
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

            await EnsureTeacherIsFreeAsync(
                original.TeacherProfileId, request.ScheduledStartAtUtc, request.ScheduledEndAtUtc,
                cancellationToken, excludeSessionId: original.Id);

            original.Status = SessionStatus.Rescheduled;

            // A reschedule is a new calendar entry linked to the original,
            // so history and colour coding stay traceable.
            var replacement = new ClassSession
            {
                BatchId = original.BatchId,
                TeacherProfileId = original.TeacherProfileId,
                Type = original.Type,
                ScheduledStartAtUtc = request.ScheduledStartAtUtc,
                ScheduledEndAtUtc = request.ScheduledEndAtUtc,
                MeetingRoomId = original.MeetingRoomId,
                RescheduledFromSessionId = original.Id,
            };
            await _unitOfWork.Repository<ClassSession>().AddAsync(replacement, cancellationToken);
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

            // Performance summary: the teacher's class notes go straight to the batch's parents
            if (!string.IsNullOrWhiteSpace(session.Summary) && session.BatchId.HasValue)
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
            };
            await _unitOfWork.Repository<ClassSession>().AddAsync(carriedForward, cancellationToken);

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
            var session = await _unitOfWork.Repository<ClassSession>().Query()
                .FirstOrDefaultAsync(s => s.MeetingRoomId == roomName, cancellationToken);
            if (session is null)
            {
                return null;
            }

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

            var weekdays = request.DaysOfWeek.Distinct().ToHashSet();
            var holidays = (await _unitOfWork.Repository<Holiday>().Query()
                    .Select(h => h.Date)
                    .ToListAsync(cancellationToken))
                .ToHashSet();

            var sessionRepository = _unitOfWork.Repository<ClassSession>();
            var date = request.StartDate;
            var created = 0;
            DateOnly? lastDate = null;

            // Walk the calendar until every course session is placed; hard cap
            // of two years guards against a weekday set that never matches.
            var safetyLimit = request.StartDate.AddYears(2);
            while (created < course.TotalSessions && date < safetyLimit)
            {
                if (weekdays.Contains(date.DayOfWeek) && !holidays.Contains(date))
                {
                    var startUtc = date.ToDateTime(request.StartTimeUtc, DateTimeKind.Utc);
                    await EnsureTeacherIsFreeAsync(
                        batch.TeacherProfileId, startUtc, startUtc.AddMinutes(course.DurationMinutes), cancellationToken);
                    await sessionRepository.AddAsync(
                        new ClassSession
                        {
                            BatchId = batch.Id,
                            TeacherProfileId = batch.TeacherProfileId,
                            ScheduledStartAtUtc = startUtc,
                            ScheduledEndAtUtc = startUtc.AddMinutes(course.DurationMinutes),
                            MeetingRoomId = $"trn-{Guid.NewGuid():N}",
                        },
                        cancellationToken);
                    created++;
                    lastDate = date;
                }

                date = date.AddDays(1);
            }

            if (created < course.TotalSessions)
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
                return await _unitOfWork.Repository<BatchEnrollment>().Query()
                    .Where(e => e.BatchId == session.BatchId.Value && e.Status == EnrollmentStatus.Active)
                    .Join(_unitOfWork.Repository<Child>().Query(), e => e.ChildId, c => c.Id, (e, c) => c.ParentProfileId)
                    .Join(_unitOfWork.Repository<ParentProfile>().Query(), parentProfileId => parentProfileId, p => p.Id, (parentProfileId, p) => p.UserId)
                    .AnyAsync(u => u == userId, cancellationToken);
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
                    p => p.UserId == userId && p.Module == PermissionModule.SessionCalendarManagement && p.CanEdit,
                    cancellationToken);
            }

            return false;
        }

        public async Task<JitsiJoinDto> GetJitsiJoinAsync(Guid sessionId, Guid userId, CancellationToken cancellationToken = default)
        {
            var session = await _unitOfWork.Repository<ClassSession>().GetByIdAsync(sessionId, cancellationToken)
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
            var now = DateTime.UtcNow;
            if (now < session.ScheduledStartAtUtc.AddMinutes(-10))
            {
                throw new DomainValidationException("This class hasn't opened for joining yet.");
            }
            if (now > session.ScheduledEndAtUtc)
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

            return new JitsiJoinDto { Room = session.MeetingRoomId, Domain = domain, Token = token };
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
            };
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
