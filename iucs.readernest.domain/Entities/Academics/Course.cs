using System.ComponentModel.DataAnnotations;
using iucs.readernest.domain.Entities.Common;
using iucs.readernest.domain.Enums;

namespace iucs.readernest.domain.Entities.Academics
{
    public class Course : AuditEntity
    {
        public Guid CourseCategoryId { get; set; }

        public CourseCategory CourseCategory { get; set; } = null!;

        [MaxLength(200)]
        public string Name { get; set; } = null!;

        [MaxLength(1000)]
        public string? Description { get; set; }

        public CourseType Type { get; set; }

        /// <summary>Configured class length; business rule allows 30, 45 or 60 minutes.</summary>
        public int DurationMinutes { get; set; }

        public decimal Price { get; set; }

        /// <summary>Total sessions in the course; completion of all sessions moves the batch to Dormant.</summary>
        public int TotalSessions { get; set; }

        /// <summary>Determines which payment gateway account invoices are routed to.</summary>
        public Department Department { get; set; }

        public bool IsActive { get; set; } = true;
    }
}
