using iucs.readernest.application.Common.Exceptions;
using iucs.readernest.application.Dto.Users;
using iucs.readernest.domain.Entities.Users;
using iucs.readernest.domain.Enums;
using iucs.readernest.domain.Repository;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace iucs.readernest.application.Services
{
    public class StaffLeaveService : IStaffLeaveService
    {
        private readonly IUnitOfWork _unitOfWork;
        private readonly IAuditLogService _auditLog;
        private readonly INotificationService _notificationService;
        private readonly IConfiguration _configuration;
        private readonly ILogger<StaffLeaveService> _logger;

        public StaffLeaveService(
            IUnitOfWork unitOfWork,
            IAuditLogService auditLog,
            INotificationService notificationService,
            IConfiguration configuration,
            ILogger<StaffLeaveService> logger)
        {
            _unitOfWork = unitOfWork;
            _auditLog = auditLog;
            _notificationService = notificationService;
            _configuration = configuration;
            _logger = logger;
        }

        public async Task<IReadOnlyList<StaffLeaveRequestDto>> ListMineAsync(Guid userId, CancellationToken cancellationToken = default)
        {
            return await ProjectAsync(Query().Where(l => l.UserId == userId), cancellationToken);
        }

        public async Task<StaffLeaveRequestDto> SubmitAsync(
            Guid userId, SubmitStaffLeaveRequest request, CancellationToken cancellationToken = default)
        {
            var reason = request.Reason?.Trim() ?? string.Empty;
            if (reason.Length == 0)
            {
                throw new DomainValidationException("Please add a reason for your leave.");
            }

            if (request.EndDate < request.StartDate)
            {
                throw new DomainValidationException("The leave end date can't be before the start date.");
            }

            var user = await _unitOfWork.Repository<User>().Query()
                .FirstOrDefaultAsync(u => u.Id == userId, cancellationToken)
                ?? throw new NotFoundException("User not found.");
            if (user.Role is not (UserRole.SubAdmin or UserRole.AdmissionTeam))
            {
                throw new ForbiddenException("Staff leave is for admin-team members; teachers apply from their own Leave page.");
            }

            // No minimum notice (client requirement: apply whenever required) — only a clash with
            // leave already pending/approved for the same days is refused.
            var overlap = await Query().AnyAsync(
                l => l.UserId == userId
                     && (l.Status == LeaveStatus.Pending || l.Status == LeaveStatus.Approved)
                     && l.StartDate <= request.EndDate && l.EndDate >= request.StartDate,
                cancellationToken);
            if (overlap)
            {
                throw new ConflictException("You already have pending or approved leave covering some of these days.");
            }

            var leave = new StaffLeaveRequest
            {
                UserId = userId,
                StartDate = request.StartDate,
                EndDate = request.EndDate,
                Reason = reason,
            };
            await _unitOfWork.Repository<StaffLeaveRequest>().AddAsync(leave, cancellationToken);
            await _auditLog.StageAsync(AuditAction.Create, nameof(StaffLeaveRequest), leave.Id.ToString(), cancellationToken: cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            await NotifyApproversAsync(user, leave, cancellationToken);
            return await GetAsync(leave.Id, cancellationToken);
        }

        public async Task<StaffLeaveRequestDto> CancelMineAsync(Guid userId, Guid id, CancellationToken cancellationToken = default)
        {
            var leave = await _unitOfWork.Repository<StaffLeaveRequest>().TrackedQuery()
                .FirstOrDefaultAsync(l => l.Id == id && l.UserId == userId, cancellationToken)
                ?? throw new NotFoundException(nameof(StaffLeaveRequest), id);
            if (leave.Status != LeaveStatus.Pending)
            {
                throw new DomainValidationException($"This leave is already {leave.Status} and can no longer be withdrawn.");
            }

            leave.Status = LeaveStatus.Cancelled;
            await _auditLog.StageAsync(AuditAction.Update, nameof(StaffLeaveRequest), leave.Id.ToString(), cancellationToken: cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            return await GetAsync(leave.Id, cancellationToken);
        }

        public async Task<IReadOnlyList<StaffLeaveRequestDto>> ListAsync(LeaveStatus? status, CancellationToken cancellationToken = default)
        {
            var query = Query();
            if (status is not null)
            {
                query = query.Where(l => l.Status == status);
            }

            return await ProjectAsync(query, cancellationToken);
        }

        public async Task<StaffLeaveRequestDto> ReviewAsync(
            Guid reviewerUserId, Guid id, ReviewStaffLeaveRequest request, CancellationToken cancellationToken = default)
        {
            var leave = await _unitOfWork.Repository<StaffLeaveRequest>().TrackedQuery()
                .Include(l => l.User)
                .FirstOrDefaultAsync(l => l.Id == id, cancellationToken)
                ?? throw new NotFoundException(nameof(StaffLeaveRequest), id);
            if (leave.Status != LeaveStatus.Pending)
            {
                throw new ConflictException($"This leave has already been {leave.Status.ToString().ToLowerInvariant()}.");
            }

            leave.Status = request.Approve ? LeaveStatus.Approved : LeaveStatus.Rejected;
            leave.ReviewedByUserId = reviewerUserId;
            leave.ReviewedAtUtc = DateTime.UtcNow;
            leave.ReviewNote = string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim();
            await _auditLog.StageAsync(AuditAction.Update, nameof(StaffLeaveRequest), leave.Id.ToString(),
                changesJson: $"{{\"status\":\"{leave.Status}\"}}", cancellationToken: cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            try
            {
                await _notificationService.SendTemplatedEmailAsync(
                    leave.User.Id, leave.User.Email, NotificationType.LeaveStatusUpdate, "staff-leave-reviewed",
                    new Dictionary<string, string>
                    {
                        ["FirstName"] = leave.User.FirstName,
                        ["Dates"] = FormatDates(leave.StartDate, leave.EndDate),
                        ["Status"] = leave.Status == LeaveStatus.Approved ? "approved" : "not approved",
                        ["ReviewNote"] = leave.ReviewNote ?? string.Empty,
                    },
                    cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not email the staff-leave decision for {LeaveId}", leave.Id);
            }

            return await GetAsync(leave.Id, cancellationToken);
        }

        private IQueryable<StaffLeaveRequest> Query() => _unitOfWork.Repository<StaffLeaveRequest>().Query();

        private async Task<StaffLeaveRequestDto> GetAsync(Guid id, CancellationToken cancellationToken)
        {
            return (await ProjectAsync(Query().Where(l => l.Id == id), cancellationToken)).Single();
        }

        private async Task<List<StaffLeaveRequestDto>> ProjectAsync(IQueryable<StaffLeaveRequest> query, CancellationToken cancellationToken)
        {
            var rows = await query
                .Include(l => l.User).ThenInclude(u => u.RoleDefinition)
                .OrderByDescending(l => l.StartDate).ThenByDescending(l => l.CreatedAtUtc)
                .ToListAsync(cancellationToken);
            var reviewerIds = rows.Where(r => r.ReviewedByUserId != null).Select(r => r.ReviewedByUserId!.Value).Distinct().ToList();
            var reviewers = await _unitOfWork.Repository<User>().Query()
                .Where(u => reviewerIds.Contains(u.Id))
                .Select(u => new { u.Id, Name = u.FirstName + " " + u.LastName })
                .ToDictionaryAsync(u => u.Id, u => u.Name.Trim(), cancellationToken);

            return rows.Select(l => new StaffLeaveRequestDto
            {
                Id = l.Id,
                UserId = l.UserId,
                StaffName = $"{l.User.FirstName} {l.User.LastName}".Trim(),
                StaffEmail = l.User.Email,
                RoleName = l.User.RoleDefinition?.DisplayName ?? (l.User.Role == UserRole.AdmissionTeam ? "Admission" : null),
                StartDate = l.StartDate,
                EndDate = l.EndDate,
                Days = l.EndDate.DayNumber - l.StartDate.DayNumber + 1,
                Reason = l.Reason,
                Status = l.Status,
                ReviewedByName = l.ReviewedByUserId is { } rid && reviewers.TryGetValue(rid, out var name) ? name : null,
                ReviewedAtUtc = l.ReviewedAtUtc,
                ReviewNote = l.ReviewNote,
                CreatedAtUtc = l.CreatedAtUtc,
            }).ToList();
        }

        private static string FormatDates(DateOnly start, DateOnly end) =>
            start == end ? start.ToString("ddd, d MMM yyyy") : $"{start:ddd, d MMM} – {end:ddd, d MMM yyyy}";

        /// <summary>Admin plus every Sub Admin who can approve people matters (UserManagement:Approve — the Founder preset). Best-effort.</summary>
        private async Task NotifyApproversAsync(User staff, StaffLeaveRequest leave, CancellationToken cancellationToken)
        {
            try
            {
                var module = PermissionModule.UserManagement.ToString();
                var grants = _unitOfWork.Repository<SubAdminPermission>().Query();
                var approvers = await _unitOfWork.Repository<User>().Query()
                    .Include(u => u.RoleDefinition)
                    .Where(u => u.Status == UserStatus.Active
                                && (u.Role == UserRole.Admin
                                    || (u.Role == UserRole.SubAdmin
                                        && grants.Any(p => p.UserId == u.Id && p.Module == module && p.CanApprove))))
                    .ToListAsync(cancellationToken);

                var baseUrl = (_configuration["Frontend:BaseUrl"] ?? "http://localhost:5173").TrimEnd('/');
                foreach (var approver in approvers)
                {
                    var portal = approver.Role == UserRole.Admin
                        ? "/admin"
                        : (approver.RoleDefinition?.DefaultRoute ?? "/executive").TrimEnd('/');
                    await _notificationService.SendTemplatedEmailAsync(
                        approver.Id, approver.Email, NotificationType.General, "staff-leave-submitted",
                        new Dictionary<string, string>
                        {
                            ["StaffName"] = $"{staff.FirstName} {staff.LastName}".Trim(),
                            ["Dates"] = FormatDates(leave.StartDate, leave.EndDate),
                            ["Reason"] = leave.Reason,
                            ["ReviewUrl"] = $"{baseUrl}{portal}/staff-leave",
                        },
                        cancellationToken);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not send the staff-leave alert for {LeaveId}", leave.Id);
            }
        }
    }
}
