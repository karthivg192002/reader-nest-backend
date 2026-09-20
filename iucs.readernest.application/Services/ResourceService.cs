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

            var visibleResourceIds = _unitOfWork.Repository<ResourceBatchVisibility>().Query()
                .Where(v => batchIds.Contains(v.BatchId))
                .Select(v => v.ResourceId);

            var query = _unitOfWork.Repository<Resource>().Query()
                .Include(r => r.Batch)
                .Where(r =>
                    (r.BatchId != null && batchIds.Contains(r.BatchId.Value)) ||
                    (r.BatchId == null && r.CourseId != null && courseIds.Contains(r.CourseId.Value)) ||
                    visibleResourceIds.Contains(r.Id));

            if (type.HasValue)
            {
                query = query.Where(r => r.Type == type.Value);
            }

            var resources = await query.OrderByDescending(r => r.CreatedAtUtc).ToListAsync(cancellationToken);
            return await WithVisibleBatchNamesAsync(resources, cancellationToken);
        }

        /// <summary>Maps to DTOs with every visible batch's name resolved (multi-batch visibility).</summary>
        private async Task<IReadOnlyList<ResourceDto>> WithVisibleBatchNamesAsync(
            List<Resource> resources,
            CancellationToken cancellationToken)
        {
            var ids = resources.Select(r => r.Id).ToList();
            var names = await _unitOfWork.Repository<ResourceBatchVisibility>().Query()
                .Where(v => ids.Contains(v.ResourceId))
                .Select(v => new { v.ResourceId, v.Batch.Name })
                .ToListAsync(cancellationToken);
            var byResource = names.GroupBy(n => n.ResourceId)
                .ToDictionary(g => g.Key, g => (IReadOnlyList<string>)g.Select(n => n.Name).Distinct().ToList());

            return resources.Select(r =>
            {
                var dto = r.ToDto();
                if (byResource.TryGetValue(r.Id, out var batchNames))
                {
                    dto.VisibleBatchNames = batchNames;
                }
                else if (dto.BatchName is not null)
                {
                    dto.VisibleBatchNames = [dto.BatchName];
                }

                return dto;
            }).ToList();
        }

        public async Task<ResourceDto> CreateAsync(
            CreateResourceRequest request,
            string storedRelativePath,
            string? mimeType,
            long sizeBytes,
            CancellationToken cancellationToken = default)
        {
            // Multi-batch visibility: the uploader picks which batch(es) see the resource;
            // the legacy single BatchId stays as the primary batch for display.
            var visibleBatchIds = request.BatchIds.Distinct().ToList();
            if (request.BatchId.HasValue && !visibleBatchIds.Contains(request.BatchId.Value))
            {
                visibleBatchIds.Insert(0, request.BatchId.Value);
            }

            // CourseId/BatchId(s) were never checked against the DB before being written into
            // Resource/ResourceBatchVisibility rows that reference them — a stale dropdown
            // value (or one that got deleted between page-load and submit) hit the FK
            // constraint at SaveChanges as an unhandled DbUpdateException, surfacing as a raw
            // 500 with no indication of what was actually wrong. Checked up front instead, so
            // it's a clean 404 naming the missing id.
            if (request.CourseId.HasValue
                && !await _unitOfWork.Repository<Course>().ExistsAsync(c => c.Id == request.CourseId.Value, cancellationToken))
            {
                throw new NotFoundException(nameof(Course), request.CourseId.Value);
            }
            foreach (var batchId in visibleBatchIds)
            {
                if (!await _unitOfWork.Repository<Batch>().ExistsAsync(b => b.Id == batchId, cancellationToken))
                {
                    throw new NotFoundException(nameof(Batch), batchId);
                }
            }
            if (request.FolderId.HasValue
                && !await _unitOfWork.Repository<ResourceFolder>().ExistsAsync(f => f.Id == request.FolderId.Value, cancellationToken))
            {
                throw new NotFoundException(nameof(ResourceFolder), request.FolderId.Value);
            }

            var resource = new Resource
            {
                FolderId = request.FolderId,
                Title = request.Title.Trim(),
                Type = request.Type,
                FileUrl = storedRelativePath,
                MimeType = mimeType,
                FileSizeBytes = sizeBytes,
                CourseId = request.CourseId,
                BatchId = request.BatchId ?? (visibleBatchIds.Count > 0 ? visibleBatchIds[0] : null),
                // Business rule: reading books are view-only regardless of the flag sent
                IsDownloadable = request.Type == ResourceType.ReadingBook ? false : request.IsDownloadable,
                Description = request.Description,
            };
            await _unitOfWork.Repository<Resource>().AddAsync(resource, cancellationToken);
            foreach (var batchId in visibleBatchIds)
            {
                await _unitOfWork.Repository<ResourceBatchVisibility>().AddAsync(
                    new ResourceBatchVisibility { Resource = resource, BatchId = batchId }, cancellationToken);
            }

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
            var requested = request.BatchIds.Concat(request.BatchId.HasValue ? [request.BatchId.Value] : []).Distinct().ToList();
            if (requested.Count == 0 || requested.Any(id => !batchIds.Contains(id)))
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
                (!resource.BatchId.HasValue && resource.CourseId.HasValue && courseIds.Contains(resource.CourseId.Value)) ||
                await _unitOfWork.Repository<ResourceBatchVisibility>()
                    .ExistsAsync(v => v.ResourceId == id && batchIds.Contains(v.BatchId), cancellationToken);
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
            var parentProfileIds = request.ParentProfileIds.Distinct().ToList();

            // Two set-based lookups for the whole grant instead of two per parent id:
            // which of the requested parents actually exist, and which already have a row
            // for this resource. TrackedQuery so the existing rows below can be mutated
            // in place and persisted by the single SaveChangesAsync at the end.
            var knownParentIds = await _unitOfWork.Repository<ParentProfile>().Query()
                .Where(p => parentProfileIds.Contains(p.Id))
                .Select(p => p.Id)
                .ToListAsync(cancellationToken);
            var knownParentIdSet = knownParentIds.ToHashSet();

            var existingByParentId = await accessRepository.TrackedQuery()
                .Where(a => a.ResourceId == resourceId && parentProfileIds.Contains(a.ParentProfileId))
                .ToDictionaryAsync(a => a.ParentProfileId, cancellationToken);

            foreach (var parentProfileId in parentProfileIds)
            {
                if (!knownParentIdSet.Contains(parentProfileId))
                {
                    throw new NotFoundException(nameof(ParentProfile), parentProfileId);
                }

                existingByParentId.TryGetValue(parentProfileId, out var existing);

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

        public async Task<ResourceDto> UpdateAsync(
            Guid id,
            UpdateResourceRequest request,
            CancellationToken cancellationToken = default)
        {
            var resource = await _unitOfWork.Repository<Resource>().GetByIdAsync(id, cancellationToken)
                ?? throw new NotFoundException(nameof(Resource), id);

            // Same rule CreateAsync enforces: reading books never become downloadable.
            resource.IsDownloadable = resource.Type == ResourceType.ReadingBook ? false : request.IsDownloadable;

            await _auditLog.StageAsync(AuditAction.Update, nameof(Resource), resource.Id.ToString(), cancellationToken: cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return resource.ToDto();
        }
    }
}
