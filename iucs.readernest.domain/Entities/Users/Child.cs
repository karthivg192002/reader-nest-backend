using System.ComponentModel.DataAnnotations;
using iucs.readernest.domain.Entities.Common;
using iucs.readernest.domain.Enums;

namespace iucs.readernest.domain.Entities.Users
{
    /// <summary>
    /// Student record under a parent account. Children have no login of their own.
    /// </summary>
    public class Child : AuditEntity
    {
        public Guid ParentProfileId { get; set; }

        public ParentProfile ParentProfile { get; set; } = null!;

        [MaxLength(100)]
        public string FirstName { get; set; } = null!;

        [MaxLength(100)]
        public string LastName { get; set; } = null!;

        public DateOnly? DateOfBirth { get; set; }

        public Gender? Gender { get; set; }

        /// <summary>Current academic level, seeded from demo feedback and updated over time.</summary>
        [MaxLength(100)]
        public string? AcademicLevel { get; set; }

        public bool IsActive { get; set; } = true;
    }
}
