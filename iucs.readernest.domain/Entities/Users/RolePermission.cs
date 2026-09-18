using iucs.readernest.domain.Entities.Common;
using Microsoft.EntityFrameworkCore;

namespace iucs.readernest.domain.Entities.Users
{
    /// <summary>
    /// Module-level grants a <see cref="RoleDefinition"/> carries; mirrors <see cref="SubAdminPermission"/>.
    /// Module is a <see cref="PermissionModuleDefinition.Key"/> — either a built-in
    /// <see cref="Enums.PermissionModule"/> enum name or an Admin-defined custom module.
    /// </summary>
    [Index(nameof(RoleDefinitionId), nameof(Module), IsUnique = true)]
    public class RolePermission : AuditEntity
    {
        public Guid RoleDefinitionId { get; set; }

        public RoleDefinition RoleDefinition { get; set; } = null!;

        public string Module { get; set; } = null!;

        public bool CanView { get; set; }

        public bool CanCreate { get; set; }

        public bool CanEdit { get; set; }

        public bool CanDelete { get; set; }

        public bool CanApprove { get; set; }
    }
}
