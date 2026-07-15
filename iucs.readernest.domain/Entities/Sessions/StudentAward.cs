using System.ComponentModel.DataAnnotations;
using iucs.readernest.domain.Entities.Common;
using iucs.readernest.domain.Entities.Users;
using iucs.readernest.domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace iucs.readernest.domain.Entities.Sessions
{
    /// <summary>
    /// A persisted gamification award (star/badge/milestone) earned in a live class.
    /// The in-class leaderboard is ephemeral hub state; this is its durable record,
    /// powering cross-session leaderboards and progress views.
    /// </summary>
    [Index(nameof(ClassSessionId))]
    [Index(nameof(ParticipantName))]
    public class StudentAward : BaseEntity
    {
        public Guid? ClassSessionId { get; set; }

        public ClassSession? ClassSession { get; set; }

        /// <summary>Null for anonymous/demo participants; set when the roster child is known.</summary>
        public Guid? ChildId { get; set; }

        public Child? Child { get; set; }

        [MaxLength(200)]
        public string ParticipantName { get; set; } = null!;

        public AwardKind Kind { get; set; }

        /// <summary>Badge/milestone display name (e.g. "Rising Star — 3 stars").</summary>
        [MaxLength(100)]
        public string? Label { get; set; }

        public int Points { get; set; } = 1;
    }
}
