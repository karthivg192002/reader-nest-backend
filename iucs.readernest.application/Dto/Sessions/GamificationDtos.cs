using System.ComponentModel.DataAnnotations;
using iucs.readernest.domain.Enums;

namespace iucs.readernest.application.Dto.Sessions
{
    public class GrantAwardRequest
    {
        public Guid? SessionId { get; set; }

        [Required]
        [MaxLength(200)]
        public string ParticipantName { get; set; } = null!;

        public AwardKind Kind { get; set; } = AwardKind.Star;

        [MaxLength(100)]
        public string? Label { get; set; }

        [Range(1, 100)]
        public int Points { get; set; } = 1;
    }

    public class AwardDto
    {
        public Guid Id { get; set; }

        public Guid? SessionId { get; set; }

        public string ParticipantName { get; set; } = null!;

        public AwardKind Kind { get; set; }

        public string? Label { get; set; }

        public int Points { get; set; }

        public DateTime CreatedAtUtc { get; set; }
    }

    /// <summary>Aggregated leaderboard row: total stars plus earned badges/milestones.</summary>
    public class LeaderboardEntryDto
    {
        public string ParticipantName { get; set; } = null!;

        public int Stars { get; set; }

        public IReadOnlyList<string> Badges { get; set; } = [];
    }
}
