using iucs.readernest.application.Dto.Audit;
using iucs.readernest.application.Dto.Common;
using iucs.readernest.domain.Common;
using iucs.readernest.domain.Entities.Auditing;
using iucs.readernest.domain.Entities.Users;
using iucs.readernest.domain.Enums;
using iucs.readernest.domain.Repository;
using Microsoft.EntityFrameworkCore;

namespace iucs.readernest.application.Services
{
    public class AuditLogService : IAuditLogService
    {
        private readonly IUnitOfWork _unitOfWork;
        private readonly ICurrentUserService _currentUser;

        public AuditLogService(IUnitOfWork unitOfWork, ICurrentUserService currentUser)
        {
            _unitOfWork = unitOfWork;
            _currentUser = currentUser;
        }

        public async Task StageAsync(
            AuditAction action,
            string entityName,
            string? entityId = null,
            string? changesJson = null,
            CancellationToken cancellationToken = default)
        {
            await _unitOfWork.Repository<AuditLog>().AddAsync(
                new AuditLog
                {
                    // In a "view as parent" session, the staff member looking -- not the parent.
                    ActorUserId = _currentUser.ViewAsActorUserId ?? _currentUser.UserId,
                    Action = action,
                    EntityName = entityName,
                    EntityId = entityId,
                    ChangesJson = changesJson,
                },
                cancellationToken);
        }

        public async Task<PagedResult<AuditLogDto>> ListAsync(
            string? entityName,
            AuditAction? action,
            int page,
            int pageSize,
            Guid? restrictToActorId = null,
            CancellationToken cancellationToken = default)
        {
            page = Math.Max(page, 1);
            pageSize = Math.Clamp(pageSize, 1, 200);

            var query = _unitOfWork.Repository<AuditLog>().Query();
            if (restrictToActorId.HasValue)
            {
                query = query.Where(a => a.ActorUserId == restrictToActorId.Value);
            }

            if (!string.IsNullOrWhiteSpace(entityName))
            {
                query = query.Where(a => a.EntityName == entityName);
            }

            if (action.HasValue)
            {
                query = query.Where(a => a.Action == action.Value);
            }

            var totalCount = await query.CountAsync(cancellationToken);

            // Left-join the actor's name for display (system actions have no actor).
            var users = _unitOfWork.Repository<User>().Query();
            var rows = await query
                // CreatedAtUtc alone ties on rows logged in the same tick (e.g. a bulk
                // action), which makes Skip/Take non-deterministic across pages — a row can
                // repeat or vanish depending on which side of the tie it lands on each
                // request. Same fix as ListInvoicesAsync's pagination (a68b1a1).
                .OrderByDescending(a => a.CreatedAtUtc)
                .ThenBy(a => a.Id)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(a => new AuditLogDto
                {
                    Id = a.Id,
                    ActorUserId = a.ActorUserId,
                    ActorName = users.Where(u => u.Id == a.ActorUserId)
                        .Select(u => u.FirstName + " " + u.LastName)
                        .FirstOrDefault(),
                    Action = a.Action,
                    EntityName = a.EntityName,
                    EntityId = a.EntityId,
                    ChangesJson = a.ChangesJson,
                    CreatedAtUtc = a.CreatedAtUtc,
                })
                .ToListAsync(cancellationToken);

            return new PagedResult<AuditLogDto>
            {
                Items = rows,
                TotalCount = totalCount,
                Page = page,
                PageSize = pageSize,
            };
        }
    }
}
