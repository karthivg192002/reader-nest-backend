using iucs.readernest.domain.Common;
using iucs.readernest.domain.Entities.Auditing;
using iucs.readernest.domain.Enums;
using iucs.readernest.domain.Repository;

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
    }
}
