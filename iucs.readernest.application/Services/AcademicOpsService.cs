using iucs.readernest.application.Common;
using iucs.readernest.application.Common.Exceptions;
using iucs.readernest.application.Dto.Academics;
using iucs.readernest.application.Helper;
using iucs.readernest.domain.Common;
using iucs.readernest.domain.Entities.Academics;
using iucs.readernest.domain.Entities.Admission;
using iucs.readernest.domain.Entities.Sessions;
using iucs.readernest.domain.Entities.Users;
using iucs.readernest.domain.Enums;
using iucs.readernest.domain.Repository;
using Microsoft.EntityFrameworkCore;

namespace iucs.readernest.application.Services
{
    public class AcademicOpsService : IAcademicOpsService
    {
        private static readonly TimeSpan LeaveCutoff = TimeSpan.FromHours(6);

        private readonly IUnitOfWork _unitOfWork;
        private readonly IAuditLogService _auditLog;
        private readonly INotificationService _notificationService;
        private readonly ICurrentUserService _currentUser;
        private readonly ISessionService _sessionService;
        private readonly IClassSessionEventLogService _eventLog;

        public AcademicOpsService(
            IUnitOfWork unitOfWork,
            IAuditLogService auditLog,
            INotificationService notificationService,
            ICurrentUserService currentUser,
            ISessionService sessionService,
            IClassSessionEventLogService eventLog)
        {
            _unitOfWork = unitOfWork;
            _auditLog = auditLog;
            _notificationService = notificationService;
            _currentUser = currentUser;
            _sessionService = sessionService;
            _eventLog = eventLog;
        }

        /// <summary>
        /// Attendance is written and read per session by "Admin or Teacher", but that role gate
        /// alone lets any teacher touch any class they can name an id for. Same participation
        /// rule the live classroom and engagement endpoints already use: Admin passes, a teacher
        /// only for the session they are assigned to.
        /// </summary>
        private async Task EnsureSessionParticipantAsync(Guid sessionId, CancellationToken cancellationToken)
        {
            var userId = _currentUser.UserId
                ?? throw new UnauthorizedException("Not signed in.");

            if (!await _sessionService.IsSessionParticipantAsync(sessionId, userId, cancellationToken))
            {
                throw new ForbiddenException("You do not have access to this session.");
            }
        }

        /// <summary>
        /// Join-based attendance capture — the PDF's "System Marks Attendance" step, fired by
        /// ClassroomHub.JoinSession whenever a genuine session participant connects to the live
        /// classroom. A Teacher captures against their own TeacherProfile; a Parent captures
        /// against whichever of their children are actively enrolled in the session's batch
        /// (normally one, occasionally more for siblings sharing a batch). Admin/SubAdmin joins
        /// are monitoring, not attendance, and are silently skipped, as is any session whose
        /// status doesn't currently accept attendance (mirrors CaptureAttendanceAsync's own
        /// guard). Never throws — a capture hiccup must never stop someone from joining their
        /// own class; the hub calls this best-effort after the join itself has already succeeded.
        /// </summary>
        public async Task CaptureJoinAttendanceAsync(Guid sessionId, Guid userId, CancellationToken cancellationToken = default)
        {
            try
            {
                var user = await _unitOfWork.Repository<User>().GetByIdAsync(userId, cancellationToken);
                if (user is null)
                {
                    return;
                }

                var entries = new List<AttendanceEntryDto>();
                var session = await _unitOfWork.Repository<ClassSession>().GetByIdAsync(sessionId, cancellationToken);
                if (session is null)
                {
                    return;
                }

                if (user.Role == UserRole.Teacher)
                {
                    // Not just "is a teacher" — must be THIS session's own assigned teacher. In
                    // production the hub's JoinSession already guarantees this before ever
                    // calling in here, but this method has to be correct standing on its own too.
                    var isAssignedTeacher = await _unitOfWork.Repository<TeacherProfile>()
                        .ExistsAsync(t => t.Id == session.TeacherProfileId && t.UserId == userId, cancellationToken);
                    if (isAssignedTeacher)
                    {
                        // A network drop followed by SignalR's automatic reconnect calls this
                        // again for the SAME class — CaptureAttendanceCoreAsync's merge always
                        // overwrites JoinedAtUtc with "now" (a real value beats the ?? fallback)
                        // but leaves the stale LeftAtUtc the earlier disconnect wrote untouched,
                        // so the row ends up with a leave time BEFORE its join time. That fed a
                        // negative "attended minutes" into the payout review check, which then
                        // flagged a teacher who taught almost the entire class as if they'd left
                        // after a moment, purely because of a brief reconnect. On a genuine
                        // rejoin (a row already exists), keep the original JoinedAtUtc and clear
                        // the now-stale LeftAtUtc directly instead of going through the entries
                        // path at all — the teacher is back, they haven't "left" this session yet.
                        var existingTeacherRow = await _unitOfWork.Repository<SessionAttendance>().TrackedQuery()
                            .FirstOrDefaultAsync(a => a.ClassSessionId == sessionId && a.TeacherProfileId == session.TeacherProfileId, cancellationToken);
                        var isReconnect = existingTeacherRow is not null;

                        if (isReconnect)
                        {
                            existingTeacherRow!.Status = AttendanceStatus.Present;
                            existingTeacherRow.LeftAtUtc = null;
                            await _unitOfWork.SaveChangesAsync(cancellationToken);
                        }
                        else
                        {
                            entries.Add(new AttendanceEntryDto
                            {
                                TeacherProfileId = session.TeacherProfileId,
                                Status = AttendanceStatus.Present,
                                JoinedAtUtc = DateTime.UtcNow,
                            });

                            // The real "class started" moment — nothing else in this app ever
                            // flips a session to InProgress or stamps ActualStartAtUtc from the
                            // teacher's own actual arrival; CompleteAsync's own ??= fallback only
                            // covers a session that never got here at all. `session` is the same
                            // tracked entity for this request, so this rides whichever
                            // SaveChangesAsync call happens to run next (the event-log write below
                            // included).
                            if (session.Status is SessionStatus.Scheduled or SessionStatus.CarriedForward)
                            {
                                session.Status = SessionStatus.InProgress;
                            }
                            session.ActualStartAtUtc ??= DateTime.UtcNow;
                        }

                        await _eventLog.LogTeacherJoinAsync(
                            session, session.TeacherProfileId, userId,
                            $"{user.FirstName} {user.LastName}".Trim(), isReconnect, cancellationToken);
                    }
                }
                else if (user.Role == UserRole.Parent)
                {
                    if (session.BatchId is Guid batchId)
                    {
                        var children = await _unitOfWork.Repository<BatchEnrollment>().Query()
                            .Where(e => e.BatchId == batchId && e.Status == EnrollmentStatus.Active)
                            .Join(_unitOfWork.Repository<Child>().Query(), e => e.ChildId, c => c.Id, (e, c) => c)
                            .Join(_unitOfWork.Repository<ParentProfile>().Query(), c => c.ParentProfileId, p => p.Id, (c, p) => new { c.Id, c.FirstName, c.LastName, p.UserId })
                            .Where(x => x.UserId == userId)
                            .ToListAsync(cancellationToken);

                        entries.AddRange(children.Select(c => new AttendanceEntryDto
                        {
                            ChildId = c.Id,
                            Status = AttendanceStatus.Present,
                            JoinedAtUtc = DateTime.UtcNow,
                        }));

                        if (children.Count > 0)
                        {
                            // One rejoin check covering every enrolled child on this join, rather
                            // than a query per child — usually one row (siblings sharing a batch
                            // are the exception), bounded either way by class size.
                            var childIds = children.Select(c => c.Id).ToList();
                            var previouslyJoinedChildIds = (await _unitOfWork.Repository<SessionAttendance>().Query()
                                    .Where(a => a.ClassSessionId == sessionId && a.ChildId != null && childIds.Contains(a.ChildId!.Value))
                                    .Select(a => a.ChildId!.Value)
                                    .ToListAsync(cancellationToken))
                                .ToHashSet();

                            foreach (var child in children)
                            {
                                await _eventLog.LogStudentJoinAsync(
                                    session, child.Id, userId, $"{child.FirstName} {child.LastName}".Trim(),
                                    isReconnect: previouslyJoinedChildIds.Contains(child.Id), cancellationToken);
                            }
                        }
                    }
                    else if (!string.IsNullOrWhiteSpace(user.Email))
                    {
                        // Demo session (no batch): there is no Child/BatchEnrollment row to hang
                        // a SessionAttendance entry off of (that table requires a real Child or
                        // TeacherProfile FK — see SessionAttendance's own doc comment), so this
                        // is recorded directly on the booking instead. Returns here rather than
                        // falling into the entries-based path below, which has nothing to do for
                        // a demo join.
                        await CaptureDemoJoinAsync(session.Id, user.Email, cancellationToken);
                        return;
                    }
                }

                if (entries.Count == 0)
                {
                    return;
                }

                await CaptureAttendanceCoreAsync(sessionId, new CaptureAttendanceRequest { Entries = entries }, cancellationToken);
            }
            catch
            {
                // Best-effort: joining the class must never fail because attendance capture did.
            }
        }

