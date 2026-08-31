using System.ComponentModel.DataAnnotations;
using iucs.readernest.domain.Entities.Academics;
using iucs.readernest.domain.Entities.Common;
using Microsoft.EntityFrameworkCore;

namespace iucs.readernest.domain.Entities.Activities
{
    public enum WhiteboardActivityMode
    {
        DragDrop,
        TagMatch,
        Hotspot,
    }

    /// <summary>
    /// Admin/teacher-authored whiteboard mini-game, replacing what used to be one hardcoded
    /// fruit/letter matching set (plus a fixed "sh" sound hotspot game) every live class shared
    /// regardless of subject or grade. Scoped to a Department always, and optionally narrowed
    /// to one Course — a null <see cref="CourseId"/> means it's shared across every course in
    /// the department (and is the only pool a Demo session can draw from), same convention as
    /// <see cref="Quizzes.QuizQuestion"/>.
    /// </summary>
    [Index(nameof(DepartmentId))]
    [Index(nameof(CourseId))]
    public class WhiteboardActivityTemplate : AuditEntity
    {
        public Guid DepartmentId { get; set; }

        public Department Department { get; set; } = null!;

        public Guid? CourseId { get; set; }

        public Course? Course { get; set; }

        public WhiteboardActivityMode Mode { get; set; }

        [MaxLength(500)]
        public string Prompt { get; set; } = null!;

        /// <summary>Admin-controlled sequence within its department/course pool.</summary>
        public int DisplayOrder { get; set; }

        public ICollection<WhiteboardActivityItem> Items { get; set; } = new List<WhiteboardActivityItem>();
    }
}
