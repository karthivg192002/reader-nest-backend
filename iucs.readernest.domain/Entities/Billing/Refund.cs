using System.ComponentModel.DataAnnotations;
using iucs.readernest.domain.Entities.Common;
using iucs.readernest.domain.Enums;

namespace iucs.readernest.domain.Entities.Billing
{
    public class Refund : AuditEntity
    {
        public Guid PaymentTransactionId { get; set; }

        public PaymentTransaction PaymentTransaction { get; set; } = null!;

        public decimal Amount { get; set; }

        [MaxLength(500)]
        public string Reason { get; set; } = null!;

        public RefundStatus Status { get; set; } = RefundStatus.Requested;

        public DateTime? ProcessedAtUtc { get; set; }

        /// <summary>
        /// The gateway's refund id once disbursed through Razorpay/Cashfree; null for cash
        /// transactions (nothing to call a gateway for) or while still Requested/Rejected.
        /// </summary>
        [MaxLength(256)]
        public string? GatewayRefundId { get; set; }
    }
}
