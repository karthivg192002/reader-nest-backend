using iucs.readernest.domain.Entities.Common;
using iucs.readernest.domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace iucs.readernest.domain.Entities.Users
{
    /// <summary>Module-level grants a <see cref="RoleDefinition"/> carries; mirrors <see cref="SubAdminPermission"/>.</summary>
    [Index(nameof(RoleDefinitionId), nameof(Module), IsUnique = true)]
    public class RolePermission : AuditEntity
    {
        public Guid RoleDefinitionId { get; set; }

        public RoleDefinition RoleDefinition { get; set; } = null!;

        public PermissionModule Module { get; set; }

        public bool CanView { get; set; }

        public bool CanCreate { get; set; }

        public bool CanEdit { get; set; }

        public bool CanDelete { get; set; }

        public bool CanApprove { get; set; }
    }
}
