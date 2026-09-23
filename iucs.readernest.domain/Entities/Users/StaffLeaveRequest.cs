using System.ComponentModel.DataAnnotations;
using iucs.readernest.domain.Entities.Common;
using iucs.readernest.domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace iucs.readernest.domain.Entities.Users
{
    /// <summary>
    /// A leave application from an admin-team member (Relationship Manager, Coordinator,
    /// Management, Admission, ...) — the staff counterpart of the teacher's LeaveRequest, which
    /// is tied to a TeacherProfile and its classes. Whole days, no minimum notice: staff can apply
    /// whenever they need to. Reviewed by Admin / Founder (UserManagement:Approve).
    /// </summary>
    [Index(nameof(UserId), nameof(StartDate))]
    [Index(nameof(Status), nameof(StartDate))]
    public class StaffLeaveRequest : AuditEntity
    {
        public Guid UserId { get; set; }

        public User User { get; set; } = null!;

        public DateOnly StartDate { get; set; }

        /// <summary>Inclusive.</summary>
        public DateOnly EndDate { get; set; }

        [MaxLength(1000)]
        public string Reason { get; set; } = string.Empty;

        public LeaveStatus Status { get; set; } = LeaveStatus.Pending;

        public Guid? ReviewedByUserId { get; set; }

        public DateTime? ReviewedAtUtc { get; set; }

        [MaxLength(500)]
        public string? ReviewNote { get; set; }
    }
}
