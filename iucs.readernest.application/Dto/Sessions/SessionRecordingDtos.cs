using System.ComponentModel.DataAnnotations;
using iucs.readernest.domain.Enums;

namespace iucs.readernest.application.Dto.Sessions
{
    public class SessionRecordingDto
    {
        public Guid Id { get; set; }

        public Guid ClassSessionId { get; set; }

        public string StorageUrl { get; set; } = null!;

        public int? DurationSeconds { get; set; }

        public DateTime? ExpiresAtUtc { get; set; }

        public DateTime CreatedAtUtc { get; set; }
    }

    /// <summary>Registers a finished Jitsi/Jibri recording that landed in cloud storage.</summary>
    public class RegisterRecordingRequest
    {
        [Required]
        [MaxLength(1000)]
        public string StorageUrl { get; set; } = null!;

        public int? DurationSeconds { get; set; }
    }

    public class EngagementEntryDto
    {
        public Guid? ChildId { get; set; }

        [Required]
        [MaxLength(200)]
        public string ParticipantName { get; set; } = null!;

        [Required]
        public EngagementEventType Type { get; set; }

        public int Value { get; set; } = 1;
    }

    public class RecordEngagementRequest
    {
        [Required]
        [MinLength(1)]
        public List<EngagementEntryDto> Events { get; set; } = [];
    }

    /// <summary>Per-participant engagement score and learning outcome indicators for a session.</summary>
    public class EngagementSummaryDto
    {
        public string ParticipantName { get; set; } = null!;

        public Guid? ChildId { get; set; }

        public int QuizAttempts { get; set; }

        public int QuizCorrect { get; set; }

        public int ActivityInteractions { get; set; }

        public int WhiteboardInteractions { get; set; }

        public int AttentionPings { get; set; }

        /// <summary>Weighted 0-100 score across participation, accuracy and attention.</summary>
        public int EngagementScore { get; set; }

        /// <summary>on-track | needs-encouragement | needs-attention</summary>
        public string LearningOutcome { get; set; } = "on-track";
    }

    public class CompleteSessionRequest
    {
        /// <summary>Optional class summary shown in session history.</summary>
        [MaxLength(2000)]
        public string? Summary { get; set; }
    }

    public enum NoShowParty
    {
        Teacher,
        Student,
    }

    public class MarkNoShowRequest
    {
        [Required]
        public NoShowParty Party { get; set; }

        [MaxLength(500)]
        public string? Note { get; set; }
    }
}
