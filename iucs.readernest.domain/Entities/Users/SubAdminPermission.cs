using iucs.readernest.domain.Entities.Common;
using iucs.readernest.domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace iucs.readernest.domain.Entities.Users
{
    /// <summary>
    /// Module-wise, feature-level access granted by the Admin to a Sub Admin user.
    /// 'Academic Coordinator' and 'Management' personas are presets of these rows.
    /// Sub Admins have no access by default (no row = no access).
    /// </summary>
    [Index(nameof(UserId), nameof(Module), IsUnique = true)]
    public class SubAdminPermission : AuditEntity
    {
        public Guid UserId { get; set; }

        public User User { get; set; } = null!;

        public PermissionModule Module { get; set; }

        public bool CanView { get; set; }

        public bool CanCreate { get; set; }

        public bool CanEdit { get; set; }

        public bool CanDelete { get; set; }

        public bool CanApprove { get; set; }
    }
}
