using System.ComponentModel.DataAnnotations;

namespace iucs.readernest.application.Dto.Admission
{
    /// <summary>
    /// Admission done by the counselor on the parent's behalf after a demo — for parents who
    /// don't use email and want everything shared on WhatsApp. Everything the parent would
    /// otherwise fill in themselves (enrollment form) is entered here.
    /// </summary>
    public class ManualAdmissionRequest
    {
        [Required]
        [MaxLength(200)]
        public string ParentName { get; set; } = null!;

        /// <summary>Optional. When blank, <see cref="ParentPhone"/> becomes the parent's login.</summary>
        [MaxLength(256)]
        public string? ParentEmail { get; set; }

        [MaxLength(20)]
        public string? ParentPhone { get; set; }

        [Required]
        [MaxLength(100)]
        public string ChildFirstName { get; set; } = null!;

        [MaxLength(100)]
        public string? ChildLastName { get; set; }

        [Required]
        public DateOnly ChildDateOfBirth { get; set; }

        /// <summary>Course plan to bill — issues the first invoice and the payment link.</summary>
        [Required]
        public Guid PackagePlanId { get; set; }

        public Guid? BatchId { get; set; }
    }

    /// <summary>Pick-lists for the "Admit manually" dialog (active plans and batches), served under
    /// the Admission permission so counselors without Billing/Batch access can still use it.</summary>
    public class ManualAdmissionOptionsDto
    {
        public List<ManualAdmissionPlanOption> Plans { get; set; } = [];

        public List<ManualAdmissionBatchOption> Batches { get; set; } = [];
    }

    public class ManualAdmissionPlanOption
    {
        public Guid Id { get; set; }

        public string Name { get; set; } = null!;

        public string? CourseName { get; set; }

        public decimal Price { get; set; }
    }

    public class ManualAdmissionBatchOption
    {
        public Guid Id { get; set; }

        public string Name { get; set; } = null!;

        public string CourseName { get; set; } = null!;

        public string TeacherName { get; set; } = null!;

        public int SeatsLeft { get; set; }
    }

    public class ManualAdmissionResultDto
    {
        public DemoBookingDto Booking { get; set; } = null!;

        public Guid ParentUserId { get; set; }

        public Guid ChildId { get; set; }

        /// <summary>What the parent types in the login box: their email, or mobile number when they have none.</summary>
        public string LoginId { get; set; } = null!;

        /// <summary>Only set when this admission created the parent's account; an existing parent keeps their PIN.</summary>
        public string? TemporaryPin { get; set; }

        public string LoginUrl { get; set; } = null!;

        public Guid? InvoiceId { get; set; }

        public string? InvoiceNumber { get; set; }

        public decimal AmountDue { get; set; }

        public string Currency { get; set; } = "INR";

        /// <summary>Null when the gateway couldn't make one (the parent can still pay from the portal).</summary>
        public string? PaymentLinkUrl { get; set; }

        public string? PaymentLinkError { get; set; }

        /// <summary>Ready-to-paste WhatsApp message with the login details and payment link.</summary>
        public string WhatsAppMessage { get; set; } = null!;
    }
}