        /// <summary>
        /// Teacher-departure capture — fired by ClassroomHub on LeaveSession/OnDisconnectedAsync.
        /// The hub's own leave/disconnect handling only ever touched in-memory presence state
        /// (Rooms/Scores/_presenceTracker); SessionAttendance.LeftAtUtc was never actually written
        /// anywhere, so a teacher who joined then left after a few minutes of a much longer class
        /// looked identical to one who taught the whole thing. Only handles the teacher side (the
        /// side that drives payout accuracy) — parent/child leave capture is a separate concern.
        /// Same never-throw contract as the join capture: a disconnect must never fail because
        /// this write did.
        /// </summary>
        public async Task CaptureLeaveAttendanceAsync(Guid sessionId, Guid userId, CancellationToken cancellationToken = default)
        {
            try
            {
                var session = await _unitOfWork.Repository<ClassSession>().GetByIdAsync(sessionId, cancellationToken);
                if (session is null)
                {
                    return;
                }

                var isAssignedTeacher = await _unitOfWork.Repository<TeacherProfile>()
                    .ExistsAsync(t => t.Id == session.TeacherProfileId && t.UserId == userId, cancellationToken);
                if (!isAssignedTeacher)
                {
                    return;
                }

                await CaptureAttendanceCoreAsync(
                    sessionId,
                    new CaptureAttendanceRequest
                    {
                        Entries =
                        [
                            new AttendanceEntryDto
                            {
                                TeacherProfileId = session.TeacherProfileId,
                                Status = AttendanceStatus.Present,
                                LeftAtUtc = DateTime.UtcNow,
                            },
                        ],
                    },
                    cancellationToken);
            }
            catch
            {
                // Best-effort: a disconnect must never fail because attendance capture did.
            }
        }

        /// <summary>
        /// Demo-session counterpart of the SessionAttendance path above: matches the joining
        /// account's email (case-insensitive) against the booking's primary contact
        /// (<see cref="DemoBooking.ParentEmail"/>) or an additional invitee
        /// (<see cref="DemoParticipant.Email"/>), and timestamps/flags whichever matched. A
        /// demo lead has no account requirement, so a parent who registered and joined under a
        /// different email than the one the demo was booked with is a silent no-op — same
        /// "never block the join" contract as the rest of this method.
        /// </summary>
        private async Task CaptureDemoJoinAsync(Guid sessionId, string email, CancellationToken cancellationToken)
        {
            var booking = await _unitOfWork.Repository<DemoBooking>().TrackedQuery()
                .Include(b => b.Participants)
                .FirstOrDefaultAsync(b => b.ClassSessionId == sessionId, cancellationToken);
            if (booking is null)
            {
                return;
            }

            var matched = false;
            var joinedNames = new List<string>();
            if (string.Equals(booking.ParentEmail, email, StringComparison.OrdinalIgnoreCase))
            {
                var wasAlreadyJoined = booking.ParentJoinedAtUtc.HasValue;
                booking.ParentJoinedAtUtc ??= DateTime.UtcNow;
                matched = true;
                if (!wasAlreadyJoined)
                {
                    joinedNames.Add(booking.ParentName);
                }
            }

            foreach (var participant in booking.Participants.Where(
                p => p.Email != null && string.Equals(p.Email, email, StringComparison.OrdinalIgnoreCase)))
            {
                if (!participant.HasJoined)
                {
                    joinedNames.Add(participant.Name);
                }
                participant.HasJoined = true;
                matched = true;
            }

            if (matched)
            {
                await _unitOfWork.SaveChangesAsync(cancellationToken);
            }

            // Logged after every real match, including a rejoin of an already-marked-joined
            // participant (joinedNames stays empty then, which is fine — a rejoin isn't
            // itself a notable event for a demo lead the way it is for the teacher/student
            // attendance flows above).
            foreach (var name in joinedNames)
            {
                await _eventLog.LogDemoParticipantJoinAsync(sessionId, name, cancellationToken);
            }
        }

        public async Task<IReadOnlyList<HolidayDto>> ListHolidaysAsync(CancellationToken cancellationToken = default)
        {
            var holidays = await _unitOfWork.Repository<Holiday>().Query()
                .OrderBy(h => h.Date)
                .ToListAsync(cancellationToken);
            return holidays.Select(ToDto).ToList();
        }

