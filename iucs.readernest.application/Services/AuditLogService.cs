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
                    ActorUserId = _currentUser.UserId,
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
            CancellationToken cancellationToken = default)
        {
            page = Math.Max(page, 1);
            pageSize = Math.Clamp(pageSize, 1, 200);

            var query = _unitOfWork.Repository<AuditLog>().Query();
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
                .OrderByDescending(a => a.CreatedAtUtc)
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
