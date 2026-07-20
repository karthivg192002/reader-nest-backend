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

        public string? AcademicLevel { get; set; }
    }

    public class AssignStudentRequest
    {
        [Required]
        public Guid ChildId { get; set; }
    }
}
