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

        private IQueryable<DemoBooking> BaseQuery()
        {
            return _unitOfWork.Repository<DemoBooking>().Query()
                .Include(b => b.ClassSession)
                .Include(b => b.Participants);
        }
    }
}
