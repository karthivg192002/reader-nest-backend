using iucs.readernest.application.Dto.Common;
using iucs.readernest.application.Dto.Users;
using iucs.readernest.domain.Enums;

namespace iucs.readernest.application.Services
{
    public interface IUserService
    {
        Task<PagedResult<UserDto>> ListAsync(
            UserRole? role,
            string? search,
            int page,
            int pageSize,
            CancellationToken cancellationToken = default);

        Task<UserDto> GetAsync(Guid id, CancellationToken cancellationToken = default);

        /// <summary>Active teachers with their profile ids, for assignment dropdowns.</summary>
        Task<IReadOnlyList<TeacherOptionDto>> ListTeachersAsync(CancellationToken cancellationToken = default);

        Task<UserDto> CreateAsync(CreateUserRequest request, CancellationToken cancellationToken = default);

        Task<UserDto> UpdateAsync(Guid id, UpdateUserRequest request, CancellationToken cancellationToken = default);

        Task<UserDto> SetStatusAsync(Guid id, UserStatus status, CancellationToken cancellationToken = default);

        /// <summary>
        /// Converts the account to a different base type (Parent/Teacher/AdmissionTeam/SubAdmin),
        /// swapping the type-specific profile record. Refuses when the current type has real
        /// operational history behind it (a parent with children, a teacher with assigned
        /// sessions) so existing data is never silently orphaned. Admin is never a valid
        /// source or target — those accounts are untouchable through this action.
        /// </summary>
        Task<UserDto> ChangeRoleAsync(Guid id, UserRole newRole, CancellationToken cancellationToken = default);

        Task<IReadOnlyList<PermissionDto>> GetPermissionsAsync(Guid userId, CancellationToken cancellationToken = default);

        Task SetPermissionsAsync(
            Guid userId,
            IReadOnlyList<PermissionDto> permissions,
            Guid? roleDefinitionId = null,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Regenerates the account's temporary password and delivers a welcome
        /// message with the new credentials over the chosen channel. The password
        /// is only reset once delivery succeeds, so a failed send never locks the
        /// account out with an undelivered password.
        /// </summary>
        Task ResendCredentialsAsync(Guid userId, CredentialChannel channel, CancellationToken cancellationToken = default);

        /// <summary>Which credential-delivery channels are enabled in Settings → Integrations, so the UI shows only usable buttons.</summary>
        Task<CredentialChannelsDto> GetCredentialChannelsAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Soft-deletes the account (the audit interceptor sets IsDeleted/DeletedAtUtc;
        /// the row and its email are excluded from all future queries/uniqueness checks).
        /// Refuses to delete the caller's own account or the last remaining Admin.
        /// </summary>
        Task DeleteAsync(Guid id, Guid currentUserId, CancellationToken cancellationToken = default);
    }
}
