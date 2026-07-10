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

        Task<IReadOnlyList<PermissionDto>> GetPermissionsAsync(Guid userId, CancellationToken cancellationToken = default);

        Task SetPermissionsAsync(Guid userId, IReadOnlyList<PermissionDto> permissions, CancellationToken cancellationToken = default);
    }
}
