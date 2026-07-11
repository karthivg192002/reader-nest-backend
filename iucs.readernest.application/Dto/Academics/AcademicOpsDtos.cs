using System.ComponentModel.DataAnnotations;
using iucs.readernest.domain.Enums;

namespace iucs.readernest.application.Dto.Academics
{
    public class HolidayDto
    {
        public Guid Id { get; set; }

        public DateOnly Date { get; set; }

        public string Name { get; set; } = null!;

        public string? Description { get; set; }
    }

    public class SaveHolidayRequest
    {
        [Required]
        public DateOnly Date { get; set; }

        [Required]
        [MaxLength(150)]
        public string Name { get; set; } = null!;

        [MaxLength(500)]
        public string? Description { get; set; }
    }

    public class LeaveRequestDto
    {
        public Guid Id { get; set; }

        public Guid TeacherProfileId { get; set; }

        public string TeacherName { get; set; } = null!;

        public DateTime StartAtUtc { get; set; }

        public DateTime EndAtUtc { get; set; }

        public string Reason { get; set; } = null!;

        public LeaveStatus Status { get; set; }

        public string? ReviewNote { get; set; }

        public DateTime CreatedAtUtc { get; set; }

        /// <summary>Sessions inside the leave window, shown to the reviewing admin.</summary>
        public int AffectedSessionCount { get; set; }
    }

    public class SubmitLeaveRequest
    {
        [Required]
        public DateTime StartAtUtc { get; set; }

        [Required]
        public DateTime EndAtUtc { get; set; }

        [Required]
        [MaxLength(1000)]
        public string Reason { get; set; } = null!;
    }

    public class ReviewLeaveRequest
    {
        [Required]
        public bool Approve { get; set; }

        [MaxLength(500)]
        public string? ReviewNote { get; set; }
    }

    public class AttendanceEntryDto
    {
        public Guid? ChildId { get; set; }

        public Guid? TeacherProfileId { get; set; }

        [Required]
        public AttendanceStatus Status { get; set; }

        public DateTime? JoinedAtUtc { get; set; }

        public DateTime? LeftAtUtc { get; set; }
    }

    public class CaptureAttendanceRequest
    {
        [Required]
        [MinLength(1)]
        public List<AttendanceEntryDto> Entries { get; set; } = [];
    }

    public class SessionAttendanceDto
    {
        public Guid Id { get; set; }

        public Guid ClassSessionId { get; set; }

        public ParticipantType ParticipantType { get; set; }

        public Guid? ChildId { get; set; }

        public string? ChildName { get; set; }

        public Guid? TeacherProfileId { get; set; }

        public AttendanceStatus Status { get; set; }

        public DateTime? JoinedAtUtc { get; set; }

        public DateTime? LeftAtUtc { get; set; }
    }
}
