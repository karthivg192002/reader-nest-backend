using iucs.readernest.application.Common.Exceptions;
using iucs.readernest.application.Dto.Users;
using iucs.readernest.domain.Entities.Users;
using iucs.readernest.domain.Enums;
using iucs.readernest.domain.Repository;
using Microsoft.EntityFrameworkCore;

namespace iucs.readernest.application.Services
{
    public class AccessRequestService : IAccessRequestService
    {
        private readonly IUnitOfWork _unitOfWork;
        private readonly IAuditLogService _auditLog;
        private readonly INotificationService _notificationService;

        public AccessRequestService(
            IUnitOfWork unitOfWork,
            IAuditLogService auditLog,
            INotificationService notificationService)
        {
            _unitOfWork = unitOfWork;
            _auditLog = auditLog;
            _notificationService = notificationService;
        }

        public async Task<AccessRequestDto> SubmitAsync(
            Guid requestedByUserId,
            SubmitAccessRequestRequest request,
            CancellationToken cancellationToken = default)
        {
            var requester = await _unitOfWork.Repository<User>().GetByIdAsync(requestedByUserId, cancellationToken)
                ?? throw new NotFoundException(nameof(User), requestedByUserId);

            if (requester.Role != UserRole.SubAdmin)
            {
                throw new DomainValidationException("Only a Relationship Manager account can request additional module access.");
            }

            // One open request at a time — a second click while the first is still pending
            // just re-surfaces the existing one rather than piling up duplicates for the admin.
            var existingPending = await _unitOfWork.Repository<AccessRequest>().Query()
                .Include(r => r.RequestedByUser)
                .FirstOrDefaultAsync(r => r.RequestedByUserId == requestedByUserId && r.Status == AccessRequestStatus.Pending, cancellationToken);
            if (existingPending is not null)
            {
                return ToDto(existingPending);
            }

            var modules = request.RequestedModules.Distinct().ToList();
            if (modules.Count > 0)
            {
                var validKeys = (await _unitOfWork.Repository<PermissionModuleDefinition>().Query()
                    .Select(m => m.Key)
                    .ToListAsync(cancellationToken))
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
                var unknown = modules.Where(m => !validKeys.Contains(m)).ToList();
                if (unknown.Count > 0)
                {
                    throw new DomainValidationException($"Unknown permission module(s): {string.Join(", ", unknown)}.");
                }
            }

            var accessRequest = new AccessRequest
            {
                RequestedByUserId = requestedByUserId,
                RequestedModules = string.Join(",", modules),
            };
            await _unitOfWork.Repository<AccessRequest>().AddAsync(accessRequest, cancellationToken);
            await _auditLog.StageAsync(AuditAction.Create, nameof(AccessRequest), accessRequest.Id.ToString(), cancellationToken: cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            var admins = await _unitOfWork.Repository<User>().Query()
                .Where(u => u.Role == UserRole.Admin && u.Status == UserStatus.Active)
                .ToListAsync(cancellationToken);
            var requesterName = $"{requester.FirstName} {requester.LastName}".Trim();
            var moduleList = string.Join(", ", modules);
            foreach (var admin in admins)
            {
                await _notificationService.SendTemplatedEmailAsync(
                    admin.Id, admin.Email, NotificationType.General, "access-request-submitted-admin-alert",
                    new Dictionary<string, string>
                    {
                        ["RequesterName"] = requesterName,
                        ["RequesterEmail"] = requester.Email,
                        ["RequestedModules"] = moduleList,
                    },
                    cancellationToken);
            }

            accessRequest.RequestedByUser = requester;
            return ToDto(accessRequest);
        }

        public async Task<IReadOnlyList<AccessRequestDto>> ListMineAsync(
            Guid requestedByUserId,
            CancellationToken cancellationToken = default)
        {
            var requests = await _unitOfWork.Repository<AccessRequest>().Query()
                .Include(r => r.RequestedByUser)
                .Where(r => r.RequestedByUserId == requestedByUserId)
                .OrderByDescending(r => r.CreatedAtUtc)
                .ToListAsync(cancellationToken);
            return await ToDtosAsync(requests, cancellationToken);
        }

        public async Task<IReadOnlyList<AccessRequestDto>> ListAsync(
            AccessRequestStatus? status,
            CancellationToken cancellationToken = default)
        {
            var query = _unitOfWork.Repository<AccessRequest>().Query()
                .Include(r => r.RequestedByUser)
                .AsQueryable();
            if (status.HasValue)
            {
                query = query.Where(r => r.Status == status.Value);
            }

            var requests = await query.OrderByDescending(r => r.CreatedAtUtc).ToListAsync(cancellationToken);
            return await ToDtosAsync(requests, cancellationToken);
        }

        public async Task<AccessRequestDto> ReviewAsync(
            Guid id,
            Guid reviewerUserId,
            ReviewAccessRequestRequest request,
            CancellationToken cancellationToken = default)
        {
            var accessRequest = await _unitOfWork.Repository<AccessRequest>().TrackedQuery()
                .Include(r => r.RequestedByUser)
                .FirstOrDefaultAsync(r => r.Id == id, cancellationToken)
                ?? throw new NotFoundException(nameof(AccessRequest), id);
            var reviewer = await _unitOfWork.Repository<User>().GetByIdAsync(reviewerUserId, cancellationToken)
                ?? throw new NotFoundException(nameof(User), reviewerUserId);

            if (accessRequest.Status != AccessRequestStatus.Pending)
            {
                throw new DomainValidationException($"This access request is already {accessRequest.Status}.");
            }

            accessRequest.Status = request.Approve ? AccessRequestStatus.Approved : AccessRequestStatus.Rejected;
            accessRequest.ReviewedBy = reviewerUserId;
            accessRequest.ReviewedAtUtc = DateTime.UtcNow;
            accessRequest.ReviewNote = request.ReviewNote?.Trim();

            if (request.Approve)
            {
                await GrantRequestedModulesAsync(accessRequest, cancellationToken);
            }

            await _auditLog.StageAsync(AuditAction.Update, nameof(AccessRequest), accessRequest.Id.ToString(), cancellationToken: cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            var requester = accessRequest.RequestedByUser;
            await _notificationService.SendTemplatedEmailAsync(
                requester.Id, requester.Email, NotificationType.General, "access-request-reviewed",
                new Dictionary<string, string>
                {
                    ["RequesterFirstName"] = requester.FirstName,
                    ["RequestedModules"] = string.Join(", ", ParseModules(accessRequest.RequestedModules)),
                    ["Status"] = accessRequest.Status.ToString(),
                    ["ReviewNote"] = string.IsNullOrWhiteSpace(accessRequest.ReviewNote) ? "" : $"Note: {accessRequest.ReviewNote}",
                },
                cancellationToken);

            return ToDto(accessRequest, $"{reviewer.FirstName} {reviewer.LastName}".Trim());
        }

        /// <summary>
        /// Approving a request must actually grant the access, not just flip its status label
        /// — previously it did only the latter, so a Sub Admin whose request was approved
        /// still saw "No access" on every module they'd asked for; nothing here ever touched
        /// SubAdminPermission. Grants read-only (CanView) on each requested module — a request
        /// only ever names a module, never a specific action level, so View is the safe floor
        /// an Admin clearly intended by approving; handing over more (Create/Edit/Delete/
        /// Approve) is still a deliberate separate step through Roles &amp; Permissions. Never
        /// downgrades a module the Sub Admin already holds at a higher level.
        /// </summary>
        private async Task GrantRequestedModulesAsync(AccessRequest accessRequest, CancellationToken cancellationToken)
        {
            var modules = ParseModules(accessRequest.RequestedModules);
            var existing = await _unitOfWork.Repository<SubAdminPermission>().TrackedQuery()
                .Where(p => p.UserId == accessRequest.RequestedByUserId && modules.Contains(p.Module))
                .ToDictionaryAsync(p => p.Module, cancellationToken);

            foreach (var module in modules)
            {
                if (existing.TryGetValue(module, out var grant))
                {
                    if (!grant.CanView)
                    {
                        grant.CanView = true;
                        _unitOfWork.Repository<SubAdminPermission>().Update(grant);
                    }
                }
                else
                {
                    await _unitOfWork.Repository<SubAdminPermission>().AddAsync(
                        new SubAdminPermission { UserId = accessRequest.RequestedByUserId, Module = module, CanView = true },
                        cancellationToken);
                }
            }
        }

        private async Task<IReadOnlyList<AccessRequestDto>> ToDtosAsync(
            IReadOnlyList<AccessRequest> requests,
            CancellationToken cancellationToken)
        {
            var reviewerIds = requests.Where(r => r.ReviewedBy.HasValue).Select(r => r.ReviewedBy!.Value).Distinct().ToList();
            var reviewers = reviewerIds.Count == 0
                ? []
                : await _unitOfWork.Repository<User>().Query()
                    .Where(u => reviewerIds.Contains(u.Id))
                    .ToDictionaryAsync(u => u.Id, u => $"{u.FirstName} {u.LastName}".Trim(), cancellationToken);

            return requests.Select(r => ToDto(r, reviewers.GetValueOrDefault(r.ReviewedBy ?? Guid.Empty))).ToList();
        }

        private static IReadOnlyList<string> ParseModules(string csv) =>
            csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

        private static AccessRequestDto ToDto(AccessRequest r, string? reviewedByName = null) => new()
        {
            Id = r.Id,
            RequestedByUserId = r.RequestedByUserId,
            RequestedByName = $"{r.RequestedByUser.FirstName} {r.RequestedByUser.LastName}".Trim(),
            RequestedByEmail = r.RequestedByUser.Email,
            RequestedModules = ParseModules(r.RequestedModules),
            Status = r.Status,
            ReviewedBy = r.ReviewedBy,
            ReviewedByName = reviewedByName,
            ReviewedAtUtc = r.ReviewedAtUtc,
            ReviewNote = r.ReviewNote,
            CreatedAtUtc = r.CreatedAtUtc,
        };
    }
}
