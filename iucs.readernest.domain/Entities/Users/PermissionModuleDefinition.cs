using iucs.readernest.domain.Entities.Common;
using Microsoft.EntityFrameworkCore;

namespace iucs.readernest.domain.Entities.Users
{
    /// <summary>
    /// A grantable permission module — the 12 built-in ones (seeded, IsSystem = true, Key
    /// matching a <see cref="Enums.PermissionModule"/> enum name) plus any an Admin defines
    /// from the UI. A custom module can only ever gate a <see cref="Navigation.MenuItem"/>'s
    /// visibility, never a specific API action: enforcing a brand-new capability requires a
    /// [HasPermission] attribute already sitting on compiled code, which by definition can't
    /// exist yet for a module invented at runtime. Key is immutable after creation (same
    /// precedent as RoleDefinition.Name for a system role) since RolePermission/
    /// SubAdminPermission/MenuItem all reference it by value, not by Id.
    /// </summary>
    [Index(nameof(Key), IsUnique = true)]
    public class PermissionModuleDefinition : AuditEntity
    {
        public string Key { get; set; } = null!;

        public string Label { get; set; } = null!;

        public string? Description { get; set; }

        public bool IsSystem { get; set; }

        public int SortOrder { get; set; }
    }
}
