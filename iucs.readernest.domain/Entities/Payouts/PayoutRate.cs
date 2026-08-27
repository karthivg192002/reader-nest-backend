using iucs.readernest.domain.Entities.Common;
using iucs.readernest.domain.Entities.Users;
using Microsoft.EntityFrameworkCore;

namespace iucs.readernest.domain.Entities.Payouts
{
    /// <summary>
    /// Admin-configured per-minute rate — one rate per teacher (or the centre default),
    /// applied to a session's scheduled duration so a 5-minute session and a 60-minute
    /// session don't pay the same. A null TeacherProfileId is the centre's DEFAULT rate
    /// card — it pays any teacher who has no rate of their own, so payroll works from
    /// one configuration; rows with a teacher override the default for that teacher.
    /// Rate changes create a new row with a later EffectiveFrom so historical payouts
    /// stay reproducible.
    /// </summary>
    [Index(nameof(TeacherProfileId), nameof(EffectiveFrom), IsUnique = true)]
    public class PayoutRate : AuditEntity
    {
        public Guid? TeacherProfileId { get; set; }

        public TeacherProfile? TeacherProfile { get; set; }

        public decimal RatePerMinute { get; set; }

        /// <summary>
        /// Teacher no-show deduction as a percentage of the session's full rate (WBS
        /// p.31 "Penalty configuration"): 100 deducts the full scheduled-duration
        /// amount, 50 half, 150 a stiffer deterrent. Applied when a session is marked
        /// TeacherNoShow.
        /// </summary>
        public decimal TeacherNoShowPenaltyPercent { get; set; } = 100m;

        public DateOnly EffectiveFrom { get; set; }

        public bool IsActive { get; set; } = true;
    }
}
