using iucs.readernest.domain.Entities.Common;
using Microsoft.EntityFrameworkCore;

namespace iucs.readernest.domain.Entities.Academics
{
    /// <summary>
    /// Frozen month-end snapshot of every class cancelled because of teacher leave, generated
    /// automatically at 23:59 IST on the last day of the month. Teacher pay is deducted from
    /// these counts, so the figures that were agreed at month end must not drift when a class is
    /// later edited -- the live data stays queryable, this is the payroll record.
    /// </summary>
    [Index(nameof(Year), nameof(Month), IsUnique = true)]
    public class LeaveCancellationReport : BaseEntity
    {
        public int Year { get; set; }

        public int Month { get; set; }

        public DateTime GeneratedAtUtc { get; set; }

        /// <summary>The staff member who regenerated it by hand; null when the scheduled job did.</summary>
        public Guid? GeneratedBy { get; set; }

        /// <summary>The serialized report body (teachers, dates, batches, totals).</summary>
        public string PayloadJson { get; set; } = null!;
    }
}