        public async Task<HolidayDto> CreateHolidayAsync(SaveHolidayRequest request, CancellationToken cancellationToken = default)
        {
            var exists = await _unitOfWork.Repository<Holiday>().ExistsAsync(h => h.Date == request.Date, cancellationToken);
            if (exists)
            {
                throw new ConflictException($"A holiday already exists on {request.Date:yyyy-MM-dd}.");
            }

            var holiday = new Holiday { Date = request.Date, Name = request.Name.Trim(), Description = request.Description };
            await _unitOfWork.Repository<Holiday>().AddAsync(holiday, cancellationToken);
            await _auditLog.StageAsync(AuditAction.Create, nameof(Holiday), holiday.Id.ToString(), cancellationToken: cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            // Business rule: a class never runs on a holiday. Any session already scheduled
            // on this date is automatically carried forward to the next available same-weekday
            // slot (skipping further holidays), keeping the traceability link.
            //
            // request.Date is a local (DefaultTimeZoneId, Asia/Kolkata) calendar date, but
            // treating its midnight as UTC midnight offset the whole matching window by +5:30 —
            // a session genuinely on the holiday (e.g. 02:00 IST) fell just before the window
            // and was never detected, running as normal, while a session the FOLLOWING day in
            // that same 00:00-05:29 IST slot fell just inside it and was wrongly auto-cancelled
            // and carried forward a week as if it were on the holiday. Mirrors
            // StoreService.ListAvailableDemoSlotsAsync's own correct local-to-UTC conversion.
            var zone = TimeZoneInfo.FindSystemTimeZoneById(DateTimeDisplay.DefaultTimeZoneId);
            var dayStartLocal = request.Date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
            var dayStart = TimeZoneInfo.ConvertTimeToUtc(dayStartLocal, zone);
            var dayEnd = TimeZoneInfo.ConvertTimeToUtc(dayStartLocal.AddDays(1), zone);
            var clashingSessions = await _unitOfWork.Repository<ClassSession>().TrackedQuery()
                .Where(s => (s.Status == SessionStatus.Scheduled || s.Status == SessionStatus.CarriedForward)
                            && s.ScheduledStartAtUtc >= dayStart
                            && s.ScheduledStartAtUtc < dayEnd)
                .ToListAsync(cancellationToken);

            // The next free same-weekday slot depends only on the holiday calendar, not on
            // any individual session, so it is computed once here rather than re-derived
            // inside the loop below — that probe was issuing a fresh ExistsAsync per session
            // per candidate week while always arriving at the same answer.
            var futureHolidayDates = (await _unitOfWork.Repository<Holiday>().Query()
                    .Where(h => h.Date > request.Date)
                    .Select(h => h.Date)
                    .ToListAsync(cancellationToken))
                .ToHashSet();

            var offsetDays = 7;
            while (futureHolidayDates.Contains(request.Date.AddDays(offsetDays)))
            {
                offsetDays += 7;
            }

            foreach (var session in clashingSessions)
            {
                await _unitOfWork.Repository<ClassSession>().AddAsync(
                    new ClassSession
                    {
                        BatchId = session.BatchId,
                        TeacherProfileId = session.TeacherProfileId,
                        Type = session.Type,
                        Status = SessionStatus.CarriedForward,
                        ScheduledStartAtUtc = session.ScheduledStartAtUtc.AddDays(offsetDays),
                        ScheduledEndAtUtc = session.ScheduledEndAtUtc.AddDays(offsetDays),
                        MeetingRoomId = session.MeetingRoomId,
                        CarriedForwardFromSessionId = session.Id,
                    },
                    cancellationToken);

                session.Status = SessionStatus.Cancelled;
                session.CancellationReason = $"Holiday — {holiday.Name}; carried forward to {request.Date.AddDays(offsetDays):yyyy-MM-dd}";
            }

            if (clashingSessions.Count > 0)
            {
                await _auditLog.StageAsync(AuditAction.Update, nameof(ClassSession), null,
                    changesJson: $"{{\"holidayCarryForward\":{clashingSessions.Count}}}", cancellationToken: cancellationToken);
                await _unitOfWork.SaveChangesAsync(cancellationToken);
            }

            return ToDto(holiday);
        }

        public async Task DeleteHolidayAsync(Guid id, CancellationToken cancellationToken = default)
        {
            var holiday = await _unitOfWork.Repository<Holiday>().GetByIdAsync(id, cancellationToken)
                ?? throw new NotFoundException(nameof(Holiday), id);

            _unitOfWork.Repository<Holiday>().Remove(holiday);
            await _auditLog.StageAsync(AuditAction.Delete, nameof(Holiday), id.ToString(), cancellationToken: cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }

        public async Task<LeaveRequestDto> SubmitLeaveAsync(
            Guid teacherUserId,
            SubmitLeaveRequest request,
            CancellationToken cancellationToken = default)
        {
            var hasWindow = request.StartAtUtc.HasValue || request.EndAtUtc.HasValue;
            var hasSessions = request.SessionIds is { Count: > 0 };

            // Exactly one shape per request — a window XOR a specific session list. Silently
            // preferring one over the other if a caller somehow sent both would hide which
            // leave the teacher actually meant to apply for.
            if (hasWindow && hasSessions)
            {
                throw new DomainValidationException("Choose either specific classes or a date/time range, not both.");
            }

            if (hasSessions)
            {
                return await SubmitClassWiseLeaveAsync(teacherUserId, request.SessionIds!, request.Reason, cancellationToken);
            }

            if (!request.StartAtUtc.HasValue || !request.EndAtUtc.HasValue)
            {
                throw new DomainValidationException("Provide either specific classes to cancel or a start and end time.");
            }

            return await SubmitWindowLeaveAsync(teacherUserId, request.StartAtUtc.Value, request.EndAtUtc.Value, request.Reason, cancellationToken);
        }

        /// <summary>Whole day/date-range leave — the original behaviour, unchanged. Every
        /// Scheduled session inside [startAtUtc, endAtUtc) is affected.</summary>
        private async Task<LeaveRequestDto> SubmitWindowLeaveAsync(
            Guid teacherUserId,
            DateTime startAtUtc,
            DateTime endAtUtc,
            string reason,
            CancellationToken cancellationToken)
        {
            if (endAtUtc <= startAtUtc)
            {
                throw new DomainValidationException("Leave end time must be after the start time.");
            }

            var teacher = await _unitOfWork.Repository<TeacherProfile>().Query()
                .Include(t => t.User)
                .FirstOrDefaultAsync(t => t.UserId == teacherUserId, cancellationToken)
                ?? throw new NotFoundException("No teacher profile is linked to the current account.");

            // Nothing stopped a teacher from submitting the same window (or overlapping ones)
            // more than once — each one independently counts as "affected sessions" for the
            // admin notification and, worse, an admin approving two overlapping ones would
            // cancel the same session(s) twice over with no indication anything was already
            // handled. Pending and Approved both still "hold" the window; Rejected/Cancelled
            // don't (the teacher is free to re-request the same time).
            var duplicate = await _unitOfWork.Repository<LeaveRequest>().Query()
                .Where(l => l.TeacherProfileId == teacher.Id
                            && (l.Status == LeaveStatus.Pending || l.Status == LeaveStatus.Approved)
                            && l.StartAtUtc < endAtUtc && l.EndAtUtc > startAtUtc)
                .OrderBy(l => l.StartAtUtc)
                .FirstOrDefaultAsync(cancellationToken);
            if (duplicate is not null)
            {
                throw new ConflictException(
                    $"You already have a {duplicate.Status.ToString().ToLowerInvariant()} leave request covering this time " +
                    $"({DateTimeDisplay.ToLocalRange(duplicate.StartAtUtc, duplicate.EndAtUtc)}).");
            }

            var affectedSessions = await CountAffectedSessionsAsync(teacher.Id, startAtUtc, endAtUtc, cancellationToken);

            // 6-hour rule: leave covering a session that starts within the cutoff is auto-blocked
            var cutoffLimit = DateTime.UtcNow.Add(LeaveCutoff);
            var blockingSession = await _unitOfWork.Repository<ClassSession>().Query()
                .Where(s => s.TeacherProfileId == teacher.Id
                            && s.Status == SessionStatus.Scheduled
                            && s.ScheduledStartAtUtc < cutoffLimit
                            && s.ScheduledStartAtUtc < endAtUtc
                            && s.ScheduledEndAtUtc > startAtUtc)
                .OrderBy(s => s.ScheduledStartAtUtc)
                .FirstOrDefaultAsync(cancellationToken);
            if (blockingSession is not null)
            {
                throw new DomainValidationException(
                    $"Leave cannot cover the session at {DateTimeDisplay.ToLocal(blockingSession.ScheduledStartAtUtc)}: applications must be made at least 6 hours before a scheduled class.");
            }

            var leave = new LeaveRequest
            {
                TeacherProfileId = teacher.Id,
                StartAtUtc = startAtUtc,
                EndAtUtc = endAtUtc,
                Reason = reason.Trim(),
            };
            await _unitOfWork.Repository<LeaveRequest>().AddAsync(leave, cancellationToken);
            await _auditLog.StageAsync(AuditAction.Create, nameof(LeaveRequest), leave.Id.ToString(), cancellationToken: cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            // Settings → Notifications → "Teacher leave requests" turns this alert off.
            if (await NotificationToggles.IsEnabledAsync(_unitOfWork, NotificationToggles.LeaveRequests, cancellationToken))
            {
                await NotifyAdminsAsync(
                    new Dictionary<string, string>
                    {
                        ["TeacherName"] = $"{teacher.User.FirstName} {teacher.User.LastName}".Trim(),
                        ["StartAtLocal"] = DateTimeDisplay.ToLocal(startAtUtc),
                        ["EndAtLocal"] = DateTimeDisplay.ToLocal(endAtUtc),
                        ["AffectedSessions"] = affectedSessions.ToString(),
                        ["Reason"] = reason,
                    },
                    cancellationToken);
            }

            leave.TeacherProfile = teacher;
            return await ToDtoAsync(leave, cancellationToken);
        }

        /// <summary>
        /// Class-wise leave (WBS Round 2 feedback #23): the teacher picks exactly which of
        /// her own scheduled sessions to cancel instead of taking leave for a whole day. When
        /// the pick fits inside her remaining monthly cancellation allowance (admin-configured,
        /// see LeaveAllowance) it's approved immediately — no admin review needed, since the
        /// allowance itself already represents institute-approved policy. Once the request
        /// would exceed what's left this month, it falls back to the existing Pending/admin-
        /// review workflow, same as a whole-day request, rather than silently blocking it.
        /// </summary>
        private async Task<LeaveRequestDto> SubmitClassWiseLeaveAsync(
            Guid teacherUserId,
            List<Guid> sessionIds,
            string reason,
            CancellationToken cancellationToken)
        {
            var distinctIds = sessionIds.Distinct().ToList();

            var teacher = await _unitOfWork.Repository<TeacherProfile>().Query()
                .Include(t => t.User)
                .FirstOrDefaultAsync(t => t.UserId == teacherUserId, cancellationToken)
                ?? throw new NotFoundException("No teacher profile is linked to the current account.");

            // TRACKED (not Query()/AsNoTracking): the auto-approve path below mutates these
            // sessions' Status directly and relies on SaveChangesAsync to persist it, same
            // pattern as ReviewLeaveAsync's own affectedSessions load.
            var sessions = await _unitOfWork.Repository<ClassSession>().TrackedQuery()
                .Include(s => s.Batch)
                .Where(s => distinctIds.Contains(s.Id))
                .ToListAsync(cancellationToken);

            if (sessions.Count != distinctIds.Count)
            {
                throw new NotFoundException("One or more selected classes could not be found.");
            }

            // Every session must be this teacher's own, still Scheduled — otherwise a teacher
            // could apply "leave" against a class that isn't hers, or one already resolved.
            var notOwnedOrNotScheduled = sessions.FirstOrDefault(
                s => s.TeacherProfileId != teacher.Id || s.Status != SessionStatus.Scheduled);
            if (notOwnedOrNotScheduled is not null)
            {
                throw new DomainValidationException(
                    $"The session at {DateTimeDisplay.ToLocal(notOwnedOrNotScheduled.ScheduledStartAtUtc)} isn't one of your own currently-scheduled classes.");
            }

            // Same "already applied for" guard as the whole-window path, extended to also catch
            // a session individually linked to an earlier class-wise request — a plain window
            // overlap check alone wouldn't see that link.
            var alreadyRequestedId = await _unitOfWork.Repository<LeaveRequestSession>().Query()
                .Where(ls => distinctIds.Contains(ls.ClassSessionId)
                             && (ls.LeaveRequest.Status == LeaveStatus.Pending || ls.LeaveRequest.Status == LeaveStatus.Approved))
                .Select(ls => (Guid?)ls.ClassSessionId)
                .FirstOrDefaultAsync(cancellationToken);
            if (alreadyRequestedId is not null)
            {
                throw new ConflictException("One of the selected classes already has a pending or approved leave request against it.");
            }
            var windowOverlap = await _unitOfWork.Repository<LeaveRequest>().Query()
                .Where(l => l.TeacherProfileId == teacher.Id
                            && !l.IsClassWise
                            && (l.Status == LeaveStatus.Pending || l.Status == LeaveStatus.Approved)
                            && sessions.Select(s => s.ScheduledStartAtUtc).Min() < l.EndAtUtc
                            && sessions.Select(s => s.ScheduledEndAtUtc).Max() > l.StartAtUtc)
                .AnyAsync(cancellationToken);
            if (windowOverlap)
            {
                throw new ConflictException("One of the selected classes already falls inside an existing pending or approved leave request.");
            }

            // 6-hour rule, same cutoff as the whole-window path, checked per selected session.
            var cutoffLimit = DateTime.UtcNow.Add(LeaveCutoff);
            var blockingSession = sessions
                .Where(s => s.ScheduledStartAtUtc < cutoffLimit)
                .OrderBy(s => s.ScheduledStartAtUtc)
                .FirstOrDefault();
            if (blockingSession is not null)
            {
                throw new DomainValidationException(
                    $"The class at {DateTimeDisplay.ToLocal(blockingSession.ScheduledStartAtUtc)} can't be cancelled this way: applications must be made at least 6 hours before a scheduled class.");
            }

            // The monthly allowance is keyed to the month each class was actually scheduled in
            // (not the month the teacher happens to be applying in) — a request picking classes
            // that span two calendar months must fit within whatever's left in EACH of those
            // months, checked independently.
            foreach (var monthGroup in sessions.GroupBy(s => (s.ScheduledStartAtUtc.Year, s.ScheduledStartAtUtc.Month)))
            {
                var remaining = await GetRemainingAllowanceAsync(teacher.Id, monthGroup.Key.Year, monthGroup.Key.Month, cancellationToken);
                if (monthGroup.Count() > remaining)
                {
                    // Not an error — falls back to the reviewed workflow below, same as a
                    // whole-day request always has.
                    return await CreatePendingClassWiseLeaveAsync(teacher, sessions, reason, cancellationToken);
                }
            }

            return await CreateAutoApprovedClassWiseLeaveAsync(teacher, sessions, reason, cancellationToken);
        }

        private async Task<LeaveRequestDto> CreateAutoApprovedClassWiseLeaveAsync(
            TeacherProfile teacher,
            List<ClassSession> sessions,
            string reason,
            CancellationToken cancellationToken)
        {
            var leave = new LeaveRequest
            {
                TeacherProfileId = teacher.Id,
                StartAtUtc = sessions.Min(s => s.ScheduledStartAtUtc),
                EndAtUtc = sessions.Max(s => s.ScheduledEndAtUtc),
                Reason = reason.Trim(),
                IsClassWise = true,
                Status = LeaveStatus.Approved,
                ReviewedAtUtc = DateTime.UtcNow,
                ReviewNote = "Auto-approved: within your monthly class-cancellation allowance.",
            };
            foreach (var session in sessions)
            {
                leave.Sessions.Add(new LeaveRequestSession { ClassSessionId = session.Id, ClassSession = session });
                session.Status = SessionStatus.Cancelled;
                session.CancellationReason =
                    $"Teacher self-cancelled via class-wise leave (within monthly allowance): {reason.Trim()}";
            }

            await _unitOfWork.Repository<LeaveRequest>().AddAsync(leave, cancellationToken);
            await _auditLog.StageAsync(AuditAction.Create, nameof(LeaveRequest), leave.Id.ToString(),
                changesJson: $"{{\"autoApproved\":true,\"sessionsCancelled\":{sessions.Count}}}", cancellationToken: cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            await NotifyClassWiseApprovalAsync(teacher, sessions.Count, cancellationToken);

            leave.TeacherProfile = teacher;
            var dto = await ToDtoAsync(leave, cancellationToken);
            dto.AffectedSessionCount = sessions.Count;
            return dto;
        }

        private async Task<LeaveRequestDto> CreatePendingClassWiseLeaveAsync(
            TeacherProfile teacher,
            List<ClassSession> sessions,
            string reason,
            CancellationToken cancellationToken)
        {
            var leave = new LeaveRequest
            {
                TeacherProfileId = teacher.Id,
                StartAtUtc = sessions.Min(s => s.ScheduledStartAtUtc),
                EndAtUtc = sessions.Max(s => s.ScheduledEndAtUtc),
                Reason = reason.Trim(),
                IsClassWise = true,
            };
            foreach (var session in sessions)
            {
                leave.Sessions.Add(new LeaveRequestSession { ClassSessionId = session.Id, ClassSession = session });
            }

            await _unitOfWork.Repository<LeaveRequest>().AddAsync(leave, cancellationToken);
            await _auditLog.StageAsync(AuditAction.Create, nameof(LeaveRequest), leave.Id.ToString(), cancellationToken: cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            if (await NotificationToggles.IsEnabledAsync(_unitOfWork, NotificationToggles.LeaveRequests, cancellationToken))
            {
                await NotifyAdminsAsync(
                    new Dictionary<string, string>
                    {
                        ["TeacherName"] = $"{teacher.User.FirstName} {teacher.User.LastName}".Trim(),
                        ["StartAtLocal"] = DateTimeDisplay.ToLocal(leave.StartAtUtc),
                        ["EndAtLocal"] = DateTimeDisplay.ToLocal(leave.EndAtUtc),
                        ["AffectedSessions"] = sessions.Count.ToString(),
                        ["Reason"] = $"{reason} (exceeds this month's remaining cancellation allowance — needs review)",
                    },
                    cancellationToken);
            }

            leave.TeacherProfile = teacher;
            var dto = await ToDtoAsync(leave, cancellationToken);
            dto.AffectedSessionCount = sessions.Count;
            return dto;
        }

        /// <summary>Teacher confirmation + the same core-team/parent fan-out an admin-approved
        /// leave sends (ReviewLeaveAsync), for the auto-approved class-wise path — an
        /// auto-approval is still a real approval, so it must tell the same people.</summary>
        private async Task NotifyClassWiseApprovalAsync(TeacherProfile teacher, int cancelledCount, CancellationToken cancellationToken)
        {
            var teacherUser = teacher.User;
            await _notificationService.SendTemplatedEmailAsync(
                teacherUser.Id, teacherUser.Email, NotificationType.LeaveStatusUpdate, "leave-status-teacher",
                new Dictionary<string, string>
                {
                    ["TeacherFirstName"] = teacherUser.FirstName,
                    ["StartAtLocal"] = $"{cancelledCount} class(es)",
                    ["EndAtLocal"] = "",
                    ["Status"] = "Approved",
                    ["ReviewNote"] = "Note: Auto-approved — within your monthly class-cancellation allowance.",
                },
                cancellationToken);

            var teacherName = $"{teacherUser.FirstName} {teacherUser.LastName}".Trim();
            var coreTeam = await _unitOfWork.Repository<User>().Query()
                .Where(u => (u.Role == UserRole.Admin || u.Role == UserRole.SubAdmin) && u.Status == UserStatus.Active)
                .ToListAsync(cancellationToken);
            foreach (var member in coreTeam)
            {
                await _notificationService.SendTemplatedEmailAsync(
                    member.Id, member.Email, NotificationType.LeaveStatusUpdate, "leave-notify-core-team",
                    new Dictionary<string, string> { ["TeacherName"] = teacherName, ["Window"] = $"{cancelledCount} individual class(es), self-cancelled within allowance" },
                    cancellationToken);
            }

            var affectedParents = await _unitOfWork.Repository<BatchEnrollment>().Query()
                .Where(e => e.Status == EnrollmentStatus.Active && e.Batch.TeacherProfileId == teacher.Id)
                .Select(e => e.Child.ParentProfile.User)
                .Distinct()
                .ToListAsync(cancellationToken);
            foreach (var parent in affectedParents)
            {
                await _notificationService.SendTemplatedEmailAsync(
                    parent.Id, parent.Email, NotificationType.LeaveStatusUpdate, "leave-notify-parent",
                    new Dictionary<string, string> { ["TeacherName"] = teacherName, ["Window"] = $"{cancelledCount} individual class(es)" },
                    cancellationToken);
            }
        }

        /// <summary>
        /// Teacher withdraws their own leave request — the only way to get out of a mistaken or
        /// no-longer-needed application before this, was to email an admin and have them Reject
        /// it, which recorded it as rejected rather than what actually happened. Only the
        /// request's own teacher can cancel it, and only while it's still Pending — once an
        /// admin has acted (Approved/Rejected), that decision stands.
        /// </summary>
        public async Task CancelLeaveAsync(Guid teacherUserId, Guid leaveId, CancellationToken cancellationToken = default)
        {
            var teacher = await _unitOfWork.Repository<TeacherProfile>()
                .FirstOrDefaultAsync(t => t.UserId == teacherUserId, cancellationToken)
                ?? throw new NotFoundException("No teacher profile is linked to the current account.");

            var leave = await _unitOfWork.Repository<LeaveRequest>()
                .FirstOrDefaultAsync(l => l.Id == leaveId, cancellationToken)
                ?? throw new NotFoundException(nameof(LeaveRequest), leaveId);

            // Not this teacher's own request: NotFound rather than Forbidden, so this endpoint
            // never confirms/denies that some other teacher's leave request even exists.
            if (leave.TeacherProfileId != teacher.Id)
            {
                throw new NotFoundException(nameof(LeaveRequest), leaveId);
            }

            if (leave.Status != LeaveStatus.Pending)
            {
                throw new DomainValidationException($"This leave application is already {leave.Status} and can no longer be cancelled.");
            }

            leave.Status = LeaveStatus.Cancelled;
            await _auditLog.StageAsync(AuditAction.Update, nameof(LeaveRequest), leave.Id.ToString(), cancellationToken: cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }

        public async Task<IReadOnlyList<LeaveAllowanceDto>> ListLeaveAllowancesAsync(CancellationToken cancellationToken = default)
        {
            var allowances = await _unitOfWork.Repository<LeaveAllowance>().Query()
                .Include(a => a.TeacherProfile).ThenInclude(t => t!.User)
                .OrderBy(a => a.TeacherProfileId == null ? 0 : 1)
                .ThenBy(a => a.TeacherProfile != null ? a.TeacherProfile.User.FirstName : null)
                .ToListAsync(cancellationToken);
            return allowances.Select(ToDto).ToList();
        }

        public async Task<LeaveAllowanceDto> SetLeaveAllowanceAsync(
            SaveLeaveAllowanceRequest request,
            CancellationToken cancellationToken = default)
        {
            // Not trusted from the DTO's own [Range] alone (same reasoning as PayoutService.
            // SetRateAsync) — a negative allowance would let CountClassWiseCancellationsThisMonthAsync's
            // "remaining = allowance - used" go negative and, worse, a request with a negative
            // remaining calculated below still isn't what blocks anything (0 already blocks the
            // auto-approve fast path), so this exists purely to reject an obviously-wrong value
            // outright rather than silently store one nothing can ever satisfy.
            if (request.MonthlyAllowance < 0)
            {
                throw new DomainValidationException("Monthly allowance cannot be negative.");
            }

            if (request.TeacherProfileId is { } teacherProfileId)
            {
                var teacherExists = await _unitOfWork.Repository<TeacherProfile>()
                    .ExistsAsync(t => t.Id == teacherProfileId, cancellationToken);
                if (!teacherExists)
                {
                    throw new NotFoundException(nameof(TeacherProfile), teacherProfileId);
                }
            }

            var allowance = await _unitOfWork.Repository<LeaveAllowance>()
                .FirstOrDefaultAsync(a => a.TeacherProfileId == request.TeacherProfileId, cancellationToken);
            if (allowance is null)
            {
                allowance = new LeaveAllowance { TeacherProfileId = request.TeacherProfileId };
                await _unitOfWork.Repository<LeaveAllowance>().AddAsync(allowance, cancellationToken);
            }

            allowance.MonthlyAllowance = request.MonthlyAllowance;

            await _auditLog.StageAsync(AuditAction.Update, nameof(LeaveAllowance), allowance.Id.ToString(), cancellationToken: cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            var saved = await _unitOfWork.Repository<LeaveAllowance>().Query()
                .Include(a => a.TeacherProfile).ThenInclude(t => t!.User)
                .FirstAsync(a => a.Id == allowance.Id, cancellationToken);
            return ToDto(saved);
        }

        public async Task<LeaveAllowanceStatusDto> GetMyLeaveAllowanceStatusAsync(
            Guid teacherUserId,
            CancellationToken cancellationToken = default)
        {
            var teacher = await _unitOfWork.Repository<TeacherProfile>()
                .FirstOrDefaultAsync(t => t.UserId == teacherUserId, cancellationToken)
                ?? throw new NotFoundException("No teacher profile is linked to the current account.");

            var now = DateTime.UtcNow;
            var allowance = await GetEffectiveAllowanceAsync(teacher.Id, cancellationToken);
            var used = await CountClassWiseCancellationsInMonthAsync(teacher.Id, now.Year, now.Month, cancellationToken);
            return new LeaveAllowanceStatusDto
            {
                MonthlyAllowance = allowance,
                UsedThisMonth = used,
                Remaining = Math.Max(0, allowance - used),
            };
        }

        /// <summary>Resolves how many more sessions this teacher may class-wise-cancel in the
        /// given calendar month right now — allowance minus what's already been approved for
        /// that month.</summary>
        private async Task<int> GetRemainingAllowanceAsync(
            Guid teacherProfileId, int year, int month, CancellationToken cancellationToken)
        {
            var allowance = await GetEffectiveAllowanceAsync(teacherProfileId, cancellationToken);
            var used = await CountClassWiseCancellationsInMonthAsync(teacherProfileId, year, month, cancellationToken);
            return Math.Max(0, allowance - used);
        }

        /// <summary>The teacher's own configured allowance if she has one, else the centre-wide
        /// default (null TeacherProfileId), else 0 — mirrors PayoutRate's own default-card
        /// fallback rule.</summary>
        private async Task<int> GetEffectiveAllowanceAsync(Guid teacherProfileId, CancellationToken cancellationToken)
        {
            var ownAllowance = await _unitOfWork.Repository<LeaveAllowance>()
                .FirstOrDefaultAsync(a => a.TeacherProfileId == teacherProfileId, cancellationToken);
            if (ownAllowance is not null)
            {
                return ownAllowance.MonthlyAllowance;
            }

            var defaultAllowance = await _unitOfWork.Repository<LeaveAllowance>()
                .FirstOrDefaultAsync(a => a.TeacherProfileId == null, cancellationToken);
            return defaultAllowance?.MonthlyAllowance ?? 0;
        }

        /// <summary>How many sessions this teacher has already had class-wise-cancelled
        /// (Approved, whether auto- or admin-approved) for the given calendar month —
        /// counted from the linked sessions' own scheduled month, not the leave request's
        /// submission date, matching how the allowance itself is keyed.</summary>
        private async Task<int> CountClassWiseCancellationsInMonthAsync(
            Guid teacherProfileId, int year, int month, CancellationToken cancellationToken)
        {
            return await _unitOfWork.Repository<LeaveRequestSession>().Query()
                .Where(ls => ls.LeaveRequest.TeacherProfileId == teacherProfileId
                             && ls.LeaveRequest.IsClassWise
                             && ls.LeaveRequest.Status == LeaveStatus.Approved
                             && ls.ClassSession.ScheduledStartAtUtc.Year == year
                             && ls.ClassSession.ScheduledStartAtUtc.Month == month)
                .CountAsync(cancellationToken);
        }

        public async Task<IReadOnlyList<LeaveRequestDto>> ListLeaveAsync(
            LeaveStatus? status,
            CancellationToken cancellationToken = default)
        {
            IQueryable<LeaveRequest> query = _unitOfWork.Repository<LeaveRequest>().Query()
                .Include(l => l.TeacherProfile).ThenInclude(t => t.User)
                .Include(l => l.Sessions).ThenInclude(s => s.ClassSession).ThenInclude(cs => cs.Batch);
            if (status.HasValue)
            {
                query = query.Where(l => l.Status == status.Value);
            }

            var leaves = await query.OrderByDescending(l => l.CreatedAtUtc).ToListAsync(cancellationToken);
            var result = new List<LeaveRequestDto>(leaves.Count);
            foreach (var leave in leaves)
            {
                result.Add(await ToDtoAsync(leave, cancellationToken));
            }

            return result;
        }

        public async Task<IReadOnlyList<LeaveRequestDto>> ListLeaveForTeacherUserAsync(
            Guid teacherUserId,
            CancellationToken cancellationToken = default)
        {
            var teacher = await _unitOfWork.Repository<TeacherProfile>()
                .FirstOrDefaultAsync(t => t.UserId == teacherUserId, cancellationToken)
                ?? throw new NotFoundException("No teacher profile is linked to the current account.");

            var leaves = await _unitOfWork.Repository<LeaveRequest>().Query()
                .Include(l => l.TeacherProfile).ThenInclude(t => t.User)
                .Include(l => l.Sessions).ThenInclude(s => s.ClassSession).ThenInclude(cs => cs.Batch)
                .Where(l => l.TeacherProfileId == teacher.Id)
                .OrderByDescending(l => l.CreatedAtUtc)
                .ToListAsync(cancellationToken);

            var result = new List<LeaveRequestDto>(leaves.Count);
            foreach (var leave in leaves)
            {
                result.Add(await ToDtoAsync(leave, cancellationToken));
            }

            return result;
        }

        public async Task<LeaveRequestDto> ReviewLeaveAsync(
            Guid id,
            ReviewLeaveRequest request,
            CancellationToken cancellationToken = default)
        {
            // Load tracked (Query() is AsNoTracking; mutating that never persists) — .Include
            // for Sessions since a class-wise request's cancellation targets exactly those,
            // not a time-window scan.
            var leave = await _unitOfWork.Repository<LeaveRequest>().TrackedQuery()
                .Include(l => l.Sessions).ThenInclude(s => s.ClassSession).ThenInclude(cs => cs.Batch)
                .FirstOrDefaultAsync(l => l.Id == id, cancellationToken)
                ?? throw new NotFoundException(nameof(LeaveRequest), id);

            if (leave.Status != LeaveStatus.Pending)
            {
                throw new DomainValidationException($"This leave application is already {leave.Status}.");
            }

            leave.Status = request.Approve ? LeaveStatus.Approved : LeaveStatus.Rejected;
            leave.ReviewNote = request.ReviewNote;
            leave.ReviewedAtUtc = DateTime.UtcNow;

            // Approving leave must actually resolve the sessions it covers — previously
            // this only computed AffectedSessionCount for display and sent a notice; the
            // sessions themselves stayed Scheduled indefinitely for a teacher now on leave,
            // still blocking EnsureTeacherIsFreeAsync's slot checks and still completable.
            // This does NOT auto-reschedule a makeup class (unlike the no-show carry-forward
            // flow) — picking a new date/capacity for a makeup is an admin call, not one to
            // make silently here.
            var affectedCount = 0;
            if (leave.Status == LeaveStatus.Approved)
            {
                List<ClassSession> affectedSessions;
                if (leave.IsClassWise)
                {
                    // Exactly the sessions the teacher picked — never a time-window scan,
                    // which could sweep up other classes that happen to fall in the same
                    // span (see LeaveRequestSession's own doc comment).
                    var sessionIds = leave.Sessions.Select(s => s.ClassSessionId).ToList();
                    affectedSessions = await _unitOfWork.Repository<ClassSession>().TrackedQuery()
                        .Where(s => sessionIds.Contains(s.Id) && s.Status == SessionStatus.Scheduled)
                        .ToListAsync(cancellationToken);
                }
                else
                {
                    affectedSessions = await _unitOfWork.Repository<ClassSession>().TrackedQuery()
                        .Where(s => s.TeacherProfileId == leave.TeacherProfileId
                            && s.Status == SessionStatus.Scheduled
                            && s.ScheduledStartAtUtc < leave.EndAtUtc
                            && s.ScheduledEndAtUtc > leave.StartAtUtc)
                        .ToListAsync(cancellationToken);
                }
                affectedCount = affectedSessions.Count;

                foreach (var session in affectedSessions)
                {
                    session.Status = SessionStatus.Cancelled;
                    session.CancellationReason = leave.IsClassWise
                        ? $"Teacher on approved class-wise leave: {leave.Reason}"
                        : $"Teacher on approved leave ({DateTimeDisplay.ToLocalDate(leave.StartAtUtc, "dd MMM yyyy")} – {DateTimeDisplay.ToLocalDate(leave.EndAtUtc, "dd MMM yyyy")}).";
                }
            }

            await _auditLog.StageAsync(AuditAction.Update, nameof(LeaveRequest), leave.Id.ToString(),
                changesJson: affectedCount > 0 ? $"{{\"sessionsCancelled\":{affectedCount}}}" : null,
                cancellationToken: cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            // The review is committed from here on; the notification fan-out must not be
            // torn down by the caller aborting the HTTP request (browser closed/navigated),
            // or the teacher/team/parents silently miss the update mid-loop.
            cancellationToken = CancellationToken.None;

            var teacherProfile = await _unitOfWork.Repository<TeacherProfile>().Query()
                .Include(t => t.User)
                .FirstAsync(t => t.Id == leave.TeacherProfileId, cancellationToken);
            var teacherUser = teacherProfile.User;
            await _notificationService.SendTemplatedEmailAsync(
                teacherUser.Id,
                teacherUser.Email,
                NotificationType.LeaveStatusUpdate,
                "leave-status-teacher",
                new Dictionary<string, string>
                {
                    ["TeacherFirstName"] = teacherUser.FirstName,
                    ["StartAtLocal"] = DateTimeDisplay.ToLocal(leave.StartAtUtc, teacherUser.TimeZoneId),
                    ["EndAtLocal"] = DateTimeDisplay.ToLocal(leave.EndAtUtc, teacherUser.TimeZoneId),
                    ["Status"] = leave.Status.ToString(),
                    ["ReviewNote"] = string.IsNullOrEmpty(request.ReviewNote) ? "" : $"Note: {request.ReviewNote}",
                },
                cancellationToken);

            // Approved leave fans out: the whole core team plus every parent whose child
            // is in one of this teacher's batches gets notified (client requirement).
            if (leave.Status == LeaveStatus.Approved)
            {
                var teacherName = $"{teacherUser.FirstName} {teacherUser.LastName}".Trim();
                var window = DateTimeDisplay.ToLocalRange(leave.StartAtUtc, leave.EndAtUtc);

                var coreTeam = await _unitOfWork.Repository<User>().Query()
                    .Where(u => (u.Role == UserRole.Admin || u.Role == UserRole.SubAdmin) && u.Status == UserStatus.Active)
                    .ToListAsync(cancellationToken);
                foreach (var member in coreTeam)
                {
                    await _notificationService.SendTemplatedEmailAsync(
                        member.Id, member.Email, NotificationType.LeaveStatusUpdate,
                        "leave-notify-core-team",
                        new Dictionary<string, string> { ["TeacherName"] = teacherName, ["Window"] = window },
                        cancellationToken);
                }

                var affectedParents = await _unitOfWork.Repository<BatchEnrollment>().Query()
                    .Where(e => e.Status == EnrollmentStatus.Active
                                && e.Batch.TeacherProfileId == leave.TeacherProfileId)
                    .Select(e => e.Child.ParentProfile.User)
                    .Distinct()
                    .ToListAsync(cancellationToken);
                foreach (var parent in affectedParents)
                {
                    await _notificationService.SendTemplatedEmailAsync(
                        parent.Id, parent.Email, NotificationType.LeaveStatusUpdate,
                        "leave-notify-parent",
                        new Dictionary<string, string> { ["TeacherName"] = teacherName, ["Window"] = window },
                        cancellationToken);
                }
            }

            // Attach the nav for DTO mapping only, after the last SaveChanges (avoids re-tracking).
            leave.TeacherProfile = teacherProfile;
            var dto = await ToDtoAsync(leave, cancellationToken);
            if (leave.Status == LeaveStatus.Approved)
            {
                // ToDtoAsync recomputes this by counting Scheduled sessions in the window —
                // now 0, since approval just cancelled them. Report what this approval
                // actually resolved, not a live count that undercounts its own effect.
                dto.AffectedSessionCount = affectedCount;
            }

            return dto;
        }

        public async Task<IReadOnlyList<SessionAttendanceDto>> CaptureAttendanceAsync(
            Guid sessionId,
            CaptureAttendanceRequest request,
            CancellationToken cancellationToken = default)
        {
            await EnsureSessionParticipantAsync(sessionId, cancellationToken);
            return await CaptureAttendanceCoreAsync(sessionId, request, cancellationToken);
        }

        /// <summary>
        /// Unguarded core, used by the public (HTTP-request-scoped, <see cref="_currentUser"/>-
        /// checked) <see cref="CaptureAttendanceAsync"/> above and by
        /// <see cref="CaptureJoinAttendanceAsync"/> below. The join-capture path deliberately
        /// does NOT go through <see cref="EnsureSessionParticipantAsync"/>: <see cref="_currentUser"/>
        /// resolves from <c>IHttpContextAccessor</c>, which is not reliably populated for a
        /// SignalR hub method invoked over an already-established connection (unlike a plain HTTP
        /// request) — the hub instead resolves and validates the caller itself, from
        /// <c>Hub.Context.User</c>, before ever calling in here.
        /// </summary>
        private async Task<IReadOnlyList<SessionAttendanceDto>> CaptureAttendanceCoreAsync(
            Guid sessionId,
            CaptureAttendanceRequest request,
            CancellationToken cancellationToken)
        {
            var session = await _unitOfWork.Repository<ClassSession>()
                .FirstOrDefaultAsync(s => s.Id == sessionId, cancellationToken)
                ?? throw new NotFoundException(nameof(ClassSession), sessionId);

            if (session.Status is SessionStatus.Cancelled or SessionStatus.Rescheduled
                or SessionStatus.TeacherNoShow or SessionStatus.StudentNoShow)
            {
                throw new DomainValidationException(
                    $"Attendance cannot be recorded for a session in status '{session.Status}'.");
            }

            var repository = _unitOfWork.Repository<SessionAttendance>();

            // One tracked read of this session's attendance rows (bounded by class size),
            // instead of a FirstOrDefaultAsync per submitted entry. The whole-session read
            // is covered by the existing ClassSessionId index and lets the rejoin-updates-
            // the-existing-row rule below be resolved in memory.
            var existingRows = await repository.TrackedQuery()
                .Where(a => a.ClassSessionId == sessionId)
                .ToListAsync(cancellationToken);

            foreach (var entry in request.Entries)
            {
                if ((entry.ChildId is null) == (entry.TeacherProfileId is null))
                {
                    throw new DomainValidationException("Each attendance entry must set exactly one of childId or teacherProfileId.");
                }

                // Rejoin after a network drop updates the existing row, never duplicates it
                var existing = existingRows.FirstOrDefault(
                    a => a.ChildId == entry.ChildId && a.TeacherProfileId == entry.TeacherProfileId);

                if (existing is null)
                {
                    await repository.AddAsync(
                        new SessionAttendance
                        {
                            ClassSessionId = sessionId,
                            ParticipantType = entry.ChildId is not null ? ParticipantType.Student : ParticipantType.Teacher,
                            ChildId = entry.ChildId,
                            TeacherProfileId = entry.TeacherProfileId,
                            Status = entry.Status,
                            JoinedAtUtc = entry.JoinedAtUtc,
                            LeftAtUtc = entry.LeftAtUtc,
                        },
                        cancellationToken);
                }
                else
                {
                    existing.Status = entry.Status;
                    existing.JoinedAtUtc = entry.JoinedAtUtc ?? existing.JoinedAtUtc;
                    existing.LeftAtUtc = entry.LeftAtUtc ?? existing.LeftAtUtc;
                }
            }

            await _auditLog.StageAsync(AuditAction.Update, nameof(SessionAttendance), sessionId.ToString(),
                changesJson: $"{{\"entries\":{request.Entries.Count}}}", cancellationToken: cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            // Attendance updates: parents hear about an absence the moment it is recorded
            var absentChildIds = request.Entries
                .Where(e => e.ChildId.HasValue && e.Status == AttendanceStatus.Absent)
                .Select(e => e.ChildId!.Value)
                .ToList();
            if (absentChildIds.Count > 0)
            {
                var absentChildren = await _unitOfWork.Repository<Child>().Query()
                    .Include(c => c.ParentProfile).ThenInclude(p => p.User)
                    .Where(c => absentChildIds.Contains(c.Id))
                    .ToListAsync(cancellationToken);
                foreach (var child in absentChildren)
                {
                    var parentUser = child.ParentProfile.User;
                    await _notificationService.SendTemplatedEmailAsync(
                        parentUser.Id,
                        parentUser.Email,
                        NotificationType.AttendanceUpdate,
                        "attendance-absent",
                        new Dictionary<string, string> { ["ChildFirstName"] = child.FirstName },
                        cancellationToken);
                }
            }

            return await ListAttendanceCoreAsync(sessionId, cancellationToken);
        }

        public async Task<IReadOnlyList<SessionAttendanceDto>> ListAttendanceAsync(
            Guid sessionId,
            CancellationToken cancellationToken = default)
        {
            await EnsureSessionParticipantAsync(sessionId, cancellationToken);
            return await ListAttendanceCoreAsync(sessionId, cancellationToken);
        }

        private async Task<IReadOnlyList<SessionAttendanceDto>> ListAttendanceCoreAsync(
            Guid sessionId,
            CancellationToken cancellationToken)
        {
            var rows = await _unitOfWork.Repository<SessionAttendance>().Query()
                .Include(a => a.Child)
                .Where(a => a.ClassSessionId == sessionId)
                .ToListAsync(cancellationToken);

            return rows.Select(a => new SessionAttendanceDto
            {
                Id = a.Id,
                ClassSessionId = a.ClassSessionId,
                ParticipantType = a.ParticipantType,
                ChildId = a.ChildId,
                ChildName = a.Child is null ? null : $"{a.Child.FirstName} {a.Child.LastName}".Trim(),
                TeacherProfileId = a.TeacherProfileId,
                Status = a.Status,
                JoinedAtUtc = a.JoinedAtUtc,
                LeftAtUtc = a.LeftAtUtc,
            }).ToList();
        }

        private async Task<int> CountAffectedSessionsAsync(
            Guid teacherProfileId,
            DateTime startUtc,
            DateTime endUtc,
            CancellationToken cancellationToken)
        {
            return await _unitOfWork.Repository<ClassSession>().Query()
                .CountAsync(
                    s => s.TeacherProfileId == teacherProfileId
                         && s.Status == SessionStatus.Scheduled
                         && s.ScheduledStartAtUtc < endUtc
                         && s.ScheduledEndAtUtc > startUtc,
                    cancellationToken);
        }

        private async Task NotifyAdminsAsync(IReadOnlyDictionary<string, string> tokens, CancellationToken cancellationToken)
        {
            var admins = await _unitOfWork.Repository<User>().Query()
                .Where(u => u.Role == UserRole.Admin && u.Status == UserStatus.Active)
                .ToListAsync(cancellationToken);
            foreach (var admin in admins)
            {
                await _notificationService.SendTemplatedEmailAsync(
                    admin.Id, admin.Email, NotificationType.General, "leave-submitted-admin-alert", tokens, cancellationToken);
            }
        }

        private async Task<LeaveRequestDto> ToDtoAsync(LeaveRequest leave, CancellationToken cancellationToken)
        {
            var dto = new LeaveRequestDto
            {
                Id = leave.Id,
                TeacherProfileId = leave.TeacherProfileId,
                TeacherName = $"{leave.TeacherProfile.User.FirstName} {leave.TeacherProfile.User.LastName}".Trim(),
                StartAtUtc = leave.StartAtUtc,
                EndAtUtc = leave.EndAtUtc,
                Reason = leave.Reason,
                Status = leave.Status,
                ReviewNote = leave.ReviewNote,
                CreatedAtUtc = leave.CreatedAtUtc,
                IsClassWise = leave.IsClassWise,
            };

            if (leave.IsClassWise)
            {
                // A fixed count of exactly what this request covers — never a live window
                // scan, which would read back 0 the moment approval cancels those sessions
                // (the same trap the non-class-wise branch below works around via the
                // caller's own affectedCount override after ReviewLeaveAsync's cancellation).
                dto.Sessions = leave.Sessions
                    .OrderBy(s => s.ClassSession.ScheduledStartAtUtc)
                    .Select(s => new LeaveSessionSummaryDto
                    {
                        SessionId = s.ClassSessionId,
                        ScheduledStartAtUtc = s.ClassSession.ScheduledStartAtUtc,
                        ScheduledEndAtUtc = s.ClassSession.ScheduledEndAtUtc,
                        BatchName = s.ClassSession.Batch?.Name,
                    })
                    .ToList();
                dto.AffectedSessionCount = leave.Sessions.Count;
            }
            else
            {
                dto.AffectedSessionCount = await CountAffectedSessionsAsync(
                    leave.TeacherProfileId, leave.StartAtUtc, leave.EndAtUtc, cancellationToken);
            }

            return dto;
        }

        private static HolidayDto ToDto(Holiday holiday)
        {
            return new HolidayDto
            {
                Id = holiday.Id,
                Date = holiday.Date,
                Name = holiday.Name,
                Description = holiday.Description,
            };
        }

        private static LeaveAllowanceDto ToDto(LeaveAllowance allowance)
        {
            return new LeaveAllowanceDto
            {
                Id = allowance.Id,
                TeacherProfileId = allowance.TeacherProfileId,
                TeacherName = allowance.TeacherProfile is null
                    ? "All teachers (default)"
                    : $"{allowance.TeacherProfile.User.FirstName} {allowance.TeacherProfile.User.LastName}".Trim(),
                MonthlyAllowance = allowance.MonthlyAllowance,
            };
        }
    }
}
