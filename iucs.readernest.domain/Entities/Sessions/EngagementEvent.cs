using System.ComponentModel.DataAnnotations;
using iucs.readernest.domain.Entities.Common;
using iucs.readernest.domain.Entities.Users;
using iucs.readernest.domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace iucs.readernest.domain.Entities.Sessions
{
    /// <summary>
    /// One engagement signal captured during a live class — quiz attempts, activity
    /// clicks, whiteboard interactions and attention pings. Aggregated into the
    /// per-student engagement score and learning outcome indicators.
    /// </summary>
    [Index(nameof(ClassSessionId))]
    [Index(nameof(ChildId))]
    public class EngagementEvent : BaseEntity
    {
        public Guid ClassSessionId { get; set; }

        public ClassSession ClassSession { get; set; } = null!;

        /// <summary>Null for anonymous/demo participants; set when the roster child is known.</summary>
        public Guid? ChildId { get; set; }

        public Child? Child { get; set; }

        [MaxLength(200)]
        public string ParticipantName { get; set; } = null!;

        public EngagementEventType Type { get; set; }

        /// <summary>Signal payload: 1 for a correct quiz answer, 0 for wrong; 1 per click/stroke/ping.</summary>
        public int Value { get; set; } = 1;
    }
}
