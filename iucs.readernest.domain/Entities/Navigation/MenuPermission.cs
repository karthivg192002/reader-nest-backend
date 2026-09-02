using iucs.readernest.domain.Entities.Common;
using iucs.readernest.domain.Entities.Users;
using Microsoft.EntityFrameworkCore;

namespace iucs.readernest.domain.Entities.Navigation
{
    /// <summary>
    /// Direct menu-to-role (or menu-to-user, reserved for a future per-user override — unused
    /// today) grant: View/Create/Edit/Delete/Approve rights on one <see cref="MenuItem"/>. This
    /// is now the real source of API authorization for Sub Admin/Teacher/Parent/AdmissionTeam:
    /// AuthService.LoadPermissionClaimsAsync aggregates these rows (OR'd across every menu item
    /// sharing a module) into the "Module:Action" JWT claims every [HasPermission] check reads —
    /// see MenuService.GetModulePermissionClaimsAsync. RolePermission/SubAdminPermission remain
    /// in the schema (still editable from the old Roles and Permissions page) but only
    /// SubAdminPermission still feeds real claims, as an additive overlay purely so Access
    /// Request approval keeps working.
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

        /// <summary>A distinct second sign-off action (leave/enrollment/access-request/refund/
        /// cash-intent/fee-suspension review) — not implied by Edit, mirrors the module system's
        /// own separate Approve action.</summary>
        public bool CanApprove { get; set; }

        public bool IsActive { get; set; } = true;
    }
}
