using System.ComponentModel.DataAnnotations;
using iucs.readernest.domain.Enums;

namespace iucs.readernest.application.Dto.Admission
{
    /// <summary>Public course-catalog card — deliberately smaller than the admin PackagePlanDto.</summary>
    public class StorePlanDto
    {
        public Guid Id { get; set; }

        public string Name { get; set; } = null!;

        public string? CourseName { get; set; }

        public BillingType BillingType { get; set; }

        public BillingCycle BillingCycle { get; set; }

        public decimal Price { get; set; }

        public int? SessionsIncluded { get; set; }
    }

    public class CreateStoreInquiryRequest
    {
        [Required]
        public Guid PackagePlanId { get; set; }

        [Required]
        [MaxLength(150)]
        public string ParentName { get; set; } = null!;

        [Required]
        [EmailAddress]
        [MaxLength(256)]
        public string ParentEmail { get; set; } = null!;

        [Required]
        [MaxLength(20)]
        public string ParentPhone { get; set; } = null!;

        [Required]
        [MaxLength(150)]
        public string ChildName { get; set; } = null!;

        [Range(1, 25)]
        public int? ChildAge { get; set; }

        [MaxLength(1000)]
        public string? Notes { get; set; }
    }

    public class StoreInquiryDto
    {
        public Guid Id { get; set; }

        public Guid PackagePlanId { get; set; }

        public string PlanName { get; set; } = null!;

        public string ParentName { get; set; } = null!;

        public string ParentEmail { get; set; } = null!;

        public string ParentPhone { get; set; } = null!;

        public string ChildName { get; set; } = null!;

        public int? ChildAge { get; set; }

        public string? Notes { get; set; }

        public StoreInquiryStatus Status { get; set; }

        public DateTime CreatedAtUtc { get; set; }
    }

    public class UpdateStoreInquiryStatusRequest
    {
        [Required]
        public StoreInquiryStatus Status { get; set; }
    }

    /// <summary>Public "Book a free demo" submission — no account, no login, no teacher choice.</summary>
    public class CreateStoreDemoBookingRequest
    {
        [Required]
        [MaxLength(150)]
        public string ParentName { get; set; } = null!;

        [Required]
        [EmailAddress]
        [MaxLength(256)]
        public string ParentEmail { get; set; } = null!;

        [Required]
        [MaxLength(20)]
        public string ParentPhone { get; set; } = null!;

        [Required]
        [MaxLength(150)]
        public string ChildName { get; set; } = null!;

        [Range(1, 25)]
        public int? ChildAge { get; set; }

        public Guid? DepartmentId { get; set; }

        /// <summary>The slot the visitor picked; the service fills in a fixed 30-minute end time.</summary>
        [Required]
        public DateTime PreferredStartAtUtc { get; set; }
    }

    /// <summary>Deliberately minimal — a public caller only needs to know it worked and when.</summary>
    public class StoreDemoBookingConfirmationDto
    {
        public Guid Id { get; set; }

        public DateTime ScheduledStartAtUtc { get; set; }

        public DateTime ScheduledEndAtUtc { get; set; }
    }

    /// <summary>One bookable 30-minute demo start time, with at least one matching teacher free.</summary>
    public class AvailableDemoSlotDto
    {
        public DateTime StartAtUtc { get; set; }

        public DateTime EndAtUtc { get; set; }
    }
}
