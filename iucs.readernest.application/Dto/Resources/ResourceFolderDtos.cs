using System.ComponentModel.DataAnnotations;

namespace iucs.readernest.application.Dto.Resources
{
    public class ResourceFolderDto
    {
        public Guid Id { get; set; }

        public string Name { get; set; } = null!;

        public string? Description { get; set; }

        public Guid? ParentFolderId { get; set; }

        /// <summary>Files directly in this folder (not counting subfolders).</summary>
        public int FileCount { get; set; }

        public int SubfolderCount { get; set; }

        /// <summary>Parents this folder is shared with directly.</summary>
        public int SharedParentCount { get; set; }

        public DateTime CreatedAtUtc { get; set; }
    }

    public class CreateResourceFolderRequest
    {
        [Required]
        [MaxLength(150)]
        public string Name { get; set; } = null!;

        [MaxLength(500)]
        public string? Description { get; set; }

        public Guid? ParentFolderId { get; set; }
    }

    public class UpdateResourceFolderRequest
    {
        [Required]
        [MaxLength(150)]
        public string Name { get; set; } = null!;

        [MaxLength(500)]
        public string? Description { get; set; }

        /// <summary>Where the folder should live; null = top level. Moving into itself or its own subfolder is refused.</summary>
        public Guid? ParentFolderId { get; set; }
    }

    /// <summary>A parent a folder is currently shared with.</summary>
    public class ResourceFolderAccessDto
    {
        public Guid ParentProfileId { get; set; }

        public string ParentName { get; set; } = null!;

        public string ParentEmail { get; set; } = null!;

        /// <summary>Comma-separated child names, to tell same-named parents apart.</summary>
        public string ChildNames { get; set; } = "";

        public DateTime SharedAtUtc { get; set; }
    }

    /// <summary>Adds and/or removes parents in one call; no cap on how many.</summary>
    public class SetResourceFolderAccessRequest
    {
        public List<Guid> AddParentProfileIds { get; set; } = [];

        public List<Guid> RemoveParentProfileIds { get; set; } = [];
    }

    public class MoveResourceRequest
    {
        /// <summary>Destination folder; null = top level.</summary>
        public Guid? FolderId { get; set; }
    }
}
