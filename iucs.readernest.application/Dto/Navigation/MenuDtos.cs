using System.ComponentModel.DataAnnotations;

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

        public string? RequiredModule { get; set; }
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

        public string? RequiredModule { get; set; }
    }
}
