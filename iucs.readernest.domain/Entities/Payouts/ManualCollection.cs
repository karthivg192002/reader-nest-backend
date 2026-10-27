using System.ComponentModel.DataAnnotations;
using iucs.readernest.domain.Entities.Common;
using iucs.readernest.domain.Entities.Users;

namespace iucs.readernest.domain.Entities.Payouts
{
    /// <summary>
    /// Cash an admission counselor collected outside the portal, entered by the admin team so it
    /// still counts toward that counselor's monthly collection -- and so toward their salary,
    /// incentive and the Admin/Founder monthly summary -- exactly like a portal payment does.
    /// CreatedBy (AuditEntity) records which admin entered it.
    /// </summary>
    public class ManualCollection : AuditEntity
    {
        /// <summary>The counselor whose collection this is.</summary>
        public Guid UserId { get; set; }

        public User User { get; set; } = null!;

        public decimal Amount { get; set; }

        /// <summary>When the cash was received; decides which IST month it counts in.</summary>
        public DateTime CollectedAtUtc { get; set; }

        /// <summary>Who paid / for which child or course, e.g. "Rahul Sharma - Phonics Level 3".</summary>
        [MaxLength(300)]
        public string? Reference { get; set; }

        [MaxLength(1000)]
        public string? Note { get; set; }
    }
}
