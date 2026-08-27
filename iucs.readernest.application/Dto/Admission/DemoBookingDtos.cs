using System.ComponentModel.DataAnnotations;
using iucs.readernest.domain.Enums;

namespace iucs.readernest.application.Dto.Admission
{
    public class DemoParticipantDto
    {
        [Required]
        [MaxLength(200)]
        public string Name { get; set; } = null!;

        /// <summary>Required for adult invitees (confirmation email); children have none.</summary>
        [EmailAddress]
        [MaxLength(256)]
        public string? Email { get; set; }

        [MaxLength(20)]
        public string? Phone { get; set; }

        /// <summary>True marks an additional child attending the demo.</summary>
        public bool IsChild { get; set; }

        /// <summary>Join-based attendance capture: set once a matching email joins the demo's classroom hub.</summary>
        public bool HasJoined { get; set; }
    }

    public class CreateDemoBookingRequest
    {
        [Required]
        [MaxLength(200)]
        public string ParentName { get; set; } = null!;

        [Required]
        [EmailAddress]
        [MaxLength(256)]
        public string ParentEmail { get; set; } = null!;

        [MaxLength(20)]
        public string? ParentPhone { get; set; }

        [Required]
        [MaxLength(200)]
        public string ChildName { get; set; } = null!;

        [Range(1, 18)]
        public int? ChildAge { get; set; }

        public Guid? DepartmentId { get; set; }

        /// <summary>Omit to auto-assign the least-loaded available teacher (department-matched when set).</summary>
        public Guid? TeacherProfileId { get; set; }

        [Required]
        public DateTime ScheduledStartAtUtc { get; set; }

        [Required]
        public DateTime ScheduledEndAtUtc { get; set; }

        /// <summary>Additional invitees — demos are flexible for more than one parent to join.</summary>
        public List<DemoParticipantDto> Participants { get; set; } = [];
    }

    public class UpdateConversionStatusRequest
    {
        [Required]
        public ConversionStatus ConversionStatus { get; set; }

        [MaxLength(2000)]
        public string? FollowUpNotes { get; set; }
    }

    public class DemoBookingDto
    {
        public Guid Id { get; set; }

        public Guid? ClassSessionId { get; set; }

        public string ParentName { get; set; } = null!;

        public string ParentEmail { get; set; } = null!;

        public string? ParentPhone { get; set; }

        public string ChildName { get; set; } = null!;

        public int? ChildAge { get; set; }

        public Guid? DepartmentId { get; set; }

        public string? DepartmentName { get; set; }

        public ConversionStatus ConversionStatus { get; set; }

        public string? FollowUpNotes { get; set; }

        public DateTime? ScheduledStartAtUtc { get; set; }

        public DateTime? ScheduledEndAtUtc { get; set; }

        public string? MeetingRoomId { get; set; }

        /// <summary>Teacher conducting (or who conducted) the demo, from the linked session.</summary>
        public Guid? TeacherProfileId { get; set; }

        public string? TeacherName { get; set; }

        /// <summary>Auto-calculated demo fee: ₹50 per demo, ₹100 once the lead is Enrolled.</summary>
        public decimal PayableAmount { get; set; }

        /// <summary>Join-based attendance capture for the primary parent — set the first time a
        /// signed-in account matching <see cref="ParentEmail"/> joins this demo's classroom hub.</summary>
        public DateTime? ParentJoinedAtUtc { get; set; }

        public IReadOnlyList<DemoParticipantDto> Participants { get; set; } = [];
    }

    public class ReassignTeacherRequest
    {
        [Required]
        public Guid TeacherProfileId { get; set; }

        /// <summary>Optional free-text reason (e.g. "Original teacher called in sick") kept in the audit trail.</summary>
        [MaxLength(500)]
        public string? Reason { get; set; }
    }

    /// <summary>One active teacher's load around a booking's slot — powers the reassignment page's availability view.</summary>
    public class TeacherWorkloadDto
    {
        public Guid TeacherProfileId { get; set; }

        public string TeacherName { get; set; } = null!;

        public Guid? DepartmentId { get; set; }

        public string? DepartmentName { get; set; }

        /// <summary>True if this teacher already has an overlapping session at the booking's slot.</summary>
        public bool IsBusyAtSlot { get; set; }

        public int SessionsToday { get; set; }

        public int SessionsThisWeek { get; set; }
    }

    /// <summary>One manual teacher reassignment on a demo booking, for the page's audit-trail panel.</summary>
    public class DemoReassignmentHistoryDto
    {
        public Guid Id { get; set; }

        public DateTime AtUtc { get; set; }

        public string? ActorName { get; set; }

        public string? OldTeacherName { get; set; }

        public string NewTeacherName { get; set; } = null!;

        public string? Reason { get; set; }
    }

    /// <summary>Per-parent demo record: every demo this parent has ever taken, with totals.</summary>
    public class ParentDemoHistoryDto
    {
        public string ParentName { get; set; } = null!;

        public string ParentEmail { get; set; } = null!;

        public string? ParentPhone { get; set; }

        public int TotalDemos { get; set; }

        public int EnrolledCount { get; set; }

        public DateTime? LastDemoAtUtc { get; set; }

        /// <summary>Sum of the auto-calculated demo fees across this parent's demos.</summary>
        public decimal TotalPayable { get; set; }

        public IReadOnlyList<DemoBookingDto> Bookings { get; set; } = [];
    }
}
