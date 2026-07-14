namespace iucs.readernest.application.Dto.Users
{
    public class RoleDto
    {
        public Guid Id { get; set; }

        public string Name { get; set; } = null!;

        public string DisplayName { get; set; } = null!;

        public string? Description { get; set; }

        /// <summary>Route a user assigned this role lands on after login, e.g. "/subadmin/reports".</summary>
        public string? DefaultRoute { get; set; }

        public bool IsSystem { get; set; }

        public IReadOnlyList<PermissionDto> Permissions { get; set; } = [];
    }

    public class SaveRoleRequest
    {
        public string Name { get; set; } = null!;

        public string DisplayName { get; set; } = null!;

        public string? Description { get; set; }

        public string? DefaultRoute { get; set; }

        public List<PermissionDto> Permissions { get; set; } = [];
    }
}
