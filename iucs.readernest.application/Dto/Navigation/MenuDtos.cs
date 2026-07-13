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
    }

    public class SaveMenuItemRequest
    {
        public string Portal { get; set; } = null!;

        public string? Section { get; set; }

        public int SectionOrder { get; set; }

        public string Label { get; set; } = null!;

        public string Path { get; set; } = null!;

        public string Icon { get; set; } = null!;

        public int SortOrder { get; set; }

        public bool IsActive { get; set; } = true;

        public PermissionModule? RequiredModule { get; set; }
    }
}
