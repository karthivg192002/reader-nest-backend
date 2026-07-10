using System.ComponentModel.DataAnnotations;
using iucs.readernest.domain.Entities.Common;
using iucs.readernest.domain.Entities.Sessions;
using iucs.readernest.domain.Enums;

namespace iucs.readernest.domain.Entities.Payouts
{
    /// <summary>
    /// Line item on a monthly payout, auto-added when a class completes.
    /// Amount is signed: earnings and student no-show waiting amounts are positive,
    /// teacher no-show deductions and penalties are negative.
    /// </summary>
    public class PayoutItem : BaseEntity
    {
        public Guid PayoutId { get; set; }

        public Payout Payout { get; set; } = null!;

        public Guid? ClassSessionId { get; set; }

        public ClassSession? ClassSession { get; set; }

        public PayoutItemType Type { get; set; }

        public decimal Amount { get; set; }

        [MaxLength(500)]
        public string? Note { get; set; }
    }
}
