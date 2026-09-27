using System.ComponentModel.DataAnnotations;
using iucs.readernest.domain.Enums;

namespace iucs.readernest.application.Dto.Users
{
    public class StaffLeaveRequestDto
    {
        public Guid Id { get; set; }

        public Guid UserId { get; set; }

        public string StaffName { get; set; } = null!;

        public string StaffEmail { get; set; } = null!;

        /// <summary>The staff member's role preset, e.g. "Parent Relationship Manager".</summary>
        public string? RoleName { get; set; }

        public DateOnly StartDate { get; set; }

        public DateOnly EndDate { get; set; }

        public int Days { get; set; }

        public string Reason { get; set; } = null!;

        public LeaveStatus Status { get; set; }

        public string? ReviewedByName { get; set; }

        public DateTime? ReviewedAtUtc { get; set; }

        public string? ReviewNote { get; set; }

        public DateTime CreatedAtUtc { get; set; }
    }

    public class SubmitStaffLeaveRequest
    {
        [Required]
        public DateOnly StartDate { get; set; }

        [Required]
        public DateOnly EndDate { get; set; }

        [Required, MaxLength(1000)]
        public string Reason { get; set; } = string.Empty;
    }

    public class ReviewStaffLeaveRequest
    {
        [Required]
        public bool Approve { get; set; }

        [MaxLength(500)]
        public string? Note { get; set; }
    }
}
