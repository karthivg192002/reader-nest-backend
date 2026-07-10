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
    }
}
