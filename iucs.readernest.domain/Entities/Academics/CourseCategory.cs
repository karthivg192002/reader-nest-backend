using System.ComponentModel.DataAnnotations;
using iucs.readernest.domain.Entities.Common;
using Microsoft.EntityFrameworkCore;

namespace iucs.readernest.domain.Entities.Academics
{
    [Index(nameof(DepartmentId), nameof(Name), IsUnique = true)]
    public class CourseCategory : AuditEntity
    {
        [MaxLength(150)]
        public string Name { get; set; } = null!;

        [MaxLength(500)]
        public string? Description { get; set; }

        public Guid DepartmentId { get; set; }

        public Department Department { get; set; } = null!;
    }
}
