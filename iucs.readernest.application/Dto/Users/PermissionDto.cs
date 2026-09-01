namespace iucs.readernest.application.Dto.Users
{
    public class PermissionDto
    {
        /// <summary>A PermissionModuleDefinition.Key — built-in enum name or a custom module.</summary>
        public string Module { get; set; } = null!;

        public bool CanView { get; set; }

        public bool CanCreate { get; set; }

        public bool CanEdit { get; set; }

        public bool CanDelete { get; set; }

        public bool CanApprove { get; set; }
    }
}
