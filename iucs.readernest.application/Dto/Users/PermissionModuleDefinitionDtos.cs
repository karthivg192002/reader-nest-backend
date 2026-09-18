using System.ComponentModel.DataAnnotations;

namespace iucs.readernest.application.Dto.Users
{
    public class PermissionModuleDefinitionDto
    {
        public Guid Id { get; set; }

        public string Key { get; set; } = null!;

        public string Label { get; set; } = null!;

        public string? Description { get; set; }

        public bool IsSystem { get; set; }

        public int SortOrder { get; set; }
    }

    public class SavePermissionModuleDefinitionRequest
    {
        /// <summary>Immutable identifier — same rules as a role's identifier: letters/digits
        /// only, no spaces, no colon (the claim string format is "Key:Action").</summary>
        [Required]
        [MaxLength(64)]
        public string Key { get; set; } = null!;

        [Required]
        [MaxLength(100)]
        public string Label { get; set; } = null!;

        [MaxLength(300)]
        public string? Description { get; set; }
    }
}
