using iucs.readernest.domain.Entities.Common;
using iucs.readernest.domain.Entities.Users;
using Microsoft.EntityFrameworkCore;

namespace iucs.readernest.domain.Entities.Resources
{
    /// <summary>
    /// Shares a whole folder (and every subfolder and file under it) with one parent. A parent
    /// can hold any number of these and a folder can be shared with any number of parents.
    /// Created and removed only by staff with Content Access edit rights (Admin / RM).
    /// </summary>
    [Index(nameof(FolderId), nameof(ParentProfileId), IsUnique = true)]
    public class ResourceFolderAccess : BaseEntity
    {
        public Guid FolderId { get; set; }

        public ResourceFolder Folder { get; set; } = null!;

        public Guid ParentProfileId { get; set; }

        public ParentProfile ParentProfile { get; set; } = null!;

        public Guid? GrantedBy { get; set; }
    }
}
