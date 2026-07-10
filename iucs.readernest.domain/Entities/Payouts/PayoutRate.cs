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

        public DateOnly EffectiveFrom { get; set; }

        public bool IsActive { get; set; } = true;
    }
}
