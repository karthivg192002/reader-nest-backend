using iucs.readernest.domain.Enums;

namespace iucs.readernest.application.Dto.Sessions
{
    public class ClassSessionDto
    {
        public Guid Id { get; set; }

        public Guid? BatchId { get; set; }

        public string? BatchName { get; set; }

        public Guid TeacherProfileId { get; set; }

        public string TeacherName { get; set; } = null!;

        public SessionType Type { get; set; }

        public SessionStatus Status { get; set; }

        public DateTime ScheduledStartAtUtc { get; set; }

        public DateTime ScheduledEndAtUtc { get; set; }

        public string? MeetingRoomId { get; set; }

        public Guid? RescheduledFromSessionId { get; set; }

        public string? CancellationReason { get; set; }

        /// <summary>Teacher's class summary, captured on completion for session history.</summary>
        public string? Summary { get; set; }

        /// <summary>
        /// Which of the calling parent's children this session belongs to — populated only by
        /// the parent-portal schedule endpoint (via the child's active batch enrollment), so a
        /// parent with multiple children can filter the shared list by the selected child. Empty
        /// for every other caller (Admin/Teacher session lists) and for demo sessions, which
        /// aren't tied to a specific enrolled child yet.
        /// </summary>
        public List<Guid> ChildIds { get; set; } = [];

        /// <summary>True when this session has at least one recording that hasn't expired yet.</summary>
        public bool HasRecording { get; set; }

        /// <summary>When the (most recent, still-active) recording's viewing window ends — null if there's none, or it never expires.</summary>
        public DateTime? RecordingExpiresAtUtc { get; set; }

        /// <summary>
        /// The DemoBooking's own free-text child name, for a Demo-type session only — DemoBooking
        /// has no Child FK (the demo happens before a Child record may even exist), so ChildIds
        /// above is always empty for these. Lets the parent portal at least label whose demo this
        /// is instead of a bare "Demo class" when a family has more than one child.
        /// </summary>
        public string? DemoChildName { get; set; }
    }
}
