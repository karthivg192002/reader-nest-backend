using System.ComponentModel.DataAnnotations;
using iucs.readernest.domain.Enums;

namespace iucs.readernest.application.Dto.Navigation
{
    public class MenuItemDto
    {
        public Guid Id { get; set; }

        public string Portal { get; set; } = null!;

        public string? Section { get; set; }

        public int SectionOrder { get; set; }

        public string Label { get; set; } = null!;

        public string Path { get; set; } = null!;

        public string Icon { get; set; } = null!;

        public int SortOrder { get; set; }

        public bool IsActive { get; set; }

        public PermissionModule? RequiredModule { get; set; }

        /// <summary>
        /// The signed-in caller's own Create/Edit/Delete/Approve rights on this menu item —
        /// only ever populated by MenuService.GetForUserAsync (the /api/menus/mine response).
        /// Every other MenuItemDto producer (the admin menu manager's list/create/update/delete)
        /// has no single "viewer" to resolve these against, so they stay false there — harmless,
        /// since nothing reads them outside the per-user sidebar response.
        /// </summary>
        public bool CanCreate { get; set; }

        public bool CanEdit { get; set; }

        public bool CanDelete { get; set; }

        public bool CanApprove { get; set; }
    }

    /// <remarks>Lengths mirror the MenuItem entity's columns — see SaveIntegrationRequest for why.</remarks>
    public class SaveMenuItemRequest
    {
        [Required]
        [MaxLength(32)]
        public string Portal { get; set; } = null!;

        [MaxLength(64)]
        public string? Section { get; set; }

        public int SectionOrder { get; set; }

        [Required]
        [MaxLength(100)]
        public string Label { get; set; } = null!;

        [Required]
        [MaxLength(200)]
        public string Path { get; set; } = null!;

        [Required]
        [MaxLength(64)]
        public string Icon { get; set; } = null!;

        public int SortOrder { get; set; }

        public bool IsActive { get; set; } = true;

        public PermissionModule? RequiredModule { get; set; }
    }
}
