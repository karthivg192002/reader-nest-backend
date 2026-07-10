using System.ComponentModel.DataAnnotations;
using iucs.readernest.domain.Entities.Academics;
using iucs.readernest.domain.Entities.Common;
using iucs.readernest.domain.Enums;

namespace iucs.readernest.domain.Entities.Billing
{
    /// <summary>Sellable package: subscription, session-based or one-time charging.</summary>
    public class PackagePlan : AuditEntity
    {
        [MaxLength(150)]
        public string Name { get; set; } = null!;

        public Guid? CourseId { get; set; }

        public Course? Course { get; set; }

        public BillingType BillingType { get; set; }

        public BillingCycle BillingCycle { get; set; }

        public decimal Price { get; set; }

        public int? SessionsIncluded { get; set; }

        public bool IsActive { get; set; } = true;
    }
}
