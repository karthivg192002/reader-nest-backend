using System.ComponentModel.DataAnnotations;
using iucs.readernest.domain.Entities.Common;
using Microsoft.EntityFrameworkCore;

namespace iucs.readernest.domain.Entities.Users
{
    /// <summary>
    /// Named, DB-maintained permission role (preset): applying one to a Sub Admin
    /// replaces their grants with the role's matrix. System roles (Academic
    /// Coordinator, Management) are seeded and cannot be deleted or renamed.
    /// </summary>
    [Index(nameof(Name), IsUnique = true)]
    public class RoleDefinition : AuditEntity
    {
        /// <summary>Stable kebab-case identifier used by the preset API, e.g. "academic-coordinator".</summary>
        [MaxLength(64)]
        public string Name { get; set; } = null!;

        [MaxLength(100)]
        public string DisplayName { get; set; } = null!;

        [MaxLength(500)]
        public string? Description { get; set; }

        /// <summary>
        /// Frontend route a user assigned this role lands on after login, e.g. "/subadmin/reports".
        /// Null falls back to the generic portal home for the user's account type.
        /// </summary>
        [MaxLength(200)]
        public string? DefaultRoute { get; set; }

        /// <summary>Seeded roles the platform depends on; protected from rename/delete.</summary>
        public bool IsSystem { get; set; }

        public ICollection<RolePermission> Permissions { get; set; } = new List<RolePermission>();
    }

    /// <summary>
    /// System role names that mirror a fixed-portal UserRole (Admin/Teacher/Parent/
    /// AdmissionTeam) or exist purely to back a non-staff feature (Student, seeded only
    /// for the Parent's own "Student View" preview) — never a valid preset for a Sub
    /// Admin account. Stamping one of these onto a Sub Admin's RoleDefinitionId would
    /// make MenuService/AuthService's DefaultRoute lookup land — and let RequireAuth
    /// admit — that Sub Admin straight into the wrong portal, or grant a real staff
    /// account the zero-permission Student preset by mistake (as happened once via
    /// CreateAsync before this set was enforced there too).
    /// </summary>
    public static class NonSubAdminPresetNames
    {
        public static readonly IReadOnlySet<string> Names =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "admin", "teacher", "parent", "admission", "student" };
    }
}
