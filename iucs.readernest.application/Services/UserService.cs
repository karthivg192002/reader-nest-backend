using iucs.readernest.application.Common.Exceptions;
using iucs.readernest.application.Common.Interfaces;
using iucs.readernest.application.Dto.Common;
using iucs.readernest.application.Dto.Users;
using iucs.readernest.application.Helper;
using iucs.readernest.application.Mappings;
using iucs.readernest.domain.Entities.Users;
using iucs.readernest.domain.Enums;
using iucs.readernest.domain.Repository;
using Microsoft.EntityFrameworkCore;

namespace iucs.readernest.application.Services
{
    public class UserService : IUserService
    {
        private readonly IUnitOfWork _unitOfWork;
        private readonly IPasswordHasher _passwordHasher;
        private readonly INotificationService _notifications;
        private readonly IAuditLogService _auditLog;

        public UserService(
            IUnitOfWork unitOfWork,
            IPasswordHasher passwordHasher,
            INotificationService notifications,
            IAuditLogService auditLog)
        {
            _unitOfWork = unitOfWork;
            _passwordHasher = passwordHasher;
            _notifications = notifications;
            _auditLog = auditLog;
        }

        public async Task<PagedResult<UserDto>> ListAsync(
            UserRole? role,
            string? search,
            int page,
            int pageSize,
            CancellationToken cancellationToken = default)
        {
            page = Math.Max(page, 1);
            pageSize = Math.Clamp(pageSize, 1, 100);

            var query = _unitOfWork.Repository<User>().Query().Include(u => u.TeacherProfile).AsQueryable();

            if (role.HasValue)
            {
                query = query.Where(u => u.Role == role.Value);
            }

            if (!string.IsNullOrWhiteSpace(search))
            {
                var term = search.Trim().ToLower();
                query = query.Where(u =>
                    u.FirstName.ToLower().Contains(term) ||
                    u.LastName.ToLower().Contains(term) ||
                    u.Email.ToLower().Contains(term));
            }

            var total = await query.CountAsync(cancellationToken);
            var users = await query
                .OrderByDescending(u => u.CreatedAtUtc)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync(cancellationToken);

            return new PagedResult<UserDto>
            {
                Items = users.Select(u => u.ToDto()).ToList(),
                TotalCount = total,
                Page = page,
                PageSize = pageSize,
            };
        }

        public async Task<UserDto> GetAsync(Guid id, CancellationToken cancellationToken = default)
        {
            var user = await _unitOfWork.Repository<User>().Query()
                .Include(u => u.TeacherProfile)
                .FirstOrDefaultAsync(u => u.Id == id, cancellationToken)
                ?? throw new NotFoundException(nameof(User), id);

            return user.ToDto();
        }

        public async Task<IReadOnlyList<TeacherOptionDto>> ListTeachersAsync(CancellationToken cancellationToken = default)
        {
            var teachers = await _unitOfWork.Repository<TeacherProfile>().Query()
                .Include(t => t.User)
                .Where(t => t.User.Status == UserStatus.Active)
                .OrderBy(t => t.User.FirstName)
                .ToListAsync(cancellationToken);

            return teachers
                .Select(t => new TeacherOptionDto
                {
                    TeacherProfileId = t.Id,
                    UserId = t.UserId,
                    FullName = $"{t.User.FirstName} {t.User.LastName}",
                    Department = t.Department,
                })
                .ToList();
        }

