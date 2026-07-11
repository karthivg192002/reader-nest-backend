using System.ComponentModel.DataAnnotations;
using iucs.readernest.domain.Enums;

namespace iucs.readernest.application.Dto.Enrollment
{
    public class EnrollmentFormDto
    {
        public Guid Id { get; set; }

        public Guid ParentProfileId { get; set; }

        public string ParentName { get; set; } = null!;

        public string ParentEmail { get; set; } = null!;

        public Guid? ChildId { get; set; }

        /// <summary>Client-defined fields as a JSON document (see docs/ENROLLMENT_FORM_FIELDS.md).</summary>
        public string FormDataJson { get; set; } = "{}";

        public EnrollmentFormStatus Status { get; set; }

        public DateTime? SubmittedAtUtc { get; set; }

        public DateTime? ReviewedAtUtc { get; set; }
    }

    public class SubmitEnrollmentFormRequest
    {
        /// <summary>Answers keyed by field id; the schema is client-configurable so no fixed columns.</summary>
        [Required]
        public string FormDataJson { get; set; } = null!;
    }

    public class ReviewEnrollmentFormRequest
    {
        [Required]
        public bool Approve { get; set; }

        /// <summary>On approval a Child record is created from these values.</summary>
        [MaxLength(100)]
        public string? ChildFirstName { get; set; }

        [MaxLength(100)]
        public string? ChildLastName { get; set; }

        public DateOnly? ChildDateOfBirth { get; set; }
    }

    public class ChildDto
    {
        public Guid Id { get; set; }

        public Guid ParentProfileId { get; set; }

        public string FirstName { get; set; } = null!;

        public string LastName { get; set; } = null!;

        public DateOnly? DateOfBirth { get; set; }

        public string? AcademicLevel { get; set; }

        public bool IsActive { get; set; }
    }
}
