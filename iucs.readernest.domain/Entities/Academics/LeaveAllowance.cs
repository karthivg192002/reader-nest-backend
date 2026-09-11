using iucs.readernest.domain.Entities.Common;
using iucs.readernest.domain.Entities.Users;
using Microsoft.EntityFrameworkCore;

namespace iucs.readernest.domain.Entities.Academics
{
    /// <summary>
    /// Admin-configured monthly class-wise-cancellation allowance — how many individual
    /// sessions a teacher may self-cancel per calendar month (institute policy: "a certain
    /// number of session cancellations allowed based on total monthly sessions"), instead of
    /// taking whole-day leave for every one. Same shape as PayoutRate: a null TeacherProfileId
    /// is the centre-wide DEFAULT allowance (applies to any teacher with no row of their own);
    /// a row with a teacher overrides it just for them. Unlike PayoutRate this has no
    /// EffectiveFrom history — a quota only gates *future* self-cancellations, so there's
    /// nothing past to keep reproducible; changing it is a straight in-place update.
    /// </summary>
    [Index(nameof(TeacherProfileId), IsUnique = true)]
    public class LeaveAllowance : AuditEntity
    {
        public Guid? TeacherProfileId { get; set; }

        public TeacherProfile? TeacherProfile { get; set; }

        public int MonthlyAllowance { get; set; }
    }
}
