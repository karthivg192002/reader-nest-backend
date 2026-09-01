namespace iucs.readernest.application.Dto.Navigation
{
    /// <summary>One menu item's grant for the role the request was scoped to.</summary>
    public class MenuPermissionDto
    {
        public Guid MenuItemId { get; set; }

        public string MenuLabel { get; set; } = null!;

        public string MenuPath { get; set; } = null!;

        public bool CanView { get; set; }

        public bool CanCreate { get; set; }

        public bool CanEdit { get; set; }

        public bool CanDelete { get; set; }
    }

    /// <summary>One row of a role's full menu grant matrix, submitted together as a replace-all save.</summary>
    public class SaveMenuPermissionItem
    {
        public Guid MenuItemId { get; set; }

        public bool CanView { get; set; }

        public bool CanCreate { get; set; }

        public bool CanEdit { get; set; }

        public bool CanDelete { get; set; }
    }
}
