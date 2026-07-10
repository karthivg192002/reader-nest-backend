using System.ComponentModel.DataAnnotations;

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
