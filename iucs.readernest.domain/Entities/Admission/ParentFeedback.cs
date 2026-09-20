using System.ComponentModel.DataAnnotations;
using iucs.readernest.domain.Entities.Academics;
using iucs.readernest.domain.Entities.Common;
using iucs.readernest.domain.Entities.Users;
using iucs.readernest.domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace iucs.readernest.domain.Entities.Admission
{
    /// <summary>
    /// A parent's one-question rating (1-5 stars plus an optional comment) of how we did —
    /// once after the demo class and once when their child's batch completes. Distinct from
    /// <see cref="DemoFeedback"/>, which is the TEACHER's mandatory assessment of the child.
    /// Read by Admin and the Admission team. At most one per demo booking, and one per
    /// child+batch completion (NULLs are distinct in Postgres, so each unique index only
    /// constrains rows of its own kind).
    /// </summary>
    [Index(nameof(DemoBookingId), IsUnique = true)]
    [Index(nameof(BatchId), nameof(ChildId), IsUnique = true)]
    [Index(nameof(Kind), nameof(SubmittedAtUtc))]
    public class ParentFeedback : BaseEntity
    {
        public ParentFeedbackKind Kind { get; set; }

        public Guid ParentUserId { get; set; }

        public User ParentUser { get; set; } = null!;

        /// <summary>1 (poor) to 5 (excellent).</summary>
        public int Rating { get; set; }

        [MaxLength(1000)]
        public string? Comment { get; set; }

        /// <summary>Set for <see cref="ParentFeedbackKind.Demo"/>.</summary>
        public Guid? DemoBookingId { get; set; }

        public DemoBooking? DemoBooking { get; set; }

        /// <summary>Set for <see cref="ParentFeedbackKind.CourseCompletion"/>.</summary>
        public Guid? BatchId { get; set; }

        public Batch? Batch { get; set; }

        public Guid? ChildId { get; set; }

        public Child? Child { get; set; }

        public DateTime SubmittedAtUtc { get; set; }
    }
}
