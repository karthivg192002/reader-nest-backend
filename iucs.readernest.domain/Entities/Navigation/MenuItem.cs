using System.ComponentModel.DataAnnotations;
using iucs.readernest.domain.Entities.Common;
using iucs.readernest.domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace iucs.readernest.domain.Entities.Navigation
{
    /// <summary>
    /// One sidebar entry of a portal, maintained by the Admin instead of being
    /// hard-coded in the frontend. Icon is a Lucide icon name resolved client-side.
    /// </summary>
    [Index(nameof(Portal), nameof(Path), IsUnique = true)]
    public class MenuItem : AuditEntity
    {
        /// <summary>Portal key matching the frontend role shells: admin, teacher, parent, subadmin, admission, coordinator, management, student.</summary>
        [MaxLength(32)]
        public string Portal { get; set; } = null!;

        /// <summary>Section heading the item is grouped under; null for the top ungrouped block.</summary>
        [MaxLength(64)]
        public string? Section { get; set; }

        /// <summary>Order of the section within the sidebar; items sharing a section share this value.</summary>
        public int SectionOrder { get; set; }

        [MaxLength(100)]
        public string Label { get; set; } = null!;

        /// <summary>Route path, e.g. "/admin/courses".</summary>
        [MaxLength(200)]
        public string Path { get; set; } = null!;

        /// <summary>Lucide icon component name, e.g. "LayoutDashboard".</summary>
        [MaxLength(64)]
        public string Icon { get; set; } = null!;

        /// <summary>Order of the item within its section.</summary>
        public int SortOrder { get; set; }

        /// <summary>Inactive items stay configured but disappear from the sidebar.</summary>
        public bool IsActive { get; set; } = true;

        /// <summary>Optional module gate: Sub Admins only see the item when granted View on this module.</summary>
        public PermissionModule? RequiredModule { get; set; }
    }
}
