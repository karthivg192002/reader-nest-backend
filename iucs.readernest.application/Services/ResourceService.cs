using iucs.readernest.application.Common.Exceptions;
using iucs.readernest.application.Dto.Resources;
using iucs.readernest.application.Mappings;
using iucs.readernest.domain.Entities.Academics;
using iucs.readernest.domain.Entities.Resources;
using iucs.readernest.domain.Entities.Users;
using iucs.readernest.domain.Enums;
using iucs.readernest.domain.Repository;
using Microsoft.EntityFrameworkCore;

namespace iucs.readernest.application.Services
{
    public class ResourceService : IResourceService
    {
        private readonly IUnitOfWork _unitOfWork;
        private readonly IAuditLogService _auditLog;

        public ResourceService(IUnitOfWork unitOfWork, IAuditLogService auditLog)
        {
            _unitOfWork = unitOfWork;
            _auditLog = auditLog;
        }

        public async Task<IReadOnlyList<ResourceDto>> ListAsync(
            ResourceType? type,
            CancellationToken cancellationToken = default)
        {
            var query = _unitOfWork.Repository<Resource>().Query();
            if (type.HasValue)
            {
                query = query.Where(r => r.Type == type.Value);
            }

            var resources = await query.OrderByDescending(r => r.CreatedAtUtc).ToListAsync(cancellationToken);
            return resources.Select(r => r.ToDto()).ToList();
        }

        public async Task<IReadOnlyList<ResourceDto>> ListForTeacherUserAsync(
            Guid userId,
            ResourceType? type,
            CancellationToken cancellationToken = default)
        {
            var (batchIds, courseIds) = await ResolveTeacherScopeAsync(userId, cancellationToken);

            var query = _unitOfWork.Repository<Resource>().Query()
                .Include(r => r.Batch)
                .Where(r =>
                    (r.BatchId != null && batchIds.Contains(r.BatchId.Value)) ||
                    (r.BatchId == null && r.CourseId != null && courseIds.Contains(r.CourseId.Value)));

            if (type.HasValue)
            {
                query = query.Where(r => r.Type == type.Value);
            }

            var resources = await query.OrderByDescending(r => r.CreatedAtUtc).ToListAsync(cancellationToken);
            return resources.Select(r => r.ToDto()).ToList();
        }

        public async Task<ResourceDto> CreateAsync(
            CreateResourceRequest request,
            string storedRelativePath,
            string? mimeType,
            long sizeBytes,
            CancellationToken cancellationToken = default)
        {
            var resource = new Resource
            {
                Title = request.Title.Trim(),
                Type = request.Type,
                FileUrl = storedRelativePath,
                MimeType = mimeType,
                FileSizeBytes = sizeBytes,
                CourseId = request.CourseId,
                BatchId = request.BatchId,
                // Business rule: reading books are view-only regardless of the flag sent
                IsDownloadable = request.Type == ResourceType.ReadingBook ? false : request.IsDownloadable,
                Description = request.Description,
            };
            await _unitOfWork.Repository<Resource>().AddAsync(resource, cancellationToken);
            await _auditLog.StageAsync(AuditAction.Create, nameof(Resource), resource.Id.ToString(), cancellationToken: cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return resource.ToDto();
        }

        public async Task<ResourceDto> CreateForTeacherUserAsync(
            Guid userId,
            CreateResourceRequest request,
            string storedRelativePath,
            string? mimeType,
            long sizeBytes,
            CancellationToken cancellationToken = default)
        {
            var (batchIds, _) = await ResolveTeacherScopeAsync(userId, cancellationToken);
            if (!request.BatchId.HasValue || !batchIds.Contains(request.BatchId.Value))
            {
                throw new ForbiddenException("You can only upload resources to your own batches.");
            }

            return await CreateAsync(request, storedRelativePath, mimeType, sizeBytes, cancellationToken);
        }

        public async Task<Resource> GetForDownloadAsync(Guid id, CancellationToken cancellationToken = default)
        {
            var resource = await _unitOfWork.Repository<Resource>().GetByIdAsync(id, cancellationToken)
                ?? throw new NotFoundException(nameof(Resource), id);

            await _auditLog.StageAsync(AuditAction.Access, nameof(Resource), resource.Id.ToString(), cancellationToken: cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return resource;
        }

        public async Task<Resource> GetForTeacherDownloadAsync(Guid userId, Guid id, CancellationToken cancellationToken = default)
        {
            var (batchIds, courseIds) = await ResolveTeacherScopeAsync(userId, cancellationToken);

            var resource = await _unitOfWork.Repository<Resource>().GetByIdAsync(id, cancellationToken)
                ?? throw new NotFoundException(nameof(Resource), id);

            var owns =
                (resource.BatchId.HasValue && batchIds.Contains(resource.BatchId.Value)) ||
                (!resource.BatchId.HasValue && resource.CourseId.HasValue && courseIds.Contains(resource.CourseId.Value));
            if (!owns)
            {
                throw new ForbiddenException("This resource is not tied to one of your batches.");
            }

            await _auditLog.StageAsync(AuditAction.Access, nameof(Resource), resource.Id.ToString(), cancellationToken: cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return resource;
        }

        /// <summary>Resolves the teacher's batch ids and the distinct course ids of those batches.</summary>
        private async Task<(List<Guid> BatchIds, List<Guid> CourseIds)> ResolveTeacherScopeAsync(
            Guid userId,
            CancellationToken cancellationToken)
        {
            var teacher = await _unitOfWork.Repository<TeacherProfile>()
                .FirstOrDefaultAsync(t => t.UserId == userId, cancellationToken)
                ?? throw new NotFoundException("No teacher profile is linked to the current account.");

            var batches = await _unitOfWork.Repository<Batch>().Query()
                .Where(b => b.TeacherProfileId == teacher.Id)
                .Select(b => new { b.Id, b.CourseId })
                .ToListAsync(cancellationToken);

            return (
                batches.Select(b => b.Id).ToList(),
                batches.Select(b => b.CourseId).Distinct().ToList());
        }

        public async Task GrantAccessAsync(
            Guid resourceId,
            GrantResourceAccessRequest request,
            CancellationToken cancellationToken = default)
        {
            var resourceExists = await _unitOfWork.Repository<Resource>()
                .ExistsAsync(r => r.Id == resourceId, cancellationToken);
            if (!resourceExists)
            {
                throw new NotFoundException(nameof(Resource), resourceId);
            }

            var accessRepository = _unitOfWork.Repository<ResourceAccess>();
            foreach (var parentProfileId in request.ParentProfileIds.Distinct())
            {
                var parentExists = await _unitOfWork.Repository<ParentProfile>()
                    .ExistsAsync(p => p.Id == parentProfileId, cancellationToken);
                if (!parentExists)
                {
                    throw new NotFoundException(nameof(ParentProfile), parentProfileId);
                }

                var existing = await accessRepository.FirstOrDefaultAsync(
                    a => a.ResourceId == resourceId && a.ParentProfileId == parentProfileId,
                    cancellationToken);

                if (existing is null)
                {
                    await accessRepository.AddAsync(
                        new ResourceAccess
                        {
                            ResourceId = resourceId,
                            ParentProfileId = parentProfileId,
                            VisibleOnDashboard = request.VisibleOnDashboard,
                        },
                        cancellationToken);
                }
                else
                {
                    existing.VisibleOnDashboard = request.VisibleOnDashboard;
                }
            }

            await _auditLog.StageAsync(AuditAction.Update, nameof(ResourceAccess), resourceId.ToString(), cancellationToken: cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }
    }
}
