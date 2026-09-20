using System.Text.Json;
using iucs.readernest.application.Common.Exceptions;
using iucs.readernest.application.Dto.Resources;
using iucs.readernest.application.Helper;
using iucs.readernest.application.Mappings;
using iucs.readernest.domain.Entities.Resources;
using iucs.readernest.domain.Entities.Users;
using iucs.readernest.domain.Enums;
using iucs.readernest.domain.Repository;
using Microsoft.EntityFrameworkCore;

namespace iucs.readernest.application.Services
{
    public class ResourceFolderService : IResourceFolderService
    {
        private readonly IUnitOfWork _unitOfWork;
        private readonly IAuditLogService _auditLog;

        public ResourceFolderService(IUnitOfWork unitOfWork, IAuditLogService auditLog)
        {
            _unitOfWork = unitOfWork;
            _auditLog = auditLog;
        }

        public async Task<IReadOnlyList<ResourceFolderDto>> ListAsync(CancellationToken cancellationToken = default)
        {
            var folders = await _unitOfWork.Repository<ResourceFolder>().Query()
                .OrderBy(f => f.Name)
                .ToListAsync(cancellationToken);

            var fileCounts = await _unitOfWork.Repository<Resource>().Query()
                .Where(r => r.FolderId != null)
                .GroupBy(r => r.FolderId!.Value)
                .Select(g => new { FolderId = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.FolderId, x => x.Count, cancellationToken);
            var sharedCounts = await _unitOfWork.Repository<ResourceFolderAccess>().Query()
                .GroupBy(a => a.FolderId)
                .Select(g => new { FolderId = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.FolderId, x => x.Count, cancellationToken);

            return folders.Select(f => ToDto(
                f,
                fileCounts.GetValueOrDefault(f.Id),
                folders.Count(c => c.ParentFolderId == f.Id),
                sharedCounts.GetValueOrDefault(f.Id))).ToList();
        }

        public async Task<ResourceFolderDto> CreateAsync(CreateResourceFolderRequest request, CancellationToken cancellationToken = default)
        {
            var name = request.Name.Trim();
            if (name.Length == 0)
            {
                throw new DomainValidationException("Give the folder a name.");
            }

            if (request.ParentFolderId.HasValue)
            {
                await RequireFolderAsync(request.ParentFolderId.Value, cancellationToken);
            }
            await EnsureNameFreeAsync(name, request.ParentFolderId, exceptFolderId: null, cancellationToken);

            var folder = new ResourceFolder { Name = name, Description = Clean(request.Description), ParentFolderId = request.ParentFolderId };
            await _unitOfWork.Repository<ResourceFolder>().AddAsync(folder, cancellationToken);
            await _auditLog.StageAsync(AuditAction.Create, nameof(ResourceFolder), folder.Id.ToString(), cancellationToken: cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            return ToDto(folder, 0, 0, 0);
        }

        public async Task<ResourceFolderDto> UpdateAsync(Guid id, UpdateResourceFolderRequest request, CancellationToken cancellationToken = default)
        {
            var folder = await _unitOfWork.Repository<ResourceFolder>().TrackedQuery()
                .FirstOrDefaultAsync(f => f.Id == id, cancellationToken)
                ?? throw new NotFoundException(nameof(ResourceFolder), id);

            var name = request.Name.Trim();
            if (name.Length == 0)
            {
                throw new DomainValidationException("Give the folder a name.");
            }

            if (request.ParentFolderId != folder.ParentFolderId)
            {
                if (request.ParentFolderId.HasValue)
                {
                    await RequireFolderAsync(request.ParentFolderId.Value, cancellationToken);
                    var all = await LoadTreeAsync(cancellationToken);
                    // A folder can't be moved into itself or anything beneath it.
                    if (ResourceFolderTree.WithDescendants(all, [id]).Contains(request.ParentFolderId.Value))
                    {
                        throw new DomainValidationException("A folder can't be moved into itself or one of its own subfolders.");
                    }
                }
                folder.ParentFolderId = request.ParentFolderId;
            }

            await EnsureNameFreeAsync(name, folder.ParentFolderId, exceptFolderId: id, cancellationToken);
            folder.Name = name;
            folder.Description = Clean(request.Description);
            _unitOfWork.Repository<ResourceFolder>().Update(folder);
            await _auditLog.StageAsync(AuditAction.Update, nameof(ResourceFolder), folder.Id.ToString(), cancellationToken: cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            var files = await _unitOfWork.Repository<Resource>().Query().CountAsync(r => r.FolderId == id, cancellationToken);
            var subs = await _unitOfWork.Repository<ResourceFolder>().Query().CountAsync(f => f.ParentFolderId == id, cancellationToken);
            var shared = await _unitOfWork.Repository<ResourceFolderAccess>().Query().CountAsync(a => a.FolderId == id, cancellationToken);
            return ToDto(folder, files, subs, shared);
        }

        public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
        {
            var folder = await _unitOfWork.Repository<ResourceFolder>().TrackedQuery()
                .FirstOrDefaultAsync(f => f.Id == id, cancellationToken)
                ?? throw new NotFoundException(nameof(ResourceFolder), id);

            if (await _unitOfWork.Repository<Resource>().ExistsAsync(r => r.FolderId == id, cancellationToken)
                || await _unitOfWork.Repository<ResourceFolder>().ExistsAsync(f => f.ParentFolderId == id, cancellationToken))
            {
                throw new DomainValidationException("This folder isn't empty. Move or delete its files and subfolders first.");
            }

            var access = await _unitOfWork.Repository<ResourceFolderAccess>().TrackedQuery()
                .Where(a => a.FolderId == id).ToListAsync(cancellationToken);
            foreach (var row in access)
            {
                _unitOfWork.Repository<ResourceFolderAccess>().Remove(row);
            }

            _unitOfWork.Repository<ResourceFolder>().Remove(folder);
            await _auditLog.StageAsync(AuditAction.Delete, nameof(ResourceFolder), id.ToString(), cancellationToken: cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }

        public async Task<IReadOnlyList<ResourceFolderAccessDto>> ListAccessAsync(Guid folderId, CancellationToken cancellationToken = default)
        {
            await RequireFolderAsync(folderId, cancellationToken);
            return await ReadAccessAsync(folderId, cancellationToken);
        }

        public async Task<IReadOnlyList<ResourceFolderAccessDto>> SetAccessAsync(
            Guid folderId, Guid actorUserId, SetResourceFolderAccessRequest request, CancellationToken cancellationToken = default)
        {
            await RequireFolderAsync(folderId, cancellationToken);
            var toAdd = request.AddParentProfileIds.Distinct().ToList();
            var toRemove = request.RemoveParentProfileIds.Distinct().Except(toAdd).ToList();

            var repo = _unitOfWork.Repository<ResourceFolderAccess>();
            var existing = await repo.TrackedQuery().Where(a => a.FolderId == folderId).ToListAsync(cancellationToken);

            if (toAdd.Count > 0)
            {
                var known = (await _unitOfWork.Repository<ParentProfile>().Query()
                        .Where(p => toAdd.Contains(p.Id)).Select(p => p.Id).ToListAsync(cancellationToken)).ToHashSet();
                var missing = toAdd.FirstOrDefault(p => !known.Contains(p));
                if (missing != Guid.Empty)
                {
                    throw new NotFoundException(nameof(ParentProfile), missing);
                }

                var already = existing.Select(a => a.ParentProfileId).ToHashSet();
                foreach (var parentId in toAdd.Where(p => !already.Contains(p)))
                {
                    await repo.AddAsync(new ResourceFolderAccess { FolderId = folderId, ParentProfileId = parentId, GrantedBy = actorUserId }, cancellationToken);
                }
            }

            foreach (var row in existing.Where(a => toRemove.Contains(a.ParentProfileId)))
            {
                repo.Remove(row);
            }

            if (toAdd.Count > 0 || toRemove.Count > 0)
            {
                await _auditLog.StageAsync(
                    AuditAction.Update,
                    "ResourceFolderSharing",
                    folderId.ToString(),
                    JsonSerializer.Serialize(new { shared = toAdd, unshared = toRemove }),
                    cancellationToken);
                await _unitOfWork.SaveChangesAsync(cancellationToken);
            }

            return await ReadAccessAsync(folderId, cancellationToken);
        }

        public async Task<ResourceDto> MoveResourceAsync(Guid resourceId, MoveResourceRequest request, CancellationToken cancellationToken = default)
        {
            var resource = await _unitOfWork.Repository<Resource>().TrackedQuery()
                .Include(r => r.Batch)
                .FirstOrDefaultAsync(r => r.Id == resourceId, cancellationToken)
                ?? throw new NotFoundException(nameof(Resource), resourceId);
            if (request.FolderId.HasValue)
            {
                await RequireFolderAsync(request.FolderId.Value, cancellationToken);
            }

            resource.FolderId = request.FolderId;
            _unitOfWork.Repository<Resource>().Update(resource);
            await _auditLog.StageAsync(AuditAction.Update, nameof(Resource), resource.Id.ToString(), cancellationToken: cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            return resource.ToDto();
        }

        private async Task<IReadOnlyList<ResourceFolderAccessDto>> ReadAccessAsync(Guid folderId, CancellationToken cancellationToken)
        {
            var rows = await _unitOfWork.Repository<ResourceFolderAccess>().Query()
                .Include(a => a.ParentProfile).ThenInclude(p => p.User)
                .Include(a => a.ParentProfile).ThenInclude(p => p.Children)
                .Where(a => a.FolderId == folderId)
                .OrderBy(a => a.ParentProfile.User.FirstName)
                .ToListAsync(cancellationToken);
            return rows.Select(a => new ResourceFolderAccessDto
            {
                ParentProfileId = a.ParentProfileId,
                ParentName = $"{a.ParentProfile.User.FirstName} {a.ParentProfile.User.LastName}".Trim(),
                ParentEmail = a.ParentProfile.User.Email,
                ChildNames = string.Join(", ", a.ParentProfile.Children.Select(c => c.FirstName)),
                SharedAtUtc = a.CreatedAtUtc,
            }).ToList();
        }

        private async Task<List<(Guid Id, Guid? ParentId)>> LoadTreeAsync(CancellationToken cancellationToken)
        {
            var rows = await _unitOfWork.Repository<ResourceFolder>().Query()
                .Select(f => new { f.Id, f.ParentFolderId })
                .ToListAsync(cancellationToken);
            return rows.Select(f => (f.Id, f.ParentFolderId)).ToList();
        }

        private async Task RequireFolderAsync(Guid id, CancellationToken cancellationToken)
        {
            if (!await _unitOfWork.Repository<ResourceFolder>().ExistsAsync(f => f.Id == id, cancellationToken))
            {
                throw new NotFoundException(nameof(ResourceFolder), id);
            }
        }

        private async Task EnsureNameFreeAsync(string name, Guid? parentId, Guid? exceptFolderId, CancellationToken cancellationToken)
        {
            var lower = name.ToLower();
            var clash = await _unitOfWork.Repository<ResourceFolder>().ExistsAsync(
                f => f.ParentFolderId == parentId && f.Name.ToLower() == lower && (exceptFolderId == null || f.Id != exceptFolderId),
                cancellationToken);
            if (clash)
            {
                throw new ConflictException($"A folder named \"{name}\" already exists here.");
            }
        }

        private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

        private static ResourceFolderDto ToDto(ResourceFolder f, int files, int subfolders, int shared) => new()
        {
            Id = f.Id,
            Name = f.Name,
            Description = f.Description,
            ParentFolderId = f.ParentFolderId,
            FileCount = files,
            SubfolderCount = subfolders,
            SharedParentCount = shared,
            CreatedAtUtc = f.CreatedAtUtc,
        };
    }
}
