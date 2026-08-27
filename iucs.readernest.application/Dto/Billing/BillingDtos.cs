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

        /// <summary>How many days of access this plan grants from a subscription's start date; null means the plan never expires on its own (a recurring Subscription plan typically leaves this unset — BillingCycle already governs it).</summary>
        public int? ValidityDays { get; set; }

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

        /// <summary>Days of access from a subscription's start date; leave unset for a plan that never expires on its own.</summary>
        [Range(1, 3650)]
        public int? ValidityDays { get; set; }

        public bool IsActive { get; set; } = true;
    }

    public class InvoiceDto
    {
        public Guid Id { get; set; }

        public string InvoiceNumber { get; set; } = null!;

        public Guid ParentProfileId { get; set; }

        public Guid? ChildId { get; set; }

        /// <summary>Resolved display name for the invoiced child; null when the invoice has no child linked.</summary>
        public string? ChildName { get; set; }

        public Guid? CourseId { get; set; }

        /// <summary>Resolved course name — direct via CourseId when set, else via the invoice's subscription/plan.</summary>
        public string? CourseName { get; set; }

        /// <summary>Resolved display name for the invoicing parent — the account holder, not the child.</summary>
        public string? ParentName { get; set; }

        public string? ParentEmail { get; set; }

        public Guid DepartmentId { get; set; }

        public string DepartmentName { get; set; } = null!;

        public decimal Amount { get; set; }

        public decimal AmountPaid { get; set; }

        public string Currency { get; set; } = null!;

        public InvoiceStatus Status { get; set; }

        public DateOnly DueDate { get; set; }

        public DateTime IssuedAtUtc { get; set; }

        public DateTime? PaidAtUtc { get; set; }
    }

    /// <summary>
    /// Everything InvoicePdfGenerator needs to render one invoice — deliberately its own small
    /// shape (not InvoiceDto) so the PDF layout doesn't silently break/change if InvoiceDto ever
    /// gains or drops fields for unrelated (screen-display) reasons.
    /// </summary>
    public class InvoicePdfData
    {
        public string InvoiceNumber { get; set; } = null!;

        public DateTime IssuedAtUtc { get; set; }

        public string ParentName { get; set; } = null!;

        public string? ParentPhone { get; set; }

        /// <summary>Line description — the invoiced course's name, or a generic fallback when none is linked.</summary>
        public string Description { get; set; } = null!;

        public decimal Amount { get; set; }

        public string Currency { get; set; } = null!;

        // Org payment/GST/signatory details — admin-editable (Settings → General → Invoice
        // Details, "invoice.*" keys), same on every invoice. BillingService resolves these
        // from AppSetting, falling back to a "Not configured" placeholder until an admin
        // fills them in — never a hardcoded real value (see BillingService.InvoiceSettingKeys).
        public string AccountNumber { get; set; } = null!;

        public string IfscCode { get; set; } = null!;

        public string BranchName { get; set; } = null!;

        public string GstNumber { get; set; } = null!;

        public string AccountName { get; set; } = null!;

        public string ContactEmail { get; set; } = null!;

        public string SignatoryName { get; set; } = null!;

        public string SignatoryTitle { get; set; } = null!;
    }

    public class CreateInvoiceRequest
    {
        [Required]
        public Guid ParentProfileId { get; set; }

        public Guid? ChildId { get; set; }

        public Guid? SubscriptionId { get; set; }

        /// <summary>Course this invoice bills for, when known (e.g. resolved from the plan at the call site).</summary>
        public Guid? CourseId { get; set; }

        /// <summary>Routes the invoice to the department's payment account (dual-gateway requirement).</summary>
        [Required]
        public Guid DepartmentId { get; set; }

        [Required]
        [Range(0.01, 9_999_999)]
        public decimal Amount { get; set; }

        [Required]
        public DateOnly DueDate { get; set; }
    }

    public class SubscriptionDto
    {
        public Guid Id { get; set; }

        public Guid ParentProfileId { get; set; }

        public Guid ChildId { get; set; }

        public string ChildName { get; set; } = null!;

        public Guid PackagePlanId { get; set; }

        public string PlanName { get; set; } = null!;

        public SubscriptionStatus Status { get; set; }

        public DateOnly StartDate { get; set; }

        /// <summary>When this subscription's access lapses on its own, from the plan's ValidityDays; null for a plan with no set validity window.</summary>
        public DateOnly? EndDate { get; set; }

        public DateTime? NextBillingAtUtc { get; set; }

        public DateTime? CancelledAtUtc { get; set; }
    }

    public class CreateSubscriptionRequest
    {
        [Required]
        public Guid ParentProfileId { get; set; }

        [Required]
        public Guid ChildId { get; set; }

        [Required]
        public Guid PackagePlanId { get; set; }

        [Required]
        public DateOnly StartDate { get; set; }
    }

    public class FeeSuspensionDto
    {
        public Guid Id { get; set; }

        public Guid ParentProfileId { get; set; }

        public string ParentName { get; set; } = null!;

        public Guid? InvoiceId { get; set; }

        public string? InvoiceNumber { get; set; }

        public string? Reason { get; set; }

        public SuspensionStatus Status { get; set; }

        public DateTime SuspendedAtUtc { get; set; }

        public DateTime? LiftedAtUtc { get; set; }

        public bool AutoRestored { get; set; }
    }

    /// <summary>One payment attempt against an invoice — the unit a refund is requested against.</summary>
    public class PaymentTransactionDto
    {
        public Guid Id { get; set; }

        public decimal Amount { get; set; }

        public string Status { get; set; } = null!;

        public string? Method { get; set; }

        public DateTime? PaidAtUtc { get; set; }

        public string? ReceiptNumber { get; set; }

        /// <summary>Sum of this transaction's non-rejected refunds — how much of it is already spoken for.</summary>
        public decimal AlreadyRefunded { get; set; }
    }

    public class RefundDto
    {
        public Guid Id { get; set; }

        public Guid PaymentTransactionId { get; set; }

        public string? InvoiceNumber { get; set; }

        public decimal Amount { get; set; }

        public string Reason { get; set; } = null!;

        public RefundStatus Status { get; set; }

        public DateTime? ProcessedAtUtc { get; set; }

        /// <summary>Gateway refund id once disbursed for real; null for cash or while still Requested/Rejected.</summary>
        public string? GatewayRefundId { get; set; }
    }

    public class RequestRefundRequest
    {
        [Required]
        public Guid PaymentTransactionId { get; set; }

        [Required]
        [Range(0.01, 9_999_999)]
        public decimal Amount { get; set; }

        [Required]
        [MaxLength(500)]
        public string Reason { get; set; } = null!;
    }

    public class ReviewRefundRequest
    {
        [Required]
        public bool Approve { get; set; }
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
