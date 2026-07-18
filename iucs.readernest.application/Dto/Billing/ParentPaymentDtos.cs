using System.ComponentModel.DataAnnotations;

namespace iucs.readernest.application.Dto.Billing
{
    /// <summary>Parent Pay-Now initiation: the chosen method key from Settings → Integrations (e.g. "razorpay", "cash").</summary>
    public class InitiateParentPaymentRequest
    {
        [Required]
        [MaxLength(100)]
        public string MethodKey { get; set; } = null!;
    }

    /// <summary>
    /// Outcome of a Pay-Now initiation. "redirect" carries a gateway checkout URL;
    /// "cash" means a pending cash intent was recorded for admin confirmation.
    /// </summary>
    public class ParentPaymentResultDto
    {
        public string Mode { get; set; } = null!;

        public string? Url { get; set; }

        public string? GatewayReference { get; set; }

        public string Message { get; set; } = null!;
    }

    /// <summary>
    /// A parent's pending offline-payment declaration awaiting staff confirmation
    /// (Billing → Pending cash confirmations).
    /// </summary>
    public class CashIntentDto
    {
        public Guid TransactionId { get; set; }

        public Guid InvoiceId { get; set; }

        public string InvoiceNumber { get; set; } = null!;

        public string ParentName { get; set; } = null!;

        public decimal Amount { get; set; }

        public string Currency { get; set; } = null!;

        public string Reference { get; set; } = null!;

        public DateTime RequestedAtUtc { get; set; }
    }

    /// <summary>Staff confirmation of a collected cash amount; null Amount confirms the declared amount.</summary>
    public class ConfirmCashIntentRequest
    {
        [Range(0.01, 9_999_999)]
        public decimal? Amount { get; set; }
    }

    public class RejectCashIntentRequest
    {
        [MaxLength(500)]
        public string? Reason { get; set; }
    }
}
