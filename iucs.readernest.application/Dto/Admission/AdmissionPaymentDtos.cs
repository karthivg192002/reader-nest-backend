using System.ComponentModel.DataAnnotations;

namespace iucs.readernest.application.Dto.Admission
{
    /// <summary>Counsellor issues the parent's payment link for a course at an agreed amount.</summary>
    public class SendAdmissionPaymentLinkRequest
    {
        [Required]
        public Guid CourseId { get; set; }

        /// <summary>The final agreed (possibly discounted) amount. Omitted = the course's list price.</summary>
        [Range(0.01, 9_999_999)]
        public decimal? Amount { get; set; }
    }

    public class AdmissionPaymentLinkDto
    {
        public DemoBookingDto Booking { get; set; } = null!;

        /// <summary>Parent-facing payment page to share (it shows the Terms &amp; Conditions first).</summary>
        public string PaymentUrl { get; set; } = null!;

        public string CourseName { get; set; } = null!;

        public decimal ListPrice { get; set; }

        public decimal Amount { get; set; }

        public string Currency { get; set; } = "INR";

        /// <summary>Ready-to-copy text for the counsellor to send with the link.</summary>
        public string ShareMessage { get; set; } = null!;
    }

    /// <summary>What the anonymous /pay/{token} page needs; deliberately no ids or contact details.</summary>
    public class PublicAdmissionPaymentDto
    {
        public string ParentName { get; set; } = null!;

        public string ChildName { get; set; } = null!;

        public string CourseName { get; set; } = null!;

        public decimal Amount { get; set; }

        public decimal AmountPaid { get; set; }

        public string Currency { get; set; } = "INR";

        /// <summary>True once the invoice is fully paid -- the page then shows a thank-you instead.</summary>
        public bool IsPaid { get; set; }

        public string? TermsUrl { get; set; }

        public string? TermsText { get; set; }
    }

    public class StartPublicAdmissionPaymentRequest
    {
        /// <summary>Must be true: payment can't start until the Terms &amp; Conditions are accepted.</summary>
        public bool TermsAccepted { get; set; }
    }

    public class StartPublicAdmissionPaymentResultDto
    {
        /// <summary>The payment gateway checkout to send the parent to.</summary>
        public string Url { get; set; } = null!;
    }
}
