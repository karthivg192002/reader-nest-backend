using iucs.readernest.application.Dto.Users;

namespace iucs.readernest.application.Services
{
    public interface IRoleService
    {
        Task<IReadOnlyList<RoleDto>> ListAsync(CancellationToken cancellationToken = default);

        Task<RoleDto> CreateAsync(SaveRoleRequest request, CancellationToken cancellationToken = default);

        Task<RoleDto> UpdateAsync(Guid id, SaveRoleRequest request, CancellationToken cancellationToken = default);

        Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);

        /// <summary>The role's permission matrix by role name; null when no such role exists.</summary>
        Task<IReadOnlyList<PermissionDto>?> ResolvePermissionsAsync(string name, CancellationToken cancellationToken = default);
    }
}
