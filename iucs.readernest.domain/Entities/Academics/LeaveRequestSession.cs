using iucs.readernest.domain.Entities.Common;
using iucs.readernest.domain.Entities.Sessions;
using Microsoft.EntityFrameworkCore;

namespace iucs.readernest.domain.Entities.Academics
{
    /// <summary>
    /// One specific class covered by a class-wise leave request (WBS "class-wise
    /// leave/cancellation" — a teacher with 10 sessions in a day who only wants to cancel
    /// 2 of them picks exactly those two, rather than the whole day). Deliberately a plain
    /// join, not a derived time-window: LeaveRequest.StartAtUtc/EndAtUtc for a class-wise
    /// request only bounds the earliest/latest of these sessions (kept for the existing
    /// overlap/duplicate-request check and the 6-hour cutoff), so approval must cancel
    /// exactly the sessions listed here — never "everything scheduled in that span," which
    /// would over-cancel any session between the picked ones that the teacher meant to keep.
    /// </summary>
    [Index(nameof(LeaveRequestId), nameof(ClassSessionId), IsUnique = true)]
    public class LeaveRequestSession : BaseEntity
    {
        public Guid LeaveRequestId { get; set; }

        public LeaveRequest LeaveRequest { get; set; } = null!;

        public Guid ClassSessionId { get; set; }

        public ClassSession ClassSession { get; set; } = null!;
    }
}
