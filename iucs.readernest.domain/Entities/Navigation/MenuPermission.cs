using iucs.readernest.domain.Entities.Common;
using iucs.readernest.domain.Entities.Users;
using Microsoft.EntityFrameworkCore;

namespace iucs.readernest.domain.Entities.Navigation
{
    /// <summary>
    /// Direct menu-to-role (or menu-to-user, reserved for a future per-user override — unused
    /// today) grant: View/Create/Edit/Delete rights on one <see cref="MenuItem"/>. Introduced
    /// alongside the existing module-level RolePermission/SubAdminPermission tables as an
    /// additive foundation — MenuService does not read this table yet, so it has no effect on
    /// sidebar visibility or API authorization until a later phase wires it in.
    /// </summary>
    [Index(nameof(MenuItemId), nameof(RoleDefinitionId), IsUnique = true)]
    [Index(nameof(MenuItemId), nameof(UserId), IsUnique = true)]
    public class MenuPermission : AuditEntity
    {
        public Guid MenuItemId { get; set; }

        public MenuItem MenuItem { get; set; } = null!;

        /// <summary>Set for a role-level grant. Exactly one of this and <see cref="UserId"/> must be set.</summary>
        public Guid? RoleDefinitionId { get; set; }

        public RoleDefinition? RoleDefinition { get; set; }

        /// <summary>Reserved for a future per-user override; no Phase 1 code path sets this.</summary>
        public Guid? UserId { get; set; }

        public User? User { get; set; }

        public bool CanView { get; set; }

        public bool CanCreate { get; set; }

        public bool CanEdit { get; set; }

        public bool CanDelete { get; set; }

        public bool IsActive { get; set; } = true;
    }
}
