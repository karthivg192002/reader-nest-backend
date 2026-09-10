using iucs.readernest.domain.Enums;

namespace iucs.readernest.application.Dto.Sessions
{
    /// <summary>One row of the Class Session Logs screen — an event enriched with just enough
    /// session context (teacher/batch/type) to be readable without a second lookup.</summary>
    public class ClassSessionEventLogDto
    {
        public Guid Id { get; set; }

        public Guid ClassSessionId { get; set; }

        public ClassSessionEventType EventType { get; set; }

        public ParticipantType? ParticipantType { get; set; }

        public string? ParticipantName { get; set; }

        public DateTime OccurredAtUtc { get; set; }

        public bool IsExpected { get; set; }

        public string? Detail { get; set; }

        public SessionType SessionType { get; set; }

        public string TeacherName { get; set; } = null!;

        public string? BatchName { get; set; }

        public DateTime ScheduledStartAtUtc { get; set; }
    }

    /// <summary>Live/next status snapshot plus rollup counts for the dashboard header.</summary>
    public class ClassSessionLogDashboardDto
    {
        public List<ClassSessionDto> LiveClasses { get; set; } = [];

        public List<ClassSessionDto> NextClasses { get; set; } = [];

        public int UnexpectedEventsToday { get; set; }

        public int NoShowsThisWeek { get; set; }

        public Dictionary<string, int> StatusCounts { get; set; } = [];
    }
}
