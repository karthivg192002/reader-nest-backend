using iucs.readernest.application.Common.Exceptions;
using iucs.readernest.application.Common.Interfaces;
using iucs.readernest.application.Dto.Common;
using iucs.readernest.application.Dto.Users;
using iucs.readernest.application.Helper;
using iucs.readernest.application.Mappings;
using iucs.readernest.domain.Entities.Communication;
using iucs.readernest.domain.Entities.Integrations;
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
        private readonly IEmailSender _emailSender;
        private readonly IWhatsAppSender _whatsAppSender;
        private readonly ISmsSender _smsSender;

        public UserService(
            IUnitOfWork unitOfWork,
            IPasswordHasher passwordHasher,
            INotificationService notifications,
            IAuditLogService auditLog,
            IEmailSender emailSender,
            IWhatsAppSender whatsAppSender,
            ISmsSender smsSender)
        {
            _unitOfWork = unitOfWork;
            _passwordHasher = passwordHasher;
            _notifications = notifications;
            _auditLog = auditLog;
            _emailSender = emailSender;
            _whatsAppSender = whatsAppSender;
            _smsSender = smsSender;
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

            if (request.RoleDefinitionId.HasValue && request.Role != UserRole.SubAdmin)
            {
                throw new DomainValidationException("A role can only be assigned to Sub Admin users.");
            }

            var email = request.Email.Trim().ToLowerInvariant();
            var users = _unitOfWork.Repository<User>();

            if (await users.ExistsAsync(u => u.Email == email, cancellationToken))
            {
                throw new ConflictException($"A user with email '{email}' already exists.");
            }

            RoleDefinition? assignedRole = null;
            if (request.RoleDefinitionId.HasValue)
            {
                assignedRole = await _unitOfWork.Repository<RoleDefinition>().Query()
                    .Include(r => r.Permissions)
                    .FirstOrDefaultAsync(r => r.Id == request.RoleDefinitionId.Value, cancellationToken)
                    ?? throw new NotFoundException(nameof(RoleDefinition), request.RoleDefinitionId.Value);
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
                RoleDefinitionId = assignedRole?.Id,
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

            if (assignedRole is not null)
            {
                var permissionRepository = _unitOfWork.Repository<SubAdminPermission>();
                foreach (var grant in assignedRole.Permissions)
                {
                    await permissionRepository.AddAsync(
                        new SubAdminPermission
                        {
                            User = user,
                            Module = grant.Module,
                            CanView = grant.CanView,
                            CanCreate = grant.CanCreate,
                            CanEdit = grant.CanEdit,
                            CanDelete = grant.CanDelete,
                            CanApprove = grant.CanApprove,
                        },
                        cancellationToken);
                }
            }

            // Requirement: the account holder receives login credentials on creation.
            // The plain-text temp password lives only in this email, never in the database.
            var (subject, body) = BuildWelcomeMessage(user.FirstName, user.Email, temporaryPassword);
            await _notifications.SendEmailAsync(
                user.Id,
                user.Email,
                NotificationType.General,
                subject,
                body,
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
            Guid? roleDefinitionId = null,
            CancellationToken cancellationToken = default)
        {
            var user = await _unitOfWork.Repository<User>().GetByIdAsync(userId, cancellationToken)
                ?? throw new NotFoundException(nameof(User), userId);

            if (user.Role != UserRole.SubAdmin)
            {
                throw new DomainValidationException("Module permissions can only be assigned to Sub Admin users.");
            }

            // Only an explicit role assignment (apply-preset) stamps the user's
            // named role; hand-editing individual checkboxes leaves it as-is.
            if (roleDefinitionId.HasValue)
            {
                user.RoleDefinitionId = roleDefinitionId;
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

        public async Task ResendCredentialsAsync(
            Guid userId,
            CredentialChannel channel,
            CancellationToken cancellationToken = default)
        {
            var user = await _unitOfWork.Repository<User>().GetByIdAsync(userId, cancellationToken)
                ?? throw new NotFoundException(nameof(User), userId);

            // Gate on the channel's integration being switched on (is_enabled). Do this
            // before regenerating the password so a disabled channel changes nothing.
            var channelKey = channel switch
            {
                CredentialChannel.WhatsApp => "whatsapp",
                CredentialChannel.Sms => "sms",
                _ => "email",
            };
            if (!await IsIntegrationEnabledAsync(channelKey, cancellationToken))
            {
                throw new DomainValidationException(
                    $"{channel} delivery is turned off. Enable it in Settings → Integrations first.");
            }

            var temporaryPassword = TemporaryPasswordGenerator.Generate();
            var (subject, body) = BuildWelcomeMessage(user.FirstName, user.Email, temporaryPassword);

            // Deliver BEFORE resetting the password: if the send fails we must not
            // leave the account with a new password nobody received. The senders
            // throw on failure so the admin gets a clear reason.
            var notificationChannel = NotificationChannel.Email;
            try
            {
                switch (channel)
                {
                    case CredentialChannel.WhatsApp:
                        if (string.IsNullOrWhiteSpace(user.Phone))
                        {
                            throw new DomainValidationException("This account has no phone number on file for WhatsApp.");
                        }

                        notificationChannel = NotificationChannel.WhatsApp;
                        await _whatsAppSender.SendAsync(user.Phone, body, cancellationToken);
                        break;

                    case CredentialChannel.Sms:
                        if (string.IsNullOrWhiteSpace(user.Phone))
                        {
                            throw new DomainValidationException("This account has no phone number on file for SMS.");
                        }

                        notificationChannel = NotificationChannel.Sms;
                        await _smsSender.SendAsync(user.Phone, body, cancellationToken);
                        break;

                    default:
                        await _emailSender.SendAsync(user.Email, subject, body, cancellationToken);
                        break;
                }
            }
            catch (AppException)
            {
                throw; // already a friendly, mapped failure
            }
            catch (Exception ex)
            {
                throw new DomainValidationException($"Could not send the {channel} message: {ex.Message}");
            }

            user.PasswordHash = _passwordHasher.Hash(temporaryPassword);
            _unitOfWork.Repository<User>().Update(user);

            await _unitOfWork.Repository<Notification>().AddAsync(
                new Notification
                {
                    RecipientUserId = user.Id,
                    Type = NotificationType.General,
                    Channel = notificationChannel,
                    Subject = subject,
                    Body = $"Onboarding credentials re-sent to {user.Email}.",
                    Status = NotificationStatus.Sent,
                    SentAtUtc = DateTime.UtcNow,
                },
                cancellationToken);

            await _auditLog.StageAsync(
                AuditAction.Update,
                nameof(User),
                user.Id.ToString(),
                $"Resent onboarding credentials via {channel}",
                cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }

        public async Task<CredentialChannelsDto> GetCredentialChannelsAsync(CancellationToken cancellationToken = default)
        {
            return new CredentialChannelsDto
            {
                Email = await IsIntegrationEnabledAsync("email", cancellationToken),
                WhatsApp = await IsIntegrationEnabledAsync("whatsapp", cancellationToken),
                Sms = await IsIntegrationEnabledAsync("sms", cancellationToken),
            };
        }

        private async Task<bool> IsIntegrationEnabledAsync(string key, CancellationToken cancellationToken)
        {
            var integration = await _unitOfWork.Repository<Integration>().Query()
                .FirstOrDefaultAsync(i => i.Key == key, cancellationToken);
            return integration is { IsEnabled: true };
        }

        private static (string Subject, string Body) BuildWelcomeMessage(string firstName, string email, string temporaryPassword)
        {
            const string subject = "Your Reader Nest account";
            var body =
                $"Hello {firstName},\n\nYour Reader Nest account is ready.\n\n" +
                $"Login: {email}\nTemporary password: {temporaryPassword}\n\n" +
                "Please sign in and change your password.";
            return (subject, body);
        }
    }
}
