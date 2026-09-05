using System.Text.Json;
using iucs.readernest.application.Common.Exceptions;
using iucs.readernest.application.Common.Interfaces;
using iucs.readernest.application.Dto.Admission;
using iucs.readernest.application.Dto.Users;
using iucs.readernest.application.Helper;
using iucs.readernest.application.Mappings;
using iucs.readernest.domain.Entities.Academics;
using iucs.readernest.domain.Entities.Admission;
using iucs.readernest.domain.Entities.Auditing;
using iucs.readernest.domain.Entities.Integrations;
using iucs.readernest.domain.Entities.Sessions;
using iucs.readernest.domain.Entities.Users;
using iucs.readernest.domain.Enums;
using iucs.readernest.domain.Repository;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace iucs.readernest.application.Services
{
    public class DemoBookingService : IDemoBookingService
    {
        private const string ReassignmentAuditEntityName = "DemoBookingTeacherReassignment";
        private const string FollowUpAuditEntityName = "DemoBookingFollowUp";

        private readonly IUnitOfWork _unitOfWork;
        private readonly IAuditLogService _auditLog;
        private readonly ICrmNotifier _crmNotifier;
        private readonly IEmailSender _emailSender;
        private readonly IEmailTemplateService _emailTemplateService;
        private readonly IJitsiTokenService _jitsiTokenService;
        private readonly INotificationService _notificationService;
        private readonly IUserService _userService;
        private readonly ILogger<DemoBookingService> _logger;

        public DemoBookingService(
            IUnitOfWork unitOfWork,
            IAuditLogService auditLog,
            IEmailSender emailSender,
            IEmailTemplateService emailTemplateService,
            ICrmNotifier crmNotifier,
            IJitsiTokenService jitsiTokenService,
            INotificationService notificationService,
            IUserService userService,
            ILogger<DemoBookingService> logger)
        {
            _unitOfWork = unitOfWork;
            _auditLog = auditLog;
            _emailSender = emailSender;
            _emailTemplateService = emailTemplateService;
            _crmNotifier = crmNotifier;
            _jitsiTokenService = jitsiTokenService;
            _notificationService = notificationService;
            _userService = userService;
            _logger = logger;
        }

        public async Task<IReadOnlyList<DemoBookingDto>> ListAsync(
            ConversionStatus? status,
            CancellationToken cancellationToken = default)
        {
            var query = BaseQuery();
            if (status.HasValue)
            {
                query = query.Where(b => b.ConversionStatus == status.Value);
            }

            var bookings = await query.OrderByDescending(b => b.CreatedAtUtc).ToListAsync(cancellationToken);
            return bookings.Select(b => b.ToDto()).ToList();
        }

        public async Task<DemoBookingDto> GetAsync(Guid id, CancellationToken cancellationToken = default)
        {
            var booking = await BaseQuery().FirstOrDefaultAsync(b => b.Id == id, cancellationToken)
                ?? throw new NotFoundException(nameof(DemoBooking), id);

            return booking.ToDto();
        }

        public async Task<DemoBookingDto> CreateAsync(
            CreateDemoBookingRequest request,
            CancellationToken cancellationToken = default)
        {
            if (request.ScheduledEndAtUtc <= request.ScheduledStartAtUtc)
            {
                throw new DomainValidationException("Demo end time must be after the start time.");
            }

            // Picking a free teacher and booking them into the slot is one indivisible decision:
            // the "nobody overlaps this slot" read is only worth anything if no one else can
            // slip a conflicting session in before this one is committed. There is no single
            // row to lock (the conflict is a range overlap across rows, not a duplicate key),
            // so this runs SERIALIZABLE and lets PostgreSQL's SSI arbitrate, retrying from the
            // top on a serialization failure. Nothing irreversible — emails, the CRM push —
            // happens inside; a retry must be free to redo the whole thing.
            var (session, booking, teacher) = await _unitOfWork.ExecuteInSerializableTransactionAsync(async ct =>
            {
                Guid teacherProfileId;
                if (request.TeacherProfileId.HasValue)
                {
                    var teacherExists = await _unitOfWork.Repository<TeacherProfile>()
                        .ExistsAsync(t => t.Id == request.TeacherProfileId.Value, ct);
                    if (!teacherExists)
                    {
                        throw new NotFoundException(nameof(TeacherProfile), request.TeacherProfileId.Value);
                    }

                    // Auto-assign already skips busy teachers; an explicitly-picked one needs the
                    // same overlap check, or staff could double-book a teacher the auto-assign
                    // path would have correctly avoided. Same predicate shape as AutoAssignTeacherAsync's
                    // Busy check, so it benefits from the same SERIALIZABLE protection this runs under.
                    var teacherBusy = await _unitOfWork.Repository<ClassSession>().ExistsAsync(
                        s => s.TeacherProfileId == request.TeacherProfileId.Value
                             && (s.Status == SessionStatus.Scheduled || s.Status == SessionStatus.CarriedForward)
                             && s.ScheduledStartAtUtc < request.ScheduledEndAtUtc
                             && s.ScheduledEndAtUtc > request.ScheduledStartAtUtc,
                        ct);
                    if (teacherBusy)
                    {
                        throw new DomainValidationException("This teacher already has a session booked during that time.");
                    }

                    teacherProfileId = request.TeacherProfileId.Value;
                }
                else
                {
                    teacherProfileId = await AutoAssignTeacherAsync(request, ct);
                }

                // Every teacher gets one fixed, permanent room (the same one backing
                // GET /api/users/me/meeting-room) reused for every demo they run, instead of a
                // fresh random room per booking — so the link in this email never goes stale
                // and is identical to the one the teacher already sees in their own portal.
                var teacher = await EnsureTeacherMeetingRoomAsync(teacherProfileId, ct);

                // Demos are always one-time sessions, never recurring, and have no batch
                var newSession = new ClassSession
                {
                    TeacherProfileId = teacherProfileId,
                    Type = SessionType.Demo,
                    ScheduledStartAtUtc = request.ScheduledStartAtUtc,
                    ScheduledEndAtUtc = request.ScheduledEndAtUtc,
                    MeetingRoomId = teacher.User.PersonalMeetingRoomId,
                };
                await _unitOfWork.Repository<ClassSession>().AddAsync(newSession, ct);

                var newBooking = new DemoBooking
                {
                    ClassSession = newSession,
                    ParentName = request.ParentName.Trim(),
                    ParentEmail = request.ParentEmail.Trim().ToLowerInvariant(),
                    ParentPhone = request.ParentPhone,
                    ChildName = request.ChildName.Trim(),
                    ChildAge = request.ChildAge,
                    DepartmentId = request.DepartmentId,
                    Participants = request.Participants
                        .Select(p =>
                        {
                            // Adults need an email for the confirmation; children carry none.
                            if (!p.IsChild && string.IsNullOrWhiteSpace(p.Email))
                            {
                                throw new DomainValidationException($"Participant '{p.Name}' needs an email address (children don't).");
                            }

                            return new DemoParticipant
                            {
                                Name = p.Name.Trim(),
                                Email = string.IsNullOrWhiteSpace(p.Email) ? null : p.Email.Trim().ToLowerInvariant(),
                                Phone = p.Phone,
                                IsChild = p.IsChild,
                            };
                        })
                        .ToList(),
                };
                await _unitOfWork.Repository<DemoBooking>().AddAsync(newBooking, ct);

                await _auditLog.StageAsync(AuditAction.Create, nameof(DemoBooking), newBooking.Id.ToString(), cancellationToken: ct);
                await _unitOfWork.SaveChangesAsync(ct);

                return (Session: newSession, Booking: newBooking, Teacher: teacher);
            }, cancellationToken);

            // Booking confirmation to the parent, every extra invitee, and the teacher (they may
            // not have accounts yet, so this bypasses the user-bound notification log). The
            // booking itself is already committed at this point — a template-render glitch or an
            // SMTP failure (e.g. the sender account's own daily limit) must not turn an
            // already-successful booking into a 500 response, the same reasoning
            // NotificationService.SendRenderedEmailAsync already applies to every other email
            // this app sends. Confirmed via production logs: this exact path was throwing an
            // uncaught SmtpException after the booking had already been saved.
            await SendParentDemoLinkEmailsAsync(session, booking, cancellationToken);
            await SendTeacherDemoLinkEmailAsync(
                session, booking, teacher, "demo-scheduled-teacher",
                new Dictionary<string, string> { ["ParentName"] = booking.ParentName },
                cancellationToken);

            // New lead lands in the client's CRM (no-op when no webhook is configured)
            await _crmNotifier.PushLeadEventAsync("lead.created", new
            {
                booking.Id,
                booking.ParentName,
                booking.ParentEmail,
                booking.ParentPhone,
                booking.ChildName,
                Department = booking.DepartmentId.HasValue
                    ? (await _unitOfWork.Repository<Department>().GetByIdAsync(booking.DepartmentId.Value, cancellationToken))?.Name
                    : null,
                DemoAtUtc = request.ScheduledStartAtUtc,
            }, cancellationToken);

            return await GetAsync(booking.Id, cancellationToken);
        }

        /// <summary>
        /// Auto-assign: the department-matched active teacher who is free at the slot with the lightest day.
        /// </summary>
        /// <remarks>
        /// RACE FIXED (found 2026-08-09, fixed 2026-08-10) — but only because of where this is
        /// called from, so do not lift it out of that context. This reads "is anyone busy at
        /// this slot" and its caller inserts the session afterwards; under READ COMMITTED two
        /// concurrent requests for the same slot (reachable by any anonymous visitor via
        /// POST /api/store/demo-bookings) could both read "teacher free" and double-book the
        /// same teacher. There is no unique key to lean on here — the conflict is an overlap
        /// between arbitrary time ranges across rows, not a duplicate value — so the fix is at
        /// the isolation level: CreateAsync now runs this read and the matching insert inside
        /// one IUnitOfWork.ExecuteInSerializableTransactionAsync, where PostgreSQL's SSI tracks
        /// the predicate this query reads, spots the concurrent insert that invalidates it,
        /// aborts one side with SQLSTATE 40001, and the unit of work transparently retries it
        /// against the now-committed state (where this query correctly reports the teacher
        /// busy). Any future caller MUST keep this read and its insert inside the same
        /// serializable transaction; reading here and writing outside it silently restores the
        /// original bug.
        /// <para>
        /// Two honest limits on that guarantee. (1) SSI only sees other SERIALIZABLE
        /// transactions: a regular (non-demo) ClassSession committed concurrently by a code
        /// path that does not use ExecuteInSerializableTransactionAsync is not detected, so
        /// demo-vs-demo is now safe while demo-vs-concurrently-created-regular-session is not.
        /// (2) SQLite cannot reproduce the original race (one ADO.NET connection serializes
        /// every command) and this environment has no PostgreSQL, so the fix rests on SSI's
        /// documented semantics rather than on an observed concurrent run — see the scope note
        /// on Store_BookDemo_ConcurrentRequestsForSameSlot_MustNotDoubleBookTheOnlyTeacher for
        /// exactly what the tests do and do not prove.
        /// </para>
        /// </remarks>
        private async Task<Guid> AutoAssignTeacherAsync(CreateDemoBookingRequest request, CancellationToken cancellationToken)
        {
            IQueryable<TeacherProfile> teachers = _unitOfWork.Repository<TeacherProfile>().Query()
                .Where(t => t.User.Status == UserStatus.Active);
            if (request.DepartmentId.HasValue)
            {
                teachers = teachers.Where(t => t.DepartmentId == request.DepartmentId.Value);
            }

            var dayStart = request.ScheduledStartAtUtc.Date;
            var dayEnd = dayStart.AddDays(1);

            var candidates = await teachers
                .Select(t => new
                {
                    t.Id,
                    Busy = _unitOfWork.Repository<ClassSession>().Query().Any(
                        s => s.TeacherProfileId == t.Id
                             && (s.Status == SessionStatus.Scheduled || s.Status == SessionStatus.CarriedForward)
                             && s.ScheduledStartAtUtc < request.ScheduledEndAtUtc
                             && s.ScheduledEndAtUtc > request.ScheduledStartAtUtc),
                    DayLoad = _unitOfWork.Repository<ClassSession>().Query().Count(
                        s => s.TeacherProfileId == t.Id
                             && s.ScheduledStartAtUtc >= dayStart
                             && s.ScheduledStartAtUtc < dayEnd),
                })
                .ToListAsync(cancellationToken);

            var chosen = candidates
                .Where(c => !c.Busy)
                .OrderBy(c => c.DayLoad)
                .FirstOrDefault()
                ?? throw new DomainValidationException("No teacher is available for this slot; pick a teacher or another time.");

            return chosen.Id;
        }

        public async Task<DemoBookingDto> UpdateConversionStatusAsync(
            Guid id,
            UpdateConversionStatusRequest request,
            CancellationToken cancellationToken = default)
        {
            var booking = await _unitOfWork.Repository<DemoBooking>().GetByIdAsync(id, cancellationToken)
                ?? throw new NotFoundException(nameof(DemoBooking), id);

            // Fires the account creation on the transition INTO ReadyForEnrollment, not every
            // save while the booking is already there -- editing FollowUpNotes on a booking
            // that's already ReadyForEnrollment must never re-trigger it (and can't anyway,
            // once EnsureParentAccountAsync's own existence check finds the account it just made).
            var enteringReadyForEnrollment = request.ConversionStatus == ConversionStatus.ReadyForEnrollment
                && booking.ConversionStatus != ConversionStatus.ReadyForEnrollment;

            booking.ConversionStatus = request.ConversionStatus;
            if (!string.IsNullOrWhiteSpace(request.Note))
            {
                // Full history (who logged it, exactly when) lives in the audit trail — see
                // GetFollowUpNotesAsync — rather than concatenated into one string, which used
                // to collapse every note ever logged into a single fabricated date/author on
                // reload. This field stays as a quick "most recent note" snapshot only.
                booking.FollowUpNotes = request.Note;
                await _auditLog.StageAsync(
                    AuditAction.Create,
                    FollowUpAuditEntityName,
                    booking.Id.ToString(),
                    JsonSerializer.Serialize(new { request.Note, request.NextFollowUpOn }),
                    cancellationToken);
            }
            if (request.NextFollowUpOn.HasValue)
            {
                booking.NextFollowUpOn = request.NextFollowUpOn;
            }

            if (enteringReadyForEnrollment)
            {
                await EnsureParentAccountAsync(booking, cancellationToken);
            }

            await _auditLog.StageAsync(AuditAction.Update, nameof(DemoBooking), booking.Id.ToString(), cancellationToken: cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            await _crmNotifier.PushLeadEventAsync("lead.status-changed", new
            {
                booking.Id,
                booking.ParentEmail,
                booking.ChildName,
                ConversionStatus = booking.ConversionStatus.ToString(),
                booking.FollowUpNotes,
            }, cancellationToken);

            return await GetAsync(booking.Id, cancellationToken);
        }

        /// <summary>
        /// The account-creation half of ReadyForEnrollment (see the enum value's own doc
        /// comment). Reuses UserService.CreateAsync wholesale -- temp PIN generation/hashing,
        /// the ParentProfile row, and the same "welcome-credentials" email an admin manually
        /// adding a parent through Users already sends -- rather than duplicating any of that
        /// here. A no-op when an account for this email already exists (a sibling's earlier
        /// demo, or a repeat lead): reuses it silently, no duplicate account and no re-sent
        /// credentials for someone who can already log in.
        /// </summary>
        private async Task EnsureParentAccountAsync(DemoBooking booking, CancellationToken cancellationToken)
        {
            var email = booking.ParentEmail.Trim().ToLowerInvariant();
            var alreadyHasAccount = await _unitOfWork.Repository<User>().ExistsAsync(u => u.Email == email, cancellationToken);
            if (alreadyHasAccount)
            {
                return;
            }

            var nameParts = booking.ParentName.Trim().Split(' ', 2);
            await _userService.CreateAsync(
                new CreateUserRequest
                {
                    Email = email,
                    FirstName = nameParts[0],
                    LastName = nameParts.Length > 1 ? nameParts[1] : string.Empty,
                    Phone = booking.ParentPhone,
                    Role = UserRole.Parent,
                },
                cancellationToken);
        }

        public async Task<DemoFeedbackDto> SubmitFeedbackAsync(
            Guid demoBookingId,
            Guid teacherUserId,
            SubmitDemoFeedbackRequest request,
            CancellationToken cancellationToken = default)
        {
            var booking = await _unitOfWork.Repository<DemoBooking>().GetByIdAsync(demoBookingId, cancellationToken)
                ?? throw new NotFoundException(nameof(DemoBooking), demoBookingId);

            var teacher = await _unitOfWork.Repository<TeacherProfile>()
                .FirstOrDefaultAsync(t => t.UserId == teacherUserId, cancellationToken)
                ?? throw new NotFoundException("No teacher profile is linked to the current account.");

            // Object-level authorization: being *a* teacher is not enough, this must be the
            // teacher who actually ran the demo. The feedback is the permanent, admission-facing
            // evaluation of a named child (it carries the recommended course and batch type and
            // feeds the conversion pipeline), it is filed under the caller's own teacher profile,
            // and the already-submitted guard below makes it one-shot — so without this check any
            // teacher who knows a booking id could falsify another teacher's assessment of a child
            // AND permanently lock the real teacher out of a mandatory step.
            // Mirrors ListForTeacherUserAsync, which already scopes the read side this way.
            var ownsDemo = booking.ClassSessionId is { } sessionId
                && await _unitOfWork.Repository<ClassSession>()
                    .ExistsAsync(s => s.Id == sessionId && s.TeacherProfileId == teacher.Id, cancellationToken);
            if (!ownsDemo)
            {
                throw new ForbiddenException("You can only submit feedback for a demo you taught.");
            }

            var alreadySubmitted = await _unitOfWork.Repository<DemoFeedback>()
                .ExistsAsync(f => f.DemoBookingId == demoBookingId, cancellationToken);
            if (alreadySubmitted)
            {
                throw new DomainValidationException("Feedback has already been submitted for this demo.");
            }

            var feedback = new DemoFeedback
            {
                DemoBookingId = booking.Id,
                TeacherProfileId = teacher.Id,
                AcademicLevel = request.AcademicLevel.Trim(),
                Strengths = request.Strengths.Trim(),
                ImprovementAreas = request.ImprovementAreas.Trim(),
                RecommendedCourseId = request.RecommendedCourseId,
                SuggestedBatchType = request.SuggestedBatchType,
                Remarks = request.Remarks,
                SubmittedAtUtc = DateTime.UtcNow,
            };
            await _unitOfWork.Repository<DemoFeedback>().AddAsync(feedback, cancellationToken);

            // Feedback closes the demo stage; the booking enters the conversion pipeline
            if (booking.ConversionStatus == ConversionStatus.DemoScheduled)
            {
                booking.ConversionStatus = ConversionStatus.DemoCompleted;
            }

            await _auditLog.StageAsync(AuditAction.Create, nameof(DemoFeedback), feedback.Id.ToString(), cancellationToken: cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            var saved = await FeedbackQuery().FirstAsync(f => f.Id == feedback.Id, cancellationToken);
            return ToFeedbackDto(saved);
        }

        public async Task<IReadOnlyList<DemoFeedbackDto>> ListFeedbackAsync(CancellationToken cancellationToken = default)
        {
            var feedbacks = await FeedbackQuery()
                .OrderByDescending(f => f.SubmittedAtUtc)
                .ToListAsync(cancellationToken);
            return feedbacks.Select(ToFeedbackDto).ToList();
        }

        public async Task<IReadOnlyList<DemoBookingDto>> ListForTeacherUserAsync(
            Guid userId,
            CancellationToken cancellationToken = default)
        {
            var teacher = await GetTeacherAsync(userId, cancellationToken);
            var bookings = await BaseQuery()
                .Where(b => b.ClassSession != null && b.ClassSession.TeacherProfileId == teacher.Id)
                .OrderByDescending(b => b.CreatedAtUtc)
                .ToListAsync(cancellationToken);
            return bookings.Select(b => b.ToDto()).ToList();
        }

        public async Task<IReadOnlyList<DemoFeedbackDto>> ListFeedbackForTeacherUserAsync(
            Guid userId,
            CancellationToken cancellationToken = default)
        {
            var teacher = await GetTeacherAsync(userId, cancellationToken);
            var feedbacks = await FeedbackQuery()
                .Where(f => f.TeacherProfileId == teacher.Id)
                .OrderByDescending(f => f.SubmittedAtUtc)
                .ToListAsync(cancellationToken);
            return feedbacks.Select(ToFeedbackDto).ToList();
        }

        private async Task<TeacherProfile> GetTeacherAsync(Guid userId, CancellationToken cancellationToken)
        {
            return await _unitOfWork.Repository<TeacherProfile>()
                .FirstOrDefaultAsync(t => t.UserId == userId, cancellationToken)
                ?? throw new NotFoundException("No teacher profile is linked to the current account.");
        }

        private IQueryable<DemoFeedback> FeedbackQuery()
        {
            return _unitOfWork.Repository<DemoFeedback>().Query()
                .Include(f => f.DemoBooking)
                .Include(f => f.RecommendedCourse)
                .Include(f => f.TeacherProfile).ThenInclude(t => t.User);
        }

        private static DemoFeedbackDto ToFeedbackDto(DemoFeedback feedback)
        {
            return new DemoFeedbackDto
            {
                Id = feedback.Id,
                DemoBookingId = feedback.DemoBookingId,
                ChildName = feedback.DemoBooking.ChildName,
                ParentName = feedback.DemoBooking.ParentName,
                TeacherProfileId = feedback.TeacherProfileId,
                TeacherName = $"{feedback.TeacherProfile.User.FirstName} {feedback.TeacherProfile.User.LastName}".Trim(),
                AcademicLevel = feedback.AcademicLevel,
                Strengths = feedback.Strengths,
                ImprovementAreas = feedback.ImprovementAreas,
                RecommendedCourseId = feedback.RecommendedCourseId,
                RecommendedCourseName = feedback.RecommendedCourse?.Name,
                SuggestedBatchType = feedback.SuggestedBatchType,
                Remarks = feedback.Remarks,
                SubmittedAtUtc = feedback.SubmittedAtUtc,
            };
        }

        public async Task<IReadOnlyList<ParentDemoHistoryDto>> ListParentHistoryAsync(
            string? search,
            CancellationToken cancellationToken = default)
        {
            var query = BaseQuery();
            if (!string.IsNullOrWhiteSpace(search))
            {
                var term = search.Trim().ToLower();
                query = query.Where(b =>
                    b.ParentName.ToLower().Contains(term)
                    || b.ParentEmail.ToLower().Contains(term)
                    || (b.ParentPhone != null && b.ParentPhone.Contains(term)));
            }

            var bookings = await query.OrderByDescending(b => b.CreatedAtUtc).ToListAsync(cancellationToken);

            // One record per parent (email is the lead identity), every demo they've taken.
            return bookings
                .GroupBy(b => b.ParentEmail)
                .Select(g =>
                {
                    var dtos = g.Select(b => b.ToDto()).ToList();
                    return new ParentDemoHistoryDto
                    {
                        ParentEmail = g.Key,
                        ParentName = g.First().ParentName,
                        ParentPhone = g.First().ParentPhone,
                        TotalDemos = dtos.Count,
                        EnrolledCount = dtos.Count(d => d.ConversionStatus == ConversionStatus.Enrolled),
                        LastDemoAtUtc = dtos.Max(d => d.ScheduledStartAtUtc),
                        TotalPayable = dtos.Sum(d => d.PayableAmount),
                        Bookings = dtos,
                    };
                })
                .OrderByDescending(h => h.LastDemoAtUtc)
                .ToList();
        }

        public async Task<DemoBookingDto> ReassignTeacherAsync(
            Guid bookingId,
            ReassignTeacherRequest request,
            CancellationToken cancellationToken = default)
        {
            // Same reasoning as CreateAsync's auto-assign race: the "is the new teacher free"
            // read and the write that commits them to the slot must be one indivisible decision,
            // or a concurrent booking/reassignment could double-book them in the gap between the
            // two. Nothing irreversible (the audit row, the emails) happens inside — a retry must
            // be free to redo the whole thing.
            var result = await _unitOfWork.ExecuteInSerializableTransactionAsync(async ct =>
            {
                var booking = await _unitOfWork.Repository<DemoBooking>().Query()
                    .Include(b => b.Participants)
                    .FirstOrDefaultAsync(b => b.Id == bookingId, ct)
                    ?? throw new NotFoundException(nameof(DemoBooking), bookingId);

                // The frontend only offers this action for a still-scheduled demo, but that's a
                // UI convenience, not a boundary — this is the actual enforcement point. Without
                // it, a booking reached directly by id could get "reassigned" after the demo
                // already happened, was cancelled, or converted, silently emailing a teacher who
                // has nothing to do.
                if (booking.ConversionStatus != ConversionStatus.DemoScheduled)
                {
                    throw new DomainValidationException("Only a demo that is still scheduled can have its teacher reassigned.");
                }

                if (booking.ClassSessionId is not { } classSessionId)
                {
                    throw new DomainValidationException("This booking has no linked class session to reassign.");
                }

                // Tracked, not the no-tracking Query() the rest of this method reads through --
                // this is the entity whose TeacherProfileId is actually mutated and saved below.
                var demoSession = await _unitOfWork.Repository<ClassSession>().TrackedQuery()
                    .FirstOrDefaultAsync(s => s.Id == classSessionId, ct)
                    ?? throw new NotFoundException(nameof(ClassSession), classSessionId);

                var newTeacher = await EnsureTeacherMeetingRoomAsync(request.TeacherProfileId, ct);

                if (newTeacher.User.Status != UserStatus.Active)
                {
                    throw new DomainValidationException("Cannot assign an inactive teacher.");
                }

                if (newTeacher.Id == demoSession.TeacherProfileId)
                {
                    throw new DomainValidationException("This teacher is already assigned to this demo.");
                }

                // Same overlap rule CreateAsync/AutoAssignTeacherAsync enforce for a fresh
                // booking — a manual override must not be allowed to double-book the new teacher.
                var conflict = await _unitOfWork.Repository<ClassSession>().ExistsAsync(
                    s => s.Id != demoSession.Id
                         && s.TeacherProfileId == newTeacher.Id
                         && (s.Status == SessionStatus.Scheduled || s.Status == SessionStatus.CarriedForward)
                         && s.ScheduledStartAtUtc < demoSession.ScheduledEndAtUtc
                         && s.ScheduledEndAtUtc > demoSession.ScheduledStartAtUtc,
                    ct);
                if (conflict)
                {
                    throw new DomainValidationException(
                        $"{newTeacher.User.FirstName} {newTeacher.User.LastName} already has a session booked during that time.");
                }

                var oldTeacher = await _unitOfWork.Repository<TeacherProfile>().Query()
                    .Include(t => t.User)
                    .FirstOrDefaultAsync(t => t.Id == demoSession.TeacherProfileId, ct);

                demoSession.TeacherProfileId = newTeacher.Id;
                // The room is the teacher's fixed personal room, so it must follow them --
                // otherwise the parent's already-sent link keeps pointing at the old teacher's
                // room. SendDemoLinkEmailsAsync below re-sends the parent/participants their
                // demo-confirmed email with the new room's link so they're never left stale.
                demoSession.MeetingRoomId = newTeacher.User.PersonalMeetingRoomId;

                await _auditLog.StageAsync(
                    AuditAction.Update,
                    ReassignmentAuditEntityName,
                    booking.Id.ToString(),
                    JsonSerializer.Serialize(new
                    {
                        OldTeacherProfileId = oldTeacher?.Id,
                        OldTeacherName = oldTeacher is not null ? $"{oldTeacher.User.FirstName} {oldTeacher.User.LastName}".Trim() : null,
                        NewTeacherProfileId = newTeacher.Id,
                        NewTeacherName = $"{newTeacher.User.FirstName} {newTeacher.User.LastName}".Trim(),
                        request.Reason,
                    }),
                    ct);
                await _unitOfWork.SaveChangesAsync(ct);

                return (Booking: booking, Session: demoSession, OldTeacher: oldTeacher, NewTeacher: newTeacher);
            }, cancellationToken);

            // Notify both sides after the reassignment commits -- a retried attempt must not
            // re-send these emails, and an email failure must not undo an already-saved change.
            try
            {
                await _notificationService.SendTemplatedEmailAsync(
                    result.NewTeacher.User.Id,
                    result.NewTeacher.User.Email,
                    NotificationType.BookingConfirmation,
                    "demo-teacher-assigned",
                    new Dictionary<string, string>
                    {
                        ["ChildName"] = result.Booking.ChildName,
                        ["StartAtLocal"] = DateTimeDisplay.ToLocal(result.Session.ScheduledStartAtUtc, result.NewTeacher.User.TimeZoneId),
                        ["EndAtLocal"] = DateTimeDisplay.ToLocal(result.Session.ScheduledEndAtUtc, result.NewTeacher.User.TimeZoneId),
                        ["Reason"] = string.IsNullOrWhiteSpace(request.Reason) ? string.Empty : $"Reason: {request.Reason}",
                        ["JoinUrl"] = await BuildDemoJoinUrlAsync(
                            result.Session,
                            $"{result.NewTeacher.User.FirstName} {result.NewTeacher.User.LastName}".Trim(),
                            result.NewTeacher.User.Email,
                            moderator: true,
                            cancellationToken),
                    },
                    cancellationToken);

                if (result.OldTeacher is not null)
                {
                    await _notificationService.SendTemplatedEmailAsync(
                        result.OldTeacher.User.Id,
                        result.OldTeacher.User.Email,
                        NotificationType.BookingConfirmation,
                        "demo-teacher-unassigned",
                        new Dictionary<string, string>
                        {
                            ["ChildName"] = result.Booking.ChildName,
                            ["StartAtLocal"] = DateTimeDisplay.ToLocal(result.Session.ScheduledStartAtUtc, result.OldTeacher.User.TimeZoneId),
                            ["EndAtLocal"] = DateTimeDisplay.ToLocal(result.Session.ScheduledEndAtUtc, result.OldTeacher.User.TimeZoneId),
                        },
                        cancellationToken);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Teacher reassignment notification failed for booking {BookingId}", bookingId);
            }

            // The room moved to the new teacher's fixed room -- the parent (and every extra
            // invitee) already has a link to the old room, so re-send the demo-confirmed email
            // with the new one or they'd be locked out at demo time.
            await SendParentDemoLinkEmailsAsync(result.Session, result.Booking, cancellationToken);

            return await GetAsync(bookingId, cancellationToken);
        }

        /// <summary>
        /// Manually re-sends the demo's join link to the parent, every extra invitee, and the
        /// assigned teacher -- for when a parent reports never getting (or losing) the original
        /// confirmation email. Always uses the teacher's current fixed room: if this booking
        /// still carries a pre-fixed-link random room (created before this feature shipped), it
        /// is corrected to the teacher's permanent room here, same as CreateAsync/ReassignTeacherAsync.
        /// </summary>
        public async Task<DemoBookingDto> ResendLinkAsync(Guid bookingId, CancellationToken cancellationToken = default)
        {
            var (session, booking, teacher) = await _unitOfWork.ExecuteInSerializableTransactionAsync(async ct =>
            {
                var demoBooking = await _unitOfWork.Repository<DemoBooking>().Query()
                    .Include(b => b.Participants)
                    .FirstOrDefaultAsync(b => b.Id == bookingId, ct)
                    ?? throw new NotFoundException(nameof(DemoBooking), bookingId);

                if (demoBooking.ClassSessionId is not { } sessionId)
                {
                    throw new DomainValidationException("This booking has no linked class session to send a link for.");
                }

                var demoSession = await _unitOfWork.Repository<ClassSession>().TrackedQuery()
                    .FirstOrDefaultAsync(s => s.Id == sessionId, ct)
                    ?? throw new NotFoundException(nameof(ClassSession), sessionId);

                var teacherProfile = await EnsureTeacherMeetingRoomAsync(demoSession.TeacherProfileId, ct);
                demoSession.MeetingRoomId = teacherProfile.User.PersonalMeetingRoomId;
                await _unitOfWork.SaveChangesAsync(ct);

                return (Session: demoSession, Booking: demoBooking, Teacher: teacherProfile);
            }, cancellationToken);

            await SendParentDemoLinkEmailsAsync(session, booking, cancellationToken);
            await SendTeacherDemoLinkEmailAsync(
                session, booking, teacher, "demo-scheduled-teacher",
                new Dictionary<string, string> { ["ParentName"] = booking.ParentName },
                cancellationToken);

            return await GetAsync(bookingId, cancellationToken);
        }

        /// <summary>
        /// The parent's join link for this demo (plus the moment it stops working), for staff to
        /// copy and share manually (WhatsApp, SMS) instead of relying on the email actually
        /// landing. Same room and link-building as the email; does not mutate anything, so unlike
        /// ResendLinkAsync it does not self-heal a pre-fixed-link booking's room -- call
        /// ResendLinkAsync first if that matters here.
        /// </summary>
        public async Task<(string JoinUrl, DateTime ExpiresAtUtc)> GetJoinLinkAsync(Guid bookingId, CancellationToken cancellationToken = default)
        {
            var booking = await _unitOfWork.Repository<DemoBooking>().Query()
                .Include(b => b.ClassSession)
                .FirstOrDefaultAsync(b => b.Id == bookingId, cancellationToken)
                ?? throw new NotFoundException(nameof(DemoBooking), bookingId);

            if (booking.ClassSession is not { } session)
            {
                throw new DomainValidationException("This booking has no linked class session to build a link for.");
            }

            var expiresAtUtc = session.ScheduledEndAtUtc.AddHours(2);
            var joinUrl = await BuildDemoJoinUrlAsync(session, booking.ParentName, booking.ParentEmail, moderator: false, cancellationToken);
            return (joinUrl, expiresAtUtc);
        }

        public async Task<IReadOnlyList<TeacherWorkloadDto>> GetTeacherWorkloadAsync(
            Guid bookingId,
            CancellationToken cancellationToken = default)
        {
            var booking = await _unitOfWork.Repository<DemoBooking>().Query()
                .Include(b => b.ClassSession)
                .FirstOrDefaultAsync(b => b.Id == bookingId, cancellationToken)
                ?? throw new NotFoundException(nameof(DemoBooking), bookingId);

            if (booking.ClassSession is not { } session)
            {
                throw new DomainValidationException("This booking has no linked class session.");
            }

            var dayStart = session.ScheduledStartAtUtc.Date;
            var dayEnd = dayStart.AddDays(1);
            // Mon-Sun week containing the slot, for a stable "this week" figure no matter when it's viewed.
            var mondayOffset = ((int)session.ScheduledStartAtUtc.DayOfWeek + 6) % 7;
            var weekStart = dayStart.AddDays(-mondayOffset);
            var weekEnd = weekStart.AddDays(7);

            var teachers = await _unitOfWork.Repository<TeacherProfile>().Query()
                .Include(t => t.User)
                .Include(t => t.Department)
                .Where(t => t.User.Status == UserStatus.Active)
                .Select(t => new
                {
                    t.Id,
                    Name = t.User.FirstName + " " + t.User.LastName,
                    t.DepartmentId,
                    DepartmentName = t.Department != null ? t.Department.Name : null,
                    IsBusyAtSlot = _unitOfWork.Repository<ClassSession>().Query().Any(
                        s => s.Id != session.Id
                             && s.TeacherProfileId == t.Id
                             && (s.Status == SessionStatus.Scheduled || s.Status == SessionStatus.CarriedForward)
                             && s.ScheduledStartAtUtc < session.ScheduledEndAtUtc
                             && s.ScheduledEndAtUtc > session.ScheduledStartAtUtc),
                    SessionsToday = _unitOfWork.Repository<ClassSession>().Query().Count(
                        s => s.Id != session.Id
                             && s.TeacherProfileId == t.Id
                             && s.ScheduledStartAtUtc >= dayStart
                             && s.ScheduledStartAtUtc < dayEnd),
                    SessionsThisWeek = _unitOfWork.Repository<ClassSession>().Query().Count(
                        s => s.Id != session.Id
                             && s.TeacherProfileId == t.Id
                             && s.ScheduledStartAtUtc >= weekStart
                             && s.ScheduledStartAtUtc < weekEnd),
                })
                .ToListAsync(cancellationToken);

            return teachers
                .OrderBy(t => t.IsBusyAtSlot)
                .ThenBy(t => t.SessionsToday)
                .ThenBy(t => t.SessionsThisWeek)
                .Select(t => new TeacherWorkloadDto
                {
                    TeacherProfileId = t.Id,
                    TeacherName = t.Name,
                    DepartmentId = t.DepartmentId,
                    DepartmentName = t.DepartmentName,
                    IsBusyAtSlot = t.IsBusyAtSlot,
                    SessionsToday = t.SessionsToday,
                    SessionsThisWeek = t.SessionsThisWeek,
                })
                .ToList();
        }

        public async Task<IReadOnlyList<DemoReassignmentHistoryDto>> GetReassignmentHistoryAsync(
            Guid bookingId,
            CancellationToken cancellationToken = default)
        {
            var exists = await _unitOfWork.Repository<DemoBooking>().ExistsAsync(b => b.Id == bookingId, cancellationToken);
            if (!exists)
            {
                throw new NotFoundException(nameof(DemoBooking), bookingId);
            }

            var logs = await _unitOfWork.Repository<AuditLog>().Query()
                .Where(a => a.EntityName == ReassignmentAuditEntityName && a.EntityId == bookingId.ToString())
                .OrderByDescending(a => a.CreatedAtUtc)
                .ToListAsync(cancellationToken);

            if (logs.Count == 0)
            {
                return [];
            }

            var actorIds = logs.Where(l => l.ActorUserId.HasValue).Select(l => l.ActorUserId!.Value).Distinct().ToList();
            var actorNames = await _unitOfWork.Repository<User>().Query()
                .Where(u => actorIds.Contains(u.Id))
                .Select(u => new { u.Id, Name = u.FirstName + " " + u.LastName })
                .ToDictionaryAsync(u => u.Id, u => u.Name, cancellationToken);

            return logs.Select(log =>
            {
                // This audit trail is only ever written by ReassignTeacherAsync above, so a
                // malformed payload shouldn't occur -- but one bad row (e.g. rewritten by an
                // untested future caller) failing to parse must not 500 the entire history for
                // this booking; it just renders with the fields it can't recover blanked out.
                ReassignmentAuditPayload? payload = null;
                if (!string.IsNullOrWhiteSpace(log.ChangesJson))
                {
                    try
                    {
                        payload = JsonSerializer.Deserialize<ReassignmentAuditPayload>(log.ChangesJson);
                    }
                    catch (JsonException)
                    {
                        _logger.LogWarning("Unparseable teacher-reassignment audit payload on log {LogId} for booking {BookingId}", log.Id, bookingId);
                    }
                }

                return new DemoReassignmentHistoryDto
                {
                    Id = log.Id,
                    AtUtc = log.CreatedAtUtc,
                    ActorName = log.ActorUserId.HasValue && actorNames.TryGetValue(log.ActorUserId.Value, out var name) ? name : null,
                    OldTeacherName = payload?.OldTeacherName,
                    NewTeacherName = payload?.NewTeacherName ?? "—",
                    Reason = payload?.Reason,
                };
            }).ToList();
        }

        private sealed record ReassignmentAuditPayload(
            Guid? OldTeacherProfileId, string? OldTeacherName, Guid NewTeacherProfileId, string NewTeacherName, string? Reason);

        public async Task<IReadOnlyList<DemoBookingFollowUpDto>> GetFollowUpNotesAsync(
            Guid bookingId,
            CancellationToken cancellationToken = default)
        {
            var exists = await _unitOfWork.Repository<DemoBooking>().ExistsAsync(b => b.Id == bookingId, cancellationToken);
            if (!exists)
            {
                throw new NotFoundException(nameof(DemoBooking), bookingId);
            }

            var logs = await _unitOfWork.Repository<AuditLog>().Query()
                .Where(a => a.EntityName == FollowUpAuditEntityName && a.EntityId == bookingId.ToString())
                .OrderByDescending(a => a.CreatedAtUtc)
                .ToListAsync(cancellationToken);

            if (logs.Count == 0)
            {
                return [];
            }

            var actorIds = logs.Where(l => l.ActorUserId.HasValue).Select(l => l.ActorUserId!.Value).Distinct().ToList();
            var actorNames = await _unitOfWork.Repository<User>().Query()
                .Where(u => actorIds.Contains(u.Id))
                .Select(u => new { u.Id, Name = u.FirstName + " " + u.LastName })
                .ToDictionaryAsync(u => u.Id, u => u.Name, cancellationToken);

            return logs.Select(log =>
            {
                FollowUpAuditPayload? payload = null;
                if (!string.IsNullOrWhiteSpace(log.ChangesJson))
                {
                    try
                    {
                        payload = JsonSerializer.Deserialize<FollowUpAuditPayload>(log.ChangesJson);
                    }
                    catch (JsonException)
                    {
                        _logger.LogWarning("Unparseable follow-up-note audit payload on log {LogId} for booking {BookingId}", log.Id, bookingId);
                    }
                }

                return new DemoBookingFollowUpDto
                {
                    Id = log.Id,
                    AtUtc = log.CreatedAtUtc,
                    LoggedByName = log.ActorUserId.HasValue && actorNames.TryGetValue(log.ActorUserId.Value, out var name) ? name : null,
                    Note = payload?.Note ?? "—",
                    NextFollowUpOn = payload?.NextFollowUpOn,
                };
            }).ToList();
        }

        private sealed record FollowUpAuditPayload(string? Note, DateOnly? NextFollowUpOn);

        /// <summary>
        /// Every teacher's fixed demo room: their permanent personal meeting room (the same
        /// one GET /api/users/me/meeting-room mints), so a demo's join link is stable across
        /// every booking, reassignment and resend rather than a new random room each time.
        /// Mints the room on first use, same convention as UsersController.MyMeetingRoom.
        /// Caller is responsible for SaveChangesAsync (this may run inside a larger transaction).
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
        /// Builds one recipient's signed join URL for a demo, against the demo's fixed room
        /// (session.MeetingRoomId — the teacher's permanent personal room). No account exists
        /// yet for a demo lead, so each invitee gets their own token (name + email baked in,
        /// expiring a couple of hours past the demo) instead of a bare room name that would
        /// work forever for anyone who ever saw the email.
        /// </summary>
        private async Task<string> BuildDemoJoinUrlAsync(
            ClassSession session, string participantName, string participantEmail, bool moderator, CancellationToken cancellationToken)
        {
            var jitsiConfigJson = await _unitOfWork.Repository<Integration>().Query()
                .Where(i => i.Key == "jitsi")
                .Select(i => i.ConfigJson)
                .FirstOrDefaultAsync(cancellationToken);
            var domain = JitsiLinkBuilder.ResolveDomain(jitsiConfigJson);
            return JitsiLinkBuilder.BuildJoinUrl(
                session.MeetingRoomId,
                jitsiConfigJson,
                _jitsiTokenService.CreateToken(
                    domain, jitsiConfigJson, session.MeetingRoomId!, participantName, participantEmail,
                    moderator, session.ScheduledEndAtUtc.AddHours(2)),
                participantName)
                ?? "#";
        }

        /// <summary>
        /// Sends (or re-sends) the demo's join link to the parent and every extra invitee —
        /// always the same room (session.MeetingRoomId, the teacher's fixed personal room).
        /// Used by CreateAsync, ReassignTeacherAsync (the room changes with the teacher, so the
        /// parent's earlier link goes stale) and ResendLinkAsync, so the three call sites can't
        /// drift. Swallows delivery failures: the write this follows is already committed, and a
        /// template-render glitch or SMTP hiccup must not turn an already-successful write into a
        /// 500 (confirmed via production logs — this exact path once threw an uncaught
        /// SmtpException after the booking had already saved).
        /// </summary>
        private async Task SendParentDemoLinkEmailsAsync(ClassSession session, DemoBooking booking, CancellationToken cancellationToken)
        {
            try
            {
                var (parentSubject, parentHtml) = await _emailTemplateService.RenderAsync(
                    "demo-confirmed",
                    new Dictionary<string, string>
                    {
                        ["ChildName"] = booking.ChildName,
                        ["WhenLocal"] = DateTimeDisplay.ToLocal(session.ScheduledStartAtUtc),
                        ["JoinUrl"] = await BuildDemoJoinUrlAsync(session, booking.ParentName, booking.ParentEmail, moderator: false, cancellationToken),
                    },
                    cancellationToken);
                await _emailSender.SendAsync(booking.ParentEmail, parentSubject, parentHtml, cancellationToken);
                foreach (var participant in booking.Participants.Where(p => !string.IsNullOrWhiteSpace(p.Email)))
                {
                    var (participantSubject, participantHtml) = await _emailTemplateService.RenderAsync(
                        "demo-confirmed",
                        new Dictionary<string, string>
                        {
                            ["ChildName"] = booking.ChildName,
                            ["WhenLocal"] = DateTimeDisplay.ToLocal(session.ScheduledStartAtUtc),
                            ["JoinUrl"] = await BuildDemoJoinUrlAsync(session, participant.Name, participant.Email!, moderator: false, cancellationToken),
                        },
                        cancellationToken);
                    await _emailSender.SendAsync(participant.Email!, participantSubject, participantHtml, cancellationToken);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Demo link email delivery to parent/participants failed for booking {BookingId}", booking.Id);
            }
        }

        /// <summary>
        /// Sends the assigned teacher their copy of the demo's join link (same room as the
        /// parent, a moderator token) using the given template plus whatever extra placeholders
        /// that template needs beyond ChildName/WhenLocal/JoinUrl. Swallows delivery failures for
        /// the same reason as SendParentDemoLinkEmailsAsync.
        /// </summary>
        private async Task SendTeacherDemoLinkEmailAsync(
            ClassSession session,
            DemoBooking booking,
            TeacherProfile teacher,
            string templateKey,
            Dictionary<string, string>? extraPlaceholders,
            CancellationToken cancellationToken)
        {
            try
            {
                var teacherName = $"{teacher.User.FirstName} {teacher.User.LastName}".Trim();
                var placeholders = new Dictionary<string, string>
                {
                    ["ChildName"] = booking.ChildName,
                    ["WhenLocal"] = DateTimeDisplay.ToLocal(session.ScheduledStartAtUtc, teacher.User.TimeZoneId),
                    ["JoinUrl"] = await BuildDemoJoinUrlAsync(session, teacherName, teacher.User.Email, moderator: true, cancellationToken),
                };
                if (extraPlaceholders is not null)
                {
                    foreach (var (key, value) in extraPlaceholders)
                    {
                        placeholders[key] = value;
                    }
                }

                var (subject, html) = await _emailTemplateService.RenderAsync(templateKey, placeholders, cancellationToken);
                await _emailSender.SendAsync(teacher.User.Email, subject, html, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Demo link email delivery to teacher failed for booking {BookingId}", booking.Id);
            }
        }

        private IQueryable<DemoBooking> BaseQuery()
        {
            return _unitOfWork.Repository<DemoBooking>().Query()
                .Include(b => b.ClassSession!).ThenInclude(s => s.TeacherProfile).ThenInclude(t => t.User)
                .Include(b => b.Participants)
                .Include(b => b.Department);
        }
    }
}
