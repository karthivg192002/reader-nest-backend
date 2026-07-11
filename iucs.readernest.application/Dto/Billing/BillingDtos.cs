using System.ComponentModel.DataAnnotations;
using iucs.readernest.domain.Enums;

namespace iucs.readernest.application.Dto.Billing
{
    public class PackagePlanDto
    {
        public Guid Id { get; set; }

        public string Name { get; set; } = null!;

        public Guid? CourseId { get; set; }

        public BillingType BillingType { get; set; }

        public BillingCycle BillingCycle { get; set; }

        public decimal Price { get; set; }

        public int? SessionsIncluded { get; set; }

        public bool IsActive { get; set; }
    }

    public class SavePackagePlanRequest
    {
        [Required]
        [MaxLength(150)]
        public string Name { get; set; } = null!;

        public Guid? CourseId { get; set; }

        [Required]
        public BillingType BillingType { get; set; }

        [Required]
        public BillingCycle BillingCycle { get; set; }

        [Required]
        [Range(0, 9_999_999)]
        public decimal Price { get; set; }

        [Range(1, 1000)]
        public int? SessionsIncluded { get; set; }

        public bool IsActive { get; set; } = true;
    }

    public class InvoiceDto
    {
        public Guid Id { get; set; }

        public string InvoiceNumber { get; set; } = null!;

        public Guid ParentProfileId { get; set; }

        public Guid? ChildId { get; set; }

        public Department Department { get; set; }

        public decimal Amount { get; set; }

        public decimal AmountPaid { get; set; }

        public string Currency { get; set; } = null!;

        public InvoiceStatus Status { get; set; }

        public DateOnly DueDate { get; set; }

        public DateTime IssuedAtUtc { get; set; }

        public DateTime? PaidAtUtc { get; set; }
    }

    public class CreateInvoiceRequest
    {
        [Required]
        public Guid ParentProfileId { get; set; }

        public Guid? ChildId { get; set; }

        public Guid? SubscriptionId { get; set; }

        /// <summary>Routes the invoice to the department's payment account (dual-gateway requirement).</summary>
        [Required]
        public Department Department { get; set; }

        [Required]
        [Range(0.01, 9_999_999)]
        public decimal Amount { get; set; }

        [Required]
        public DateOnly DueDate { get; set; }
    }

    /// <summary>Shareable Pay Now link routed through the invoice's department account.</summary>
    public class PaymentLinkDto
    {
        public Guid InvoiceId { get; set; }

        public string InvoiceNumber { get; set; } = null!;

        public string Url { get; set; } = null!;

        public string GatewayReference { get; set; } = null!;

        public decimal AmountDue { get; set; }
    }

    public class RecordPaymentRequest
    {
        [Required]
        [Range(0.01, 9_999_999)]
        public decimal Amount { get; set; }

        public PaymentMethod? Method { get; set; }

        /// <summary>Gateway transaction reference; null for manually recorded payments.</summary>
        [MaxLength(256)]
        public string? GatewayTransactionId { get; set; }
    }
}
