using iucs.readernest.application.Dto.Users;
using iucs.readernest.domain.Entities.Users;

namespace iucs.readernest.application.Mappings
{
    public static class UserMappings
    {
        public static UserDto ToDto(this User user)
        {
            return new UserDto
            {
                Id = user.Id,
                Email = user.Email,
                FirstName = user.FirstName,
                LastName = user.LastName,
                Phone = user.Phone,
                Role = user.Role,
                Status = user.Status,
                TimeZoneId = user.TimeZoneId,
                Department = user.TeacherProfile?.Department,
                CreatedAtUtc = user.CreatedAtUtc,
                LastLoginAtUtc = user.LastLoginAtUtc,
            };
        }

        public static PermissionDto ToDto(this SubAdminPermission permission)
        {
            return new PermissionDto
            {
                Module = permission.Module,
                CanView = permission.CanView,
                CanCreate = permission.CanCreate,
                CanEdit = permission.CanEdit,
                CanDelete = permission.CanDelete,
                CanApprove = permission.CanApprove,
            };
        }
    }
}
