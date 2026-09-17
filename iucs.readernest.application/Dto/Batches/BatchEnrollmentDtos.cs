using System.ComponentModel.DataAnnotations;
using iucs.readernest.domain.Enums;

namespace iucs.readernest.application.Dto.Batches
{
    /// <summary>A child currently placed in a batch — the "Assign Students" roster (WBS p.17/25).</summary>
    public class BatchStudentDto
    {
        public Guid EnrollmentId { get; set; }

        public Guid ChildId { get; set; }

        public string ChildName { get; set; } = null!;

        public string? AcademicLevel { get; set; }

        public EnrollmentStatus Status { get; set; }

        public DateTime EnrolledAtUtc { get; set; }
    }

    /// <summary>An active, approved child not yet placed in this specific batch — candidates for the "Assign students" picker.</summary>
    public class UnassignedChildDto
    {
        public Guid ChildId { get; set; }

        public string ChildName { get; set; } = null!;

        public string ParentName { get; set; } = null!;

        /// <summary>Lets the "Assign students" picker tell apart two same-named children (e.g. real duplicates from migration, or one child enrolled separately per course) without an extra lookup.</summary>
        public string? ParentEmail { get; set; }

        public string? AcademicLevel { get; set; }

        /// <summary>Course name(s) this child is currently actively enrolled in elsewhere, comma-joined; null if not enrolled anywhere yet.</summary>
        public string? CurrentCourses { get; set; }
    }

    public class AssignStudentRequest
    {
        [Required]
        public Guid ChildId { get; set; }
    }
}
