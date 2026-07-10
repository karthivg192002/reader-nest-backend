using System.ComponentModel.DataAnnotations;
using iucs.readernest.domain.Entities.Common;
using iucs.readernest.domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace iucs.readernest.domain.Entities.Users
{
    [Index(nameof(UserId), IsUnique = true)]
    public class TeacherProfile : BaseEntity
    {
        public Guid UserId { get; set; }

        public User User { get; set; } = null!;

        [MaxLength(1000)]
        public string? Bio { get; set; }

        [MaxLength(200)]
        public string? Specialization { get; set; }

        /// <summary>Primary department; null when the teacher works across departments.</summary>
        public Department? Department { get; set; }
    }
}
