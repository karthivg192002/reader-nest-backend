using iucs.readernest.domain.Entities.Academics;
using iucs.readernest.domain.Entities.Common;
using Microsoft.EntityFrameworkCore;

namespace iucs.readernest.domain.Entities.Resources
{
    /// <summary>
    /// Which batch(es) a resource is visible to — the uploader (teacher) chooses them
    /// at upload time. Complements the legacy single Resource.BatchId.
    /// </summary>
    [Index(nameof(ResourceId), nameof(BatchId), IsUnique = true)]
    public class ResourceBatchVisibility : BaseEntity
    {
        public Guid ResourceId { get; set; }

        public Resource Resource { get; set; } = null!;

        public Guid BatchId { get; set; }

        public Batch Batch { get; set; } = null!;
    }
}
