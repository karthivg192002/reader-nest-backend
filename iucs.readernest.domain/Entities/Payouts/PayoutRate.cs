using iucs.readernest.domain.Entities.Common;
using iucs.readernest.domain.Entities.Users;
using Microsoft.EntityFrameworkCore;

namespace iucs.readernest.domain.Entities.Payouts
{
    /// <summary>
    /// Admin-configured per-session rate, set per teacher per batch duration
    /// (e.g. a different rate for 30/45/60-minute classes). Rate changes create a
    /// new row with a later EffectiveFrom so historical payouts stay reproducible.
    /// </summary>
    [Index(nameof(TeacherProfileId), nameof(DurationMinutes), nameof(EffectiveFrom), IsUnique = true)]
    public class PayoutRate : AuditEntity
    {
        public Guid TeacherProfileId { get; set; }

        public TeacherProfile TeacherProfile { get; set; } = null!;

        public int DurationMinutes { get; set; }

        public decimal RatePerSession { get; set; }

        /// <summary>
        /// Teacher no-show deduction as a percentage of the session rate (WBS p.31
        /// "Penalty configuration"): 100 deducts the full rate, 50 half, 150 a stiffer
        /// deterrent. Applied when a session is marked TeacherNoShow.
        /// </summary>
        public decimal TeacherNoShowPenaltyPercent { get; set; } = 100m;

        public DateOnly EffectiveFrom { get; set; }

        public bool IsActive { get; set; } = true;
    }
}
