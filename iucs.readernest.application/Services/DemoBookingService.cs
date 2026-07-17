using iucs.readernest.application.Common.Exceptions;
using iucs.readernest.application.Common.Interfaces;
using iucs.readernest.application.Dto.Admission;
using iucs.readernest.application.Mappings;
using iucs.readernest.domain.Entities.Admission;
using iucs.readernest.domain.Entities.Sessions;
using iucs.readernest.domain.Entities.Users;
using iucs.readernest.domain.Enums;
using iucs.readernest.domain.Repository;
using Microsoft.EntityFrameworkCore;

namespace iucs.readernest.application.Services
{
    public class DemoBookingService : IDemoBookingService
    {
        private readonly IUnitOfWork _unitOfWork;
        private readonly IAuditLogService _auditLog;
        private readonly ICrmNotifier _crmNotifier;
        private readonly IEmailSender _emailSender;

        public DemoBookingService(
            IUnitOfWork unitOfWork,
            IAuditLogService auditLog,
            IEmailSender emailSender,
            ICrmNotifier crmNotifier)
        {
            _unitOfWork = unitOfWork;
            _auditLog = auditLog;
            _emailSender = emailSender;
            _crmNotifier = crmNotifier;
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

            Guid teacherProfileId;
            if (request.TeacherProfileId.HasValue)
            {
                var teacherExists = await _unitOfWork.Repository<TeacherProfile>()
                    .ExistsAsync(t => t.Id == request.TeacherProfileId.Value, cancellationToken);
                if (!teacherExists)
                {
                    throw new NotFoundException(nameof(TeacherProfile), request.TeacherProfileId.Value);
                }

                teacherProfileId = request.TeacherProfileId.Value;
            }
            else
            {
                teacherProfileId = await AutoAssignTeacherAsync(request, cancellationToken);
            }

            // Demos are always one-time sessions, never recurring, and have no batch
            var session = new ClassSession
            {
                TeacherProfileId = teacherProfileId,
                Type = SessionType.Demo,
                ScheduledStartAtUtc = request.ScheduledStartAtUtc,
                ScheduledEndAtUtc = request.ScheduledEndAtUtc,
                MeetingRoomId = $"trn-demo-{Guid.NewGuid():N}",
            };
            await _unitOfWork.Repository<ClassSession>().AddAsync(session, cancellationToken);

            var booking = new DemoBooking
            {
                ClassSession = session,
                ParentName = request.ParentName.Trim(),
                ParentEmail = request.ParentEmail.Trim().ToLowerInvariant(),
                ParentPhone = request.ParentPhone,
                ChildName = request.ChildName.Trim(),
                ChildAge = request.ChildAge,
                Department = request.Department,
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
            await _unitOfWork.Repository<DemoBooking>().AddAsync(booking, cancellationToken);

            await _auditLog.StageAsync(AuditAction.Create, nameof(DemoBooking), booking.Id.ToString(), cancellationToken: cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            // Booking confirmation to the parent and every extra invitee (they may not
            // have accounts yet, so this bypasses the user-bound notification log)
            var when = $"{request.ScheduledStartAtUtc:u}";
            var confirmation = $"Your demo class for {booking.ChildName} is confirmed for {when}. A join link follows before the session.";
            await _emailSender.SendAsync(booking.ParentEmail, "Demo class confirmed", confirmation, cancellationToken);
            foreach (var participant in booking.Participants.Where(p => !string.IsNullOrWhiteSpace(p.Email)))
            {
                await _emailSender.SendAsync(participant.Email!, "Demo class confirmed", confirmation, cancellationToken);
            }

            // New lead lands in the client's CRM (no-op when no webhook is configured)
            await _crmNotifier.PushLeadEventAsync("lead.created", new
            {
                booking.Id,
                booking.ParentName,
                booking.ParentEmail,
                booking.ParentPhone,
                booking.ChildName,
                Department = booking.Department?.ToString(),
                DemoAtUtc = request.ScheduledStartAtUtc,
            }, cancellationToken);

            return await GetAsync(booking.Id, cancellationToken);
        }

        /// <summary>Auto-assign: the department-matched active teacher who is free at the slot with the lightest day.</summary>
        private async Task<Guid> AutoAssignTeacherAsync(CreateDemoBookingRequest request, CancellationToken cancellationToken)
        {
            IQueryable<TeacherProfile> teachers = _unitOfWork.Repository<TeacherProfile>().Query()
                .Where(t => t.User.Status == UserStatus.Active);
            if (request.Department.HasValue)
            {
                teachers = teachers.Where(t => t.Department == request.Department.Value);
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

            booking.ConversionStatus = request.ConversionStatus;
            if (request.FollowUpNotes is not null)
            {
                booking.FollowUpNotes = request.FollowUpNotes;
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
                TeacherName = $"{feedback.TeacherProfile.User.FirstName} {feedback.TeacherProfile.User.LastName}",
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

        private IQueryable<DemoBooking> BaseQuery()
        {
            return _unitOfWork.Repository<DemoBooking>().Query()
                .Include(b => b.ClassSession!).ThenInclude(s => s.TeacherProfile).ThenInclude(t => t.User)
                .Include(b => b.Participants);
        }
    }
}
