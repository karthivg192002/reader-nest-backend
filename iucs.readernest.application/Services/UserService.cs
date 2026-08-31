using iucs.readernest.application.Common;
using iucs.readernest.application.Common.Exceptions;
using iucs.readernest.application.Common.Interfaces;
using iucs.readernest.application.Dto.Common;
using iucs.readernest.application.Dto.Users;
using iucs.readernest.application.Helper;
using iucs.readernest.application.Mappings;
using iucs.readernest.domain.Entities.Academics;
using iucs.readernest.domain.Entities.Billing;
using iucs.readernest.domain.Entities.Communication;
using iucs.readernest.domain.Entities.Integrations;
using iucs.readernest.domain.Entities.Sessions;
using iucs.readernest.domain.Entities.Users;
using iucs.readernest.domain.Enums;
using iucs.readernest.domain.Repository;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace iucs.readernest.application.Services
{
    public class UserService : IUserService
    {
        private readonly IUnitOfWork _unitOfWork;
        private readonly IPasswordHasher _passwordHasher;
        private readonly INotificationService _notifications;
        private readonly IEmailTemplateService _emailTemplateService;
        private readonly IAuditLogService _auditLog;
        private readonly IEmailSender _emailSender;
        private readonly IWhatsAppSender _whatsAppSender;
        private readonly ISmsSender _smsSender;
        private readonly IBulkFileReader _bulkFileReader;
        private readonly ILogger<UserService> _logger;

        public UserService(
            IUnitOfWork unitOfWork,
            IPasswordHasher passwordHasher,
            INotificationService notifications,
            IEmailTemplateService emailTemplateService,
            IAuditLogService auditLog,
            IEmailSender emailSender,
            IWhatsAppSender whatsAppSender,
            ISmsSender smsSender,
            IBulkFileReader bulkFileReader,
            ILogger<UserService> logger)
        {
            _unitOfWork = unitOfWork;
            _passwordHasher = passwordHasher;
            _notifications = notifications;
            _emailTemplateService = emailTemplateService;
            _auditLog = auditLog;
            _emailSender = emailSender;
            _whatsAppSender = whatsAppSender;
            _smsSender = smsSender;
            _bulkFileReader = bulkFileReader;
            _logger = logger;
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

            var query = _unitOfWork.Repository<User>().Query().Include(u => u.TeacherProfile).ThenInclude(t => t!.Department).AsQueryable();

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

            // Every enum column (Role, Status, TeacherProfile.Department, ...) is stored as a
            // string and converted back on read; a single row whose stored value doesn't match
            // any current enum member throws while EF materializes it. Selecting just the ids
            // triggers no such conversion (Id is a plain Guid column), so it can't fail here —
            // only the per-row fetch below can, and it's scoped to one row at a time instead of
            // taking down the whole page the way a single ToListAsync() over the batch did.
            var pageIds = await query
                .OrderByDescending(u => u.CreatedAtUtc)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(u => u.Id)
                .ToListAsync(cancellationToken);

            // Fast path: one query for the whole page instead of one round trip per row. This
            // only fails if a row in THIS page is corrupt (see the comment above), which is
            // rare — so it's worth trying as a batch first and only paying for per-row
            // isolation on the pages that actually contain a bad value.
            var items = new List<UserDto>(pageIds.Count);
            try
            {
                var batch = await _unitOfWork.Repository<User>().Query()
                    .Include(u => u.TeacherProfile).ThenInclude(t => t!.Department)
                    .Where(u => pageIds.Contains(u.Id))
                    .ToListAsync(cancellationToken);
                var byId = batch.ToDictionary(u => u.Id);
                foreach (var id in pageIds)
                {
                    if (byId.TryGetValue(id, out var user)) items.Add(user.ToDto());
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // The batch failed partway through materializing — which row(s) is unknown
                // (EF surfaces the failure once, not per-row), so fall back to loading this
                // page one row at a time and skip only the ones that actually don't load.
                items.Clear();
                foreach (var id in pageIds)
                {
                    try
                    {
                        var user = await _unitOfWork.Repository<User>().Query()
                            .Include(u => u.TeacherProfile).ThenInclude(t => t!.Department)
                            .FirstAsync(u => u.Id == id, cancellationToken);
                        items.Add(user.ToDto());
                    }
                    catch (Exception rowEx) when (rowEx is not OperationCanceledException)
                    {
                        // Skipped, not defaulted — this is a corrupt/stale value that needs a real
                        // data fix, and silently guessing a role or status for it would be worse
                        // than leaving it off an admin list page.
                        _logger.LogError(rowEx, "Failed to load user {UserId} for the Users list; skipping this row.", id);
                    }
                }
            }

            return new PagedResult<UserDto>
            {
                Items = items,
                TotalCount = total,
                Page = page,
                PageSize = pageSize,
            };
        }

        public async Task<UserDto> GetAsync(Guid id, CancellationToken cancellationToken = default)
        {
            var user = await _unitOfWork.Repository<User>().Query()
                .Include(u => u.TeacherProfile).ThenInclude(t => t!.Department)
                .FirstOrDefaultAsync(u => u.Id == id, cancellationToken)
                ?? throw new NotFoundException(nameof(User), id);

            return user.ToDto();
        }

        public async Task<IReadOnlyList<TeacherOptionDto>> ListTeachersAsync(CancellationToken cancellationToken = default)
        {
            var teachers = await _unitOfWork.Repository<TeacherProfile>().Query()
                .Include(t => t.User)
                .Include(t => t.Department)
                .Where(t => t.User.Status == UserStatus.Active)
                .OrderBy(t => t.User.FirstName)
                .ToListAsync(cancellationToken);

            return teachers
                .Select(t => new TeacherOptionDto
                {
                    TeacherProfileId = t.Id,
                    UserId = t.UserId,
                    FullName = $"{t.User.FirstName} {t.User.LastName}".Trim(),
                    DepartmentId = t.DepartmentId,
                    DepartmentName = t.Department?.Name,
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

                // Mirrors UsersController.ApplyPermissionPreset's guard — that endpoint only
                // covers re-assigning an existing Sub Admin's preset, not creating a brand new
                // one with a RoleDefinitionId already set in the request body, which this was
                // missing entirely (how a real account once ended up on the "student" preset).
                if (NonSubAdminPresetNames.Names.Contains(assignedRole.Name))
                {
                    throw new DomainValidationException(
                        $"'{assignedRole.DisplayName}' is a fixed-portal system role, not a Sub Admin preset, and can't be assigned to a new account.");
                }
            }

            var temporaryPin = TemporaryPinGenerator.Generate();
            var user = new User
            {
                Email = email,
                PinHash = _passwordHasher.Hash(temporaryPin),
                FirstName = request.FirstName.Trim(),
                LastName = request.LastName?.Trim() ?? string.Empty,
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
                        .AddAsync(new TeacherProfile { User = user, DepartmentId = request.DepartmentId }, cancellationToken);
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
            // The plain-text temp PIN lives only in this email, never in the database.
            await _notifications.SendTemplatedEmailAsync(
                user.Id,
                user.Email,
                NotificationType.General,
                "welcome-credentials",
                new Dictionary<string, string>
                {
                    ["FirstName"] = user.FirstName,
                    ["Email"] = user.Email,
                    ["TemporaryPin"] = temporaryPin,
                },
                cancellationToken);

            await _auditLog.StageAsync(AuditAction.Create, nameof(User), user.Id.ToString(), cancellationToken: cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return user.ToDto();
        }

        public async Task<UserDto> UpdateAsync(Guid id, UpdateUserRequest request, CancellationToken cancellationToken = default)
        {
            var user = await _unitOfWork.Repository<User>().TrackedQuery()
                .Include(u => u.TeacherProfile).ThenInclude(t => t!.Department)
                .FirstOrDefaultAsync(u => u.Id == id, cancellationToken)
                ?? throw new NotFoundException(nameof(User), id);

            user.FirstName = request.FirstName.Trim();
            user.LastName = request.LastName?.Trim() ?? string.Empty;
            user.Phone = request.Phone;
            if (!string.IsNullOrWhiteSpace(request.TimeZoneId))
            {
                user.TimeZoneId = request.TimeZoneId;
            }

            if (request.DepartmentId.HasValue && user.TeacherProfile is not null)
            {
                user.TeacherProfile.DepartmentId = request.DepartmentId.Value;
            }

            await _auditLog.StageAsync(AuditAction.Update, nameof(User), user.Id.ToString(), cancellationToken: cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return user.ToDto();
        }

        public async Task<UserDto> SetStatusAsync(Guid id, UserStatus status, CancellationToken cancellationToken = default)
        {
            var user = await _unitOfWork.Repository<User>().GetByIdAsync(id, cancellationToken)
                ?? throw new NotFoundException(nameof(User), id);

            // Same blanket rule as ChangeRoleAsync/CreateAsync: Admin accounts are untouchable
            // through this generic action. Without it, anyone holding UserManagement:Edit — a
            // routine grant for e.g. a Relationship Manager Sub Admin — could suspend the real
            // Admin account outright (self-preservation after a privilege-escalation attempt,
            // or standalone sabotage/denial of service).
            if (user.Role == UserRole.Admin)
            {
                throw new DomainValidationException("Admin accounts can't be changed through this action.");
            }

            var wasActive = user.Status == UserStatus.Active;
            user.Status = status;

            var action = status == UserStatus.Suspended ? AuditAction.Suspend
                : status == UserStatus.Active ? AuditAction.Restore
                : AuditAction.Update;
            await _auditLog.StageAsync(action, nameof(User), user.Id.ToString(), cancellationToken: cancellationToken);

            // A parent who can no longer sign in can't pay either — leaving their
            // subscription Active would let BillingBackgroundService keep invoicing (and
            // eventually suspend/dun) an account nobody can act on. Pausing is one-way here:
            // resuming billing after a restore is a deliberate admin call, not an automatic one.
            if (user.Role == UserRole.Parent && wasActive && status != UserStatus.Active)
            {
                await PauseSubscriptionsForParentUserAsync(id, cancellationToken);
            }

            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return user.ToDto();
        }

        private async Task PauseSubscriptionsForParentUserAsync(Guid parentUserId, CancellationToken cancellationToken)
        {
            var parentProfile = await _unitOfWork.Repository<ParentProfile>()
                .FirstOrDefaultAsync(p => p.UserId == parentUserId, cancellationToken);
            if (parentProfile is null)
            {
                return;
            }

            var subscriptions = await _unitOfWork.Repository<Subscription>().TrackedQuery()
                .Where(s => s.ParentProfileId == parentProfile.Id && s.Status == SubscriptionStatus.Active)
                .ToListAsync(cancellationToken);

            foreach (var subscription in subscriptions)
            {
                subscription.Status = SubscriptionStatus.Paused;
            }
        }

        public async Task<UserDto> ChangeRoleAsync(Guid id, UserRole newRole, CancellationToken cancellationToken = default)
        {
            var user = await _unitOfWork.Repository<User>().GetByIdAsync(id, cancellationToken)
                ?? throw new NotFoundException(nameof(User), id);

            if (user.Role == UserRole.Admin || newRole == UserRole.Admin)
            {
                throw new DomainValidationException("Admin accounts can't be reassigned through this action.");
            }

            if (user.Role == newRole)
            {
                return user.ToDto();
            }

            // Refuse to strand real operational history behind a profile we're about to remove.
            switch (user.Role)
            {
                case UserRole.Parent:
                    if (await _unitOfWork.Repository<Child>().ExistsAsync(c => c.ParentProfile.UserId == id, cancellationToken))
                    {
                        throw new ConflictException(
                            "This parent has children on file — reassign or remove them before changing the account type.");
                    }
                    break;
                case UserRole.Teacher:
                    if (await _unitOfWork.Repository<ClassSession>().ExistsAsync(s => s.TeacherProfile.UserId == id, cancellationToken))
                    {
                        throw new ConflictException(
                            "This teacher has class sessions on file — reassign them before changing the account type.");
                    }
                    break;
            }

            // Remove the old type's side record/grants.
            switch (user.Role)
            {
                case UserRole.Parent:
                    var parentProfile = await _unitOfWork.Repository<ParentProfile>()
                        .FirstOrDefaultAsync(p => p.UserId == id, cancellationToken);
                    if (parentProfile is not null)
                    {
                        _unitOfWork.Repository<ParentProfile>().Remove(parentProfile);
                    }
                    break;
                case UserRole.Teacher:
                    var teacherProfile = await _unitOfWork.Repository<TeacherProfile>()
                        .FirstOrDefaultAsync(t => t.UserId == id, cancellationToken);
                    if (teacherProfile is not null)
                    {
                        _unitOfWork.Repository<TeacherProfile>().Remove(teacherProfile);
                    }
                    break;
                case UserRole.SubAdmin:
                    var grants = await _unitOfWork.Repository<SubAdminPermission>().Query()
                        .Where(p => p.UserId == id)
                        .ToListAsync(cancellationToken);
                    foreach (var grant in grants)
                    {
                        _unitOfWork.Repository<SubAdminPermission>().Remove(grant);
                    }
                    user.RoleDefinitionId = null;
                    break;
            }

            user.Role = newRole;

            // Create the new type's side record (mirrors CreateAsync).
            switch (newRole)
            {
                case UserRole.Parent:
                    await _unitOfWork.Repository<ParentProfile>().AddAsync(new ParentProfile { User = user }, cancellationToken);
                    break;
                case UserRole.Teacher:
                    await _unitOfWork.Repository<TeacherProfile>().AddAsync(new TeacherProfile { User = user }, cancellationToken);
                    break;
            }

            _unitOfWork.Repository<User>().Update(user);
            await _auditLog.StageAsync(
                AuditAction.Update, nameof(User), user.Id.ToString(), $"Role changed to {newRole}", cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return await GetAsync(id, cancellationToken);
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
            Guid currentUserId,
            IReadOnlyList<PermissionDto> permissions,
            Guid? roleDefinitionId = null,
            CancellationToken cancellationToken = default)
        {
            if (userId == currentUserId)
            {
                throw new DomainValidationException("You cannot change your own permissions.");
            }

            var user = await _unitOfWork.Repository<User>().GetByIdAsync(userId, cancellationToken)
                ?? throw new NotFoundException(nameof(User), userId);

            if (user.Role != UserRole.SubAdmin)
            {
                throw new DomainValidationException("Module permissions can only be assigned to Sub Admin users.");
            }

            // Ceiling check: a Sub Admin holding only UserManagement:Edit could otherwise grant
            // a colleague — or, combined with ResetPinAsync, a colleague's account they then
            // take over — any module/action neither of them was ever given, including
            // BillingFinance:Approve or Settings:Edit. PermissionAuthorizationHandler already
            // lets a real Admin caller through every [HasPermission] check regardless of the
            // SubAdminPermission table, so only non-Admin callers need this comparison.
            var currentUser = await _unitOfWork.Repository<User>().GetByIdAsync(currentUserId, cancellationToken)
                ?? throw new NotFoundException(nameof(User), currentUserId);
            if (currentUser.Role != UserRole.Admin)
            {
                var callerGrants = await _unitOfWork.Repository<SubAdminPermission>().Query()
                    .Where(p => p.UserId == currentUserId)
                    .ToDictionaryAsync(p => p.Module, cancellationToken);

                static bool Exceeds(bool requested, bool held) => requested && !held;

                foreach (var dto in permissions)
                {
                    callerGrants.TryGetValue(dto.Module, out var callerGrant);
                    if (Exceeds(dto.CanView, callerGrant?.CanView ?? false)
                        || Exceeds(dto.CanCreate, callerGrant?.CanCreate ?? false)
                        || Exceeds(dto.CanEdit, callerGrant?.CanEdit ?? false)
                        || Exceeds(dto.CanDelete, callerGrant?.CanDelete ?? false)
                        || Exceeds(dto.CanApprove, callerGrant?.CanApprove ?? false))
                    {
                        throw new ForbiddenException($"You can't grant '{dto.Module}' permissions you don't hold yourself.");
                    }
                }
            }

            // SubAdminPermission is uniquely indexed on (UserId, Module), so the same module
            // twice in one matrix inserts two colliding rows and fails at SaveChanges as an
            // opaque 500. RoleService.MapPermissions already rejects exactly this for the
            // role-level matrix; the per-user matrix written here needs the same guard.
            var duplicateModule = permissions.GroupBy(p => p.Module).FirstOrDefault(g => g.Count() > 1);
            if (duplicateModule is not null)
            {
                throw new DomainValidationException($"Module '{duplicateModule.Key}' appears more than once.");
            }

            // Only an explicit role assignment (apply-preset) stamps the user's
            // named role; hand-editing individual checkboxes leaves it as-is.
            if (roleDefinitionId.HasValue)
            {
                // UsersController.ApplyPermissionPreset already checks the preset name before
                // it ever gets here, but that's this method's only caller today, not a
                // guarantee for its next one — CreateAsync had the identical class of gap
                // until this same reserved-name set was pushed down there too. Enforcing it
                // here as well means the invariant holds regardless of which caller forgets.
                var assignedRole = await _unitOfWork.Repository<RoleDefinition>().GetByIdAsync(roleDefinitionId.Value, cancellationToken)
                    ?? throw new NotFoundException(nameof(RoleDefinition), roleDefinitionId.Value);
                if (NonSubAdminPresetNames.Names.Contains(assignedRole.Name))
                {
                    throw new DomainValidationException(
                        $"'{assignedRole.DisplayName}' is a fixed-portal system role, not a Sub Admin preset, and can't be assigned to an account.");
                }

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
            // before regenerating the PIN so a disabled channel changes nothing.
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

            var temporaryPin = TemporaryPinGenerator.Generate();
            var welcomeTokens = new Dictionary<string, string>
            {
                ["FirstName"] = user.FirstName,
                ["Email"] = user.Email,
                ["TemporaryPin"] = temporaryPin,
            };
            // WhatsApp/SMS are plain-text transports, not part of the Email Template Master.
            var plainBody =
                $"Hello {user.FirstName},\n\nYour Meet to Manage account is ready.\n\n" +
                $"Login: {user.Email}\nTemporary PIN: {temporaryPin}\n\n" +
                "Please sign in with this PIN. Contact your admin if you need it changed.";
            var (subject, emailHtmlBody) = await _emailTemplateService.RenderAsync(
                "welcome-credentials", welcomeTokens, cancellationToken);

            // Deliver BEFORE resetting the PIN: if the send fails we must not
            // leave the account with a new PIN nobody received. The senders
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
                        await _whatsAppSender.SendAsync(user.Phone, plainBody, cancellationToken);
                        break;

                    case CredentialChannel.Sms:
                        if (string.IsNullOrWhiteSpace(user.Phone))
                        {
                            throw new DomainValidationException("This account has no phone number on file for SMS.");
                        }

                        notificationChannel = NotificationChannel.Sms;
                        await _smsSender.SendAsync(user.Phone, plainBody, cancellationToken);
                        break;

                    default:
                        await _emailSender.SendAsync(user.Email, subject, emailHtmlBody, cancellationToken);
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

            user.PinHash = _passwordHasher.Hash(temporaryPin);
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

        public async Task<string> ResetPinAsync(Guid userId, CancellationToken cancellationToken = default)
        {
            var user = await _unitOfWork.Repository<User>().GetByIdAsync(userId, cancellationToken)
                ?? throw new NotFoundException(nameof(User), userId);

            // Every account — Admin included — authenticates with email+PIN alone (see
            // AuthService.LoginAsync), so the PIN this method hands back in the HTTP response
            // IS the full credential. Without this guard, anyone holding UserManagement:Edit
            // could reset the Admin account's PIN, read the new one straight off the screen,
            // and log in as Admin: a full account takeover from a routine, mid-tier grant.
            // Same blanket "Admin accounts can't be touched through this action" rule as
            // ChangeRoleAsync/SetStatusAsync.
            if (user.Role == UserRole.Admin)
            {
                throw new DomainValidationException("Admin accounts can't be reset through this action.");
            }

            var temporaryPin = TemporaryPinGenerator.Generate();
            user.PinHash = _passwordHasher.Hash(temporaryPin);
            _unitOfWork.Repository<User>().Update(user);

            // Logged as an admin-visible action, distinct from ResendCredentialsAsync's audit
            // entry, since this one was never sent anywhere and only the viewing admin ever
            // saw the plaintext PIN.
            await _auditLog.StageAsync(
                AuditAction.Update,
                nameof(User),
                user.Id.ToString(),
                "PIN reset by admin and shown on screen (not delivered via any channel)",
                cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return temporaryPin;
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

        public async Task DeleteAsync(Guid id, Guid currentUserId, CancellationToken cancellationToken = default)
        {
            if (id == currentUserId)
            {
                throw new DomainValidationException("You cannot delete your own account.");
            }

            var repository = _unitOfWork.Repository<User>();
            var user = await repository.GetByIdAsync(id, cancellationToken)
                ?? throw new NotFoundException(nameof(User), id);

            if (user.Role == UserRole.Admin)
            {
                // Unlike ChangeRoleAsync/SetStatusAsync/ResetPinAsync's blanket "never touch an
                // Admin" rule, removing one Admin account is a legitimate Admin-to-Admin
                // offboarding action — but only when the CALLER is also an Admin. The
                // [HasPermission(UserManagement, Delete)] attribute on this endpoint doesn't
                // restrict to Admin callers, so without this a Sub Admin holding only
                // UserManagement:Delete could remove any non-last Admin account outright.
                var currentUser = await repository.GetByIdAsync(currentUserId, cancellationToken)
                    ?? throw new NotFoundException(nameof(User), currentUserId);
                if (currentUser.Role != UserRole.Admin)
                {
                    throw new ForbiddenException("Only an Admin can delete another Admin account.");
                }

                var otherAdminExists = await repository.ExistsAsync(
                    u => u.Role == UserRole.Admin && u.Id != id, cancellationToken);
                if (!otherAdminExists)
                {
                    throw new ConflictException("Cannot delete the last remaining Admin account.");
                }
            }

            // Same "don't strand operational history" rule ChangeRoleAsync applies before
            // swapping a profile's role — a hard delete is even less reversible, so it needs
            // the same guard: a parent with children, or a teacher with sessions on the
            // calendar, must be reassigned/removed through that flow first.
            switch (user.Role)
            {
                case UserRole.Parent:
                    if (await _unitOfWork.Repository<Child>().ExistsAsync(c => c.ParentProfile.UserId == id, cancellationToken))
                    {
                        throw new ConflictException(
                            "This parent has children on file — reassign or remove them before deleting the account.");
                    }
                    break;
                case UserRole.Teacher:
                    if (await _unitOfWork.Repository<ClassSession>().ExistsAsync(
                        s => s.TeacherProfile.UserId == id
                            && s.Status != SessionStatus.Completed
                            && s.Status != SessionStatus.Cancelled
                            && s.Status != SessionStatus.Rescheduled,
                        cancellationToken))
                    {
                        throw new ConflictException(
                            "This teacher has upcoming or unresolved class sessions on file — reassign or cancel them before deleting the account.");
                    }
                    break;
            }

            repository.Remove(user);

            await _auditLog.StageAsync(AuditAction.Delete, nameof(User), user.Id.ToString(), cancellationToken: cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }

        public async Task<BulkImportResult> BulkImportAsync(
            Stream file, string fileName, UserRole role, CancellationToken cancellationToken = default)
        {
            if (role != UserRole.Parent && role != UserRole.Teacher)
            {
                throw new DomainValidationException("Only Parent and Teacher accounts can be bulk-imported.");
            }

            var rows = _bulkFileReader.ReadRows(file, fileName);
            var result = new BulkImportResult { TotalRows = rows.Count };

            for (var i = 0; i < rows.Count; i++)
            {
                var rowNumber = i + 2;
                try
                {
                    var row = rows[i];
                    var email = row.GetOrNull("Email") ?? throw new DomainValidationException("Email is required.");
                    var firstName = row.GetOrNull("FirstName") ?? throw new DomainValidationException("FirstName is required.");

                    Guid? departmentId = null;
                    if (role == UserRole.Teacher)
                    {
                        var departmentName = row.GetOrNull("DepartmentName");
                        if (departmentName is not null)
                        {
                            var department = await _unitOfWork.Repository<Department>()
                                .FirstOrDefaultAsync(d => d.Name == departmentName, cancellationToken)
                                ?? throw new NotFoundException($"No department named '{departmentName}'.");
                            departmentId = department.Id;
                        }
                    }

                    await CreateAsync(
                        new CreateUserRequest
                        {
                            Email = email,
                            FirstName = firstName,
                            LastName = row.GetOrNull("LastName"),
                            Phone = row.GetOrNull("Phone"),
                            Role = role,
                            DepartmentId = departmentId,
                        },
                        cancellationToken);
                    result.SucceededCount++;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    result.FailedCount++;
                    result.Errors.Add(new BulkImportRowError { RowNumber = rowNumber, Message = ex.Message });
                }
            }

            return result;
        }

        public async Task<string> ExportCsvAsync(UserRole? role, CancellationToken cancellationToken = default)
        {
            // Reuses the same page the Users screen itself pages through, at a size generous
            // enough to cover a school's whole roster in one export without turning this into
            // an unbounded table scan if the caller is ever handed a huge role by mistake.
            var page = await ListAsync(role, null, 1, 5000, cancellationToken);
            string[] headers = ["Email", "FirstName", "LastName", "Phone", "Role", "Status", "DepartmentName", "CreatedAtUtc"];
            var rows = page.Items.Select(u => new List<string?>
            {
                u.Email, u.FirstName, u.LastName, u.Phone, u.Role.ToString(), u.Status.ToString(),
                u.DepartmentName, u.CreatedAtUtc.ToString("yyyy-MM-dd"),
            });
            return CsvWriter.BuildCsv(headers, rows);
        }

        private async Task<bool> IsIntegrationEnabledAsync(string key, CancellationToken cancellationToken)
        {
            var integration = await _unitOfWork.Repository<Integration>().Query()
                .FirstOrDefaultAsync(i => i.Key == key, cancellationToken);
            return integration is { IsEnabled: true };
        }
    }
}
