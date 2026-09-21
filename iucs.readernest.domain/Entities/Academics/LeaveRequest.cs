using System.ComponentModel.DataAnnotations;
using iucs.readernest.domain.Entities.Common;
using iucs.readernest.domain.Entities.Users;
using iucs.readernest.domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace iucs.readernest.domain.Entities.Academics
{
    /// <summary>
    /// Teacher leave application. The 6-hour-before-session restriction is enforced in the
    /// application layer at submission time; approval/rejection is an Admin workflow.
    /// </summary>
    [Index(nameof(TeacherProfileId), nameof(Status))]
    public class LeaveRequest : AuditEntity
    {
        public Guid TeacherProfileId { get; set; }

        public TeacherProfile TeacherProfile { get; set; } = null!;

        public DateTime StartAtUtc { get; set; }

        public DateTime EndAtUtc { get; set; }

        [MaxLength(1000)]
        public string Reason { get; set; } = null!;

        public LeaveStatus Status { get; set; } = LeaveStatus.Pending;

        public Guid? ReviewedBy { get; set; }

        public DateTime? ReviewedAtUtc { get; set; }

        [MaxLength(500)]
        public string? ReviewNote { get; set; }

        /// <summary>
        /// True for a class-wise leave (specific sessions picked from <see cref="Sessions"/>)
        /// rather than a whole-day/date-range leave. Approval logic branches on this: a
        /// class-wise request cancels exactly its linked sessions; a whole-window one cancels
        /// everything scheduled inside StartAtUtc/EndAtUtc, as before.
        /// </summary>
        public bool IsClassWise { get; set; }

        /// <summary>Only populated when IsClassWise — the exact sessions this request covers.</summary>
        public ICollection<LeaveRequestSession> Sessions { get; set; } = new List<LeaveRequestSession>();
    }
}
