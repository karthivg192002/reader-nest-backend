using System.ComponentModel.DataAnnotations;
using iucs.readernest.domain.Entities.Academics;
using iucs.readernest.domain.Entities.Common;
using Microsoft.EntityFrameworkCore;

namespace iucs.readernest.domain.Entities.Quizzes
{
    /// <summary>
    /// Admin/teacher-authored live-quiz question, replacing what used to be one hardcoded
    /// bank shared by every class regardless of subject. Scoped to a Department always, and
    /// optionally narrowed to one Course — a null <see cref="CourseId"/> means the question is
    /// shared across every course in the department (this is also the only pool a Demo session
    /// can draw from, since a demo has a department but no course of its own).
    /// </summary>
    [Index(nameof(DepartmentId))]
    [Index(nameof(CourseId))]
    public class QuizQuestion : AuditEntity
    {
        public Guid DepartmentId { get; set; }

        public Department Department { get; set; } = null!;

        public Guid? CourseId { get; set; }

        public Course? Course { get; set; }

        [MaxLength(500)]
        public string Prompt { get; set; } = null!;

        /// <summary>Admin-controlled sequence within its department/course pool.</summary>
        public int DisplayOrder { get; set; }

        public ICollection<QuizQuestionOption> Options { get; set; } = new List<QuizQuestionOption>();
    }
}
