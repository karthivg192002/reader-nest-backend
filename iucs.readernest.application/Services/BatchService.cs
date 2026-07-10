using iucs.readernest.application.Common.Exceptions;
using iucs.readernest.application.Dto.Batches;
using iucs.readernest.application.Mappings;
using iucs.readernest.domain.Entities.Academics;
using iucs.readernest.domain.Entities.Users;
using iucs.readernest.domain.Enums;
using iucs.readernest.domain.Repository;
using Microsoft.EntityFrameworkCore;

namespace iucs.readernest.application.Services
{
    public class BatchService : IBatchService
    {
        private readonly IUnitOfWork _unitOfWork;
        private readonly IAuditLogService _auditLog;

        public BatchService(IUnitOfWork unitOfWork, IAuditLogService auditLog)
        {
            _unitOfWork = unitOfWork;
            _auditLog = auditLog;
        }

        public async Task<IReadOnlyList<BatchDto>> ListAsync(BatchStatus? status, CancellationToken cancellationToken = default)
        {
            var query = BaseQuery();
            if (status.HasValue)
            {
                query = query.Where(b => b.Status == status.Value);
            }

            var batches = await query.OrderBy(b => b.Name).ToListAsync(cancellationToken);
            return batches.Select(b => b.ToDto(b.Enrollments.Count)).ToList();
        }

        public async Task<BatchDto> GetAsync(Guid id, CancellationToken cancellationToken = default)
        {
            var batch = await BaseQuery().FirstOrDefaultAsync(b => b.Id == id, cancellationToken)
                ?? throw new NotFoundException(nameof(Batch), id);

            return batch.ToDto(batch.Enrollments.Count);
        }

        public async Task<BatchDto> CreateAsync(SaveBatchRequest request, CancellationToken cancellationToken = default)
        {
            var course = await ValidateAsync(request, cancellationToken);

            var batch = new Batch
            {
                CourseId = request.CourseId,
                TeacherProfileId = request.TeacherProfileId,
                Name = request.Name.Trim(),
                // Individual courses always run one student per batch
                Capacity = course.Type == CourseType.Individual ? 1 : request.Capacity,
                StartDate = request.StartDate,
                EndDate = request.EndDate,
            };
            await _unitOfWork.Repository<Batch>().AddAsync(batch, cancellationToken);
            await _auditLog.StageAsync(AuditAction.Create, nameof(Batch), batch.Id.ToString(), cancellationToken: cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return await GetAsync(batch.Id, cancellationToken);
        }

        public async Task<BatchDto> UpdateAsync(Guid id, SaveBatchRequest request, CancellationToken cancellationToken = default)
        {
            var course = await ValidateAsync(request, cancellationToken);
            var batch = await _unitOfWork.Repository<Batch>().GetByIdAsync(id, cancellationToken)
                ?? throw new NotFoundException(nameof(Batch), id);

            batch.CourseId = request.CourseId;
            batch.TeacherProfileId = request.TeacherProfileId;
            batch.Name = request.Name.Trim();
            batch.Capacity = course.Type == CourseType.Individual ? 1 : request.Capacity;
            batch.StartDate = request.StartDate;
            batch.EndDate = request.EndDate;

            await _auditLog.StageAsync(AuditAction.Update, nameof(Batch), batch.Id.ToString(), cancellationToken: cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return await GetAsync(batch.Id, cancellationToken);
        }

        public async Task<BatchDto> SetStatusAsync(Guid id, BatchStatus status, CancellationToken cancellationToken = default)
        {
            var batch = await _unitOfWork.Repository<Batch>().GetByIdAsync(id, cancellationToken)
                ?? throw new NotFoundException(nameof(Batch), id);

            batch.Status = status;
            if (status == BatchStatus.Dormant && batch.CompletedAtUtc is null)
            {
                // Anchors the 15-day recording access window for this batch
                batch.CompletedAtUtc = DateTime.UtcNow;
            }

            await _auditLog.StageAsync(AuditAction.Update, nameof(Batch), batch.Id.ToString(), cancellationToken: cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return await GetAsync(batch.Id, cancellationToken);
        }

        private IQueryable<Batch> BaseQuery()
        {
            return _unitOfWork.Repository<Batch>().Query()
                .Include(b => b.Course)
                .Include(b => b.TeacherProfile).ThenInclude(t => t.User)
                .Include(b => b.Enrollments);
        }

        private async Task<Course> ValidateAsync(SaveBatchRequest request, CancellationToken cancellationToken)
        {
            var course = await _unitOfWork.Repository<Course>().GetByIdAsync(request.CourseId, cancellationToken)
                ?? throw new NotFoundException(nameof(Course), request.CourseId);

            var teacherExists = await _unitOfWork.Repository<TeacherProfile>()
                .ExistsAsync(t => t.Id == request.TeacherProfileId, cancellationToken);
            if (!teacherExists)
            {
                throw new NotFoundException(nameof(TeacherProfile), request.TeacherProfileId);
            }

            return course;
        }
    }
}
