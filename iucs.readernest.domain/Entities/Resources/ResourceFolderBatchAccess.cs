using iucs.readernest.domain.Entities.Academics;
using iucs.readernest.domain.Entities.Common;
using Microsoft.EntityFrameworkCore;

namespace iucs.readernest.domain.Entities.Resources
{
    /// <summary>
    /// Shares a whole folder (and every subfolder and file under it) with a batch: every parent with
    /// an actively enrolled child in that batch can view and download it. Resolved at read time, so a
    /// parent who enrols later gets access with no further step, and one whose child leaves the batch
    /// loses it. A folder can be shared with any number of batches, alongside (not instead of) the
    /// per-parent shares in <see cref="ResourceFolderAccess"/>. Created and removed only by staff with
    /// Content Access edit rights.
    /// </summary>
    [Index(nameof(FolderId), nameof(BatchId), IsUnique = true)]
    public class ResourceFolderBatchAccess : BaseEntity
    {
        public Guid FolderId { get; set; }

        public ResourceFolder Folder { get; set; } = null!;

        public Guid BatchId { get; set; }

        public Batch Batch { get; set; } = null!;

        public Guid? GrantedBy { get; set; }
    }
}
