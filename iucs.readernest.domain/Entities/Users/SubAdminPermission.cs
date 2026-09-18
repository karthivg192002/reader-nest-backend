using iucs.readernest.domain.Entities.Common;
using Microsoft.EntityFrameworkCore;

namespace iucs.readernest.domain.Entities.Users
{
    /// <summary>
    /// Module-wise, feature-level access granted by the Admin to a Sub Admin user.
    /// 'Academic Coordinator' and 'Management' personas are presets of these rows.
    /// Sub Admins have no access by default (no row = no access). Module is a
    /// <see cref="PermissionModuleDefinition.Key"/> — either a built-in
    /// <see cref="Enums.PermissionModule"/> enum name or an Admin-defined custom module.
    /// </summary>
    [Index(nameof(UserId), nameof(Module), IsUnique = true)]
    public class SubAdminPermission : AuditEntity
    {
        public Guid UserId { get; set; }

        public User User { get; set; } = null!;

        public string Module { get; set; } = null!;

        public bool CanView { get; set; }

        public bool CanCreate { get; set; }

        public bool CanEdit { get; set; }

        public bool CanDelete { get; set; }

        public bool CanApprove { get; set; }
    }
}
