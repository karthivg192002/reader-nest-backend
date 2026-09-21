using System.ComponentModel.DataAnnotations;
using iucs.readernest.domain.Entities.Common;

namespace iucs.readernest.domain.Entities.Resources
{
    /// <summary>
    /// A folder in Content and Resources (e.g. "Jolly Phonics Level 1" holding a "Worksheets 1-12"
    /// subfolder). Files are uploaded once into a folder; the folder is then shared with as many
    /// parents as needed via <see cref="ResourceFolderAccess"/>, so nothing is ever uploaded twice.
    /// Folders nest (ParentFolderId) and sharing a folder also shares everything beneath it.
    /// </summary>
    public class ResourceFolder : AuditEntity
    {
        [MaxLength(150)]
        public string Name { get; set; } = null!;

        [MaxLength(500)]
        public string? Description { get; set; }

        /// <summary>Null = a top-level folder.</summary>
        public Guid? ParentFolderId { get; set; }

        public ResourceFolder? ParentFolder { get; set; }
    }
}
