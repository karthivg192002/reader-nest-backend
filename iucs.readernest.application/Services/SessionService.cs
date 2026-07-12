using iucs.readernest.application.Common.Exceptions;
using iucs.readernest.application.Dto.Sessions;
using iucs.readernest.application.Mappings;
using iucs.readernest.domain.Entities.Academics;
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

        public SessionService(
            IUnitOfWork unitOfWork,
            IAuditLogService auditLog,
            IPayoutService payoutService,
            INotificationService notificationService)
        {
            _unitOfWork = unitOfWork;
            _auditLog = auditLog;
            _payoutService = payoutService;
            _notificationService = notificationService;
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
                await _notificationService.SendEmailAsync(
                    parent.ParentUserId,
                    parent.ParentEmail,
                    NotificationType.PerformanceSummary,
                    $"Class summary — {session.ScheduledStartAtUtc:dd MMM}",
                    $"Today's class notes for {parent.ChildName}:\n\n{session.Summary}",
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

            if (TerminalStatuses.Contains(session.Status))
            {
                throw new DomainValidationException($"A session in status '{session.Status}' cannot be marked as a no-show.");
            }

            session.Status = request.Party == NoShowParty.Teacher
                ? SessionStatus.TeacherNoShow
                : SessionStatus.StudentNoShow;

            // The missed class is never lost: a carried-forward session is placed one week
            // later at the same slot, keeping the traceability link for calendar and payouts.
            var carriedForward = new ClassSession
            {
                BatchId = session.BatchId,
                TeacherProfileId = session.TeacherProfileId,
                Type = session.Type,
                Status = SessionStatus.CarriedForward,
                ScheduledStartAtUtc = session.ScheduledStartAtUtc.AddDays(7),
                ScheduledEndAtUtc = session.ScheduledEndAtUtc.AddDays(7),
                MeetingRoomId = session.MeetingRoomId,
                CarriedForwardFromSessionId = session.Id,
            };
            await _unitOfWork.Repository<ClassSession>().AddAsync(carriedForward, cancellationToken);

            if (request.Party == NoShowParty.Student)
            {
                // Teacher waited for the student: the waiting amount still accrues
                await _payoutService.AccrueForSessionAsync(
                    session, PayoutItemType.StudentNoShowWaiting,
                    request.Note ?? "Student no-show waiting amount", cancellationToken);
            }
            else
            {
                await _payoutService.AccrueForSessionAsync(
                    session, PayoutItemType.TeacherNoShowDeduction,
                    request.Note ?? "Teacher no-show deduction", cancellationToken);
                await NotifyAdminsOfTeacherNoShowAsync(session, cancellationToken);
            }

            await _auditLog.StageAsync(AuditAction.Update, nameof(ClassSession), session.Id.ToString(),
                changesJson: $"{{\"noShow\":\"{request.Party}\",\"carriedForwardTo\":\"{carriedForward.Id}\"}}",
                cancellationToken: cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return await GetAsync(carriedForward.Id, cancellationToken);
        }

        public async Task<SessionRecordingDto> AddRecordingAsync(
            Guid sessionId,
            RegisterRecordingRequest request,
            CancellationToken cancellationToken = default)
        {
            var sessionExists = await _unitOfWork.Repository<ClassSession>()
                .ExistsAsync(s => s.Id == sessionId, cancellationToken);
            if (!sessionExists)
            {
                throw new NotFoundException(nameof(ClassSession), sessionId);
            }

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

        public async Task<IReadOnlyList<SessionRecordingDto>> ListRecordingsAsync(
            Guid sessionId,
            CancellationToken cancellationToken = default)
        {
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
                    $"The teacher already has a session from {conflict.ScheduledStartAtUtc:u} to {conflict.ScheduledEndAtUtc:u}.");
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

            await _notificationService.SendEmailAsync(
                teacher.User.Id,
                teacher.User.Email,
                NotificationType.BookingConfirmation,
                "Class scheduled",
                $"A {session.Type} session was scheduled for you: {session.ScheduledStartAtUtc:u} – {session.ScheduledEndAtUtc:u}.",
                cancellationToken);
        }

        public async Task RecordEngagementAsync(
            Guid sessionId,
            RecordEngagementRequest request,
            CancellationToken cancellationToken = default)
        {
            var sessionExists = await _unitOfWork.Repository<ClassSession>()
                .ExistsAsync(s => s.Id == sessionId, cancellationToken);
            if (!sessionExists)
            {
                throw new NotFoundException(nameof(ClassSession), sessionId);
            }

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

        public async Task<IReadOnlyList<EngagementSummaryDto>> GetEngagementSummaryAsync(
            Guid sessionId,
            CancellationToken cancellationToken = default)
        {
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

                    // Weighted score: accuracy counts double; capped contributions keep one
                    // hyperactive signal from masking absence everywhere else
                    var score = Math.Min(100,
                        Math.Min(quizCorrect * 2, 30)
                        + Math.Min(quizAttempts, 20)
                        + Math.Min(activity * 2, 20)
                        + Math.Min(whiteboard, 15)
                        + Math.Min(attention, 15));

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

        private async Task NotifyAdminsOfTeacherNoShowAsync(ClassSession session, CancellationToken cancellationToken)
        {
            var admins = await _unitOfWork.Repository<User>().Query()
                .Where(u => u.Role == UserRole.Admin && u.Status == UserStatus.Active)
                .ToListAsync(cancellationToken);

            foreach (var admin in admins)
            {
                await _notificationService.SendEmailAsync(
                    admin.Id,
                    admin.Email,
                    NotificationType.NoShowAlert,
                    "Teacher no-show reported",
                    $"The teacher did not attend the session scheduled at {session.ScheduledStartAtUtc:u}. " +
                    "A deduction was applied and the session was carried forward.",
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

        private static void ValidateWindow(DateTime startUtc, DateTime endUtc)
        {
            if (endUtc <= startUtc)
            {
                throw new DomainValidationException("Session end time must be after the start time.");
            }
        }
    }
}
