using iucs.readernest.application.Common.Exceptions;
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

        public DemoBookingService(IUnitOfWork unitOfWork, IAuditLogService auditLog)
        {
            _unitOfWork = unitOfWork;
            _auditLog = auditLog;
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

            var teacherExists = await _unitOfWork.Repository<TeacherProfile>()
                .ExistsAsync(t => t.Id == request.TeacherProfileId, cancellationToken);
            if (!teacherExists)
            {
                throw new NotFoundException(nameof(TeacherProfile), request.TeacherProfileId);
            }

            // Demos are always one-time sessions, never recurring, and have no batch
            var session = new ClassSession
            {
                TeacherProfileId = request.TeacherProfileId,
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
                    .Select(p => new DemoParticipant { Name = p.Name.Trim(), Email = p.Email.Trim().ToLowerInvariant(), Phone = p.Phone })
                    .ToList(),
            };
            await _unitOfWork.Repository<DemoBooking>().AddAsync(booking, cancellationToken);

            await _auditLog.StageAsync(AuditAction.Create, nameof(DemoBooking), booking.Id.ToString(), cancellationToken: cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return await GetAsync(booking.Id, cancellationToken);
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

        private IQueryable<DemoBooking> BaseQuery()
        {
            return _unitOfWork.Repository<DemoBooking>().Query()
                .Include(b => b.ClassSession)
                .Include(b => b.Participants);
        }
    }
}
