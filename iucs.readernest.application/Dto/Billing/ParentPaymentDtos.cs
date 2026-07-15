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
}
