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

        public Department? Department { get; set; }

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

        public Department? Department { get; set; }

        public ConversionStatus ConversionStatus { get; set; }

        public string? FollowUpNotes { get; set; }

        public DateTime? ScheduledStartAtUtc { get; set; }

        public string? MeetingRoomId { get; set; }

        /// <summary>Teacher conducting (or who conducted) the demo, from the linked session.</summary>
        public Guid? TeacherProfileId { get; set; }

        public string? TeacherName { get; set; }

        /// <summary>Auto-calculated demo fee: ₹50 per demo, ₹100 once the lead is Enrolled.</summary>
        public decimal PayableAmount { get; set; }

        public IReadOnlyList<DemoParticipantDto> Participants { get; set; } = [];
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
