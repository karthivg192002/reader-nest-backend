using iucs.readernest.domain.Entities.Common;
using iucs.readernest.domain.Entities.Users;
using Microsoft.EntityFrameworkCore;

namespace iucs.readernest.domain.Entities.Resources
{
    /// <summary>
    /// Admin-controlled grant that makes a resource visible on a parent's dashboard.
    /// </summary>
    [Index(nameof(ResourceId), nameof(ParentProfileId), IsUnique = true)]
    public class ResourceAccess : BaseEntity
    {
        public Guid ResourceId { get; set; }

        public Resource Resource { get; set; } = null!;

        public Guid ParentProfileId { get; set; }

        public ParentProfile ParentProfile { get; set; } = null!;

        public bool VisibleOnDashboard { get; set; } = true;

        public Guid? GrantedBy { get; set; }
    }
}