        public async Task<UserDto> CreateAsync(CreateUserRequest request, CancellationToken cancellationToken = default)
        {
            if (request.Role == UserRole.Admin)
            {
                throw new DomainValidationException("Admin accounts cannot be created through this endpoint.");
            }

            var email = request.Email.Trim().ToLowerInvariant();
            var users = _unitOfWork.Repository<User>();

            if (await users.ExistsAsync(u => u.Email == email, cancellationToken))
            {
                throw new ConflictException($"A user with email '{email}' already exists.");
            }

            var temporaryPassword = TemporaryPasswordGenerator.Generate();
            var user = new User
            {
                Email = email,
                PasswordHash = _passwordHasher.Hash(temporaryPassword),
                FirstName = request.FirstName.Trim(),
                LastName = request.LastName.Trim(),
                Phone = request.Phone,
                Role = request.Role,
                TimeZoneId = string.IsNullOrWhiteSpace(request.TimeZoneId) ? "Asia/Kolkata" : request.TimeZoneId,
            };
            await users.AddAsync(user, cancellationToken);

            switch (request.Role)
            {
                case UserRole.Parent:
                    await _unitOfWork.Repository<ParentProfile>()
                        .AddAsync(new ParentProfile { User = user }, cancellationToken);
                    break;
                case UserRole.Teacher:
                    await _unitOfWork.Repository<TeacherProfile>()
                        .AddAsync(new TeacherProfile { User = user, Department = request.Department }, cancellationToken);
                    break;
            }

            // Requirement: the account holder receives login credentials on creation.
            // The plain-text temp password lives only in this email, never in the database.
            await _notifications.SendEmailAsync(
                user.Id,
                user.Email,
                NotificationType.General,
                "Your Reader Nest account",
                $"Hello {user.FirstName},\n\nYour Reader Nest account is ready.\n\n" +
                $"Login: {user.Email}\nTemporary password: {temporaryPassword}\n\n" +
                "Please sign in and change your password.",
                cancellationToken);

            await _auditLog.StageAsync(AuditAction.Create, nameof(User), user.Id.ToString(), cancellationToken: cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return user.ToDto();
        }

        public async Task<UserDto> UpdateAsync(Guid id, UpdateUserRequest request, CancellationToken cancellationToken = default)
        {
            var user = await _unitOfWork.Repository<User>().GetByIdAsync(id, cancellationToken)
                ?? throw new NotFoundException(nameof(User), id);

            user.FirstName = request.FirstName.Trim();
            user.LastName = request.LastName.Trim();
            user.Phone = request.Phone;
            if (!string.IsNullOrWhiteSpace(request.TimeZoneId))
            {
                user.TimeZoneId = request.TimeZoneId;
            }

            await _auditLog.StageAsync(AuditAction.Update, nameof(User), user.Id.ToString(), cancellationToken: cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return user.ToDto();
        }

        public async Task<UserDto> SetStatusAsync(Guid id, UserStatus status, CancellationToken cancellationToken = default)
        {
            var user = await _unitOfWork.Repository<User>().GetByIdAsync(id, cancellationToken)
                ?? throw new NotFoundException(nameof(User), id);

            user.Status = status;

            var action = status == UserStatus.Suspended ? AuditAction.Suspend
                : status == UserStatus.Active ? AuditAction.Restore
                : AuditAction.Update;
            await _auditLog.StageAsync(action, nameof(User), user.Id.ToString(), cancellationToken: cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return user.ToDto();
        }

        public async Task<IReadOnlyList<PermissionDto>> GetPermissionsAsync(Guid userId, CancellationToken cancellationToken = default)
        {
            var grants = await _unitOfWork.Repository<SubAdminPermission>().Query()
                .Where(p => p.UserId == userId)
                .ToListAsync(cancellationToken);

            return grants.Select(g => g.ToDto()).ToList();
        }

        public async Task SetPermissionsAsync(
            Guid userId,
            IReadOnlyList<PermissionDto> permissions,
            CancellationToken cancellationToken = default)
        {
            var user = await _unitOfWork.Repository<User>().GetByIdAsync(userId, cancellationToken)
                ?? throw new NotFoundException(nameof(User), userId);

            if (user.Role != UserRole.SubAdmin)
            {
                throw new DomainValidationException("Module permissions can only be assigned to Sub Admin users.");
            }

            var repository = _unitOfWork.Repository<SubAdminPermission>();
            var existing = await repository.Query()
                .Where(p => p.UserId == userId)
                .ToListAsync(cancellationToken);

            // Replace-all semantics: the admin's permission screen submits the full matrix.
            foreach (var grant in existing)
            {
                repository.Remove(grant);
            }

            foreach (var dto in permissions)
            {
                await repository.AddAsync(
                    new SubAdminPermission
                    {
                        UserId = userId,
                        Module = dto.Module,
                        CanView = dto.CanView,
                        CanCreate = dto.CanCreate,
                        CanEdit = dto.CanEdit,
                        CanDelete = dto.CanDelete,
                        CanApprove = dto.CanApprove,
                    },
                    cancellationToken);
            }

            await _auditLog.StageAsync(
                AuditAction.Update,
                nameof(SubAdminPermission),
                userId.ToString(),
                cancellationToken: cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }
    }
}
