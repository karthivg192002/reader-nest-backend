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

        /// <summary>Sessions inside the leave window, shown to the reviewing admin. For a
        /// class-wise request this always equals Sessions.Count.</summary>
        public int AffectedSessionCount { get; set; }

        /// <summary>True when this leave targets specific sessions (<see cref="Sessions"/>)
        /// instead of a whole day/date range.</summary>
        public bool IsClassWise { get; set; }

        /// <summary>Only populated when IsClassWise.</summary>
        public IReadOnlyList<LeaveSessionSummaryDto> Sessions { get; set; } = Array.Empty<LeaveSessionSummaryDto>();
    }

    /// <summary>One class covered by a class-wise leave request — enough for a teacher to
    /// pick it out of a list, and for an admin reviewing it to know exactly what's affected.</summary>
    public class LeaveSessionSummaryDto
    {
        public Guid SessionId { get; set; }

        public DateTime ScheduledStartAtUtc { get; set; }

        public DateTime ScheduledEndAtUtc { get; set; }

        public string? BatchName { get; set; }
    }

    public class SubmitLeaveRequest
    {
        /// <summary>Whole day/date-range leave (existing behaviour). Required unless
        /// SessionIds is given instead — the service rejects a request that supplies
        /// neither or both.</summary>
        public DateTime? StartAtUtc { get; set; }

        public DateTime? EndAtUtc { get; set; }

        /// <summary>Class-wise leave: exactly which of the teacher's own scheduled sessions
        /// to cancel, instead of every session in a time window. When this is within the
        /// teacher's remaining monthly cancellation allowance, it's auto-approved on
        /// submission — see AcademicOpsService.SubmitLeaveAsync.</summary>
        public List<Guid>? SessionIds { get; set; }

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

    /// <summary>Admin-configured monthly class-wise-cancellation allowance — same
    /// default-plus-per-teacher-override shape as a PayoutRate card.</summary>
    public class LeaveAllowanceDto
    {
        public Guid Id { get; set; }

        /// <summary>Null identifies the centre-wide default allowance.</summary>
        public Guid? TeacherProfileId { get; set; }

        public string TeacherName { get; set; } = null!;

        public int MonthlyAllowance { get; set; }
    }

    public class SaveLeaveAllowanceRequest
    {
        /// <summary>Omit to save the centre-wide default allowance.</summary>
        public Guid? TeacherProfileId { get; set; }

        [Range(0, 100)]
        public int MonthlyAllowance { get; set; }
    }

    /// <summary>A teacher's own view of their class-wise-cancellation quota for the current
    /// calendar month — surfaced on the leave-application screen so they know how many
    /// self-serve cancellations they have left before falling back to a reviewed request.</summary>
    public class LeaveAllowanceStatusDto
    {
        public int MonthlyAllowance { get; set; }

        public int UsedThisMonth { get; set; }

        public int Remaining { get; set; }
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
