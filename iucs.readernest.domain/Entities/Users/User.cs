using System.ComponentModel.DataAnnotations;
using iucs.readernest.domain.Entities.Common;
using iucs.readernest.domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace iucs.readernest.domain.Entities.Users
{
    /// <summary>
    /// Login identity for every role. Parents and teachers carry an additional
    /// 1:1 profile; students never log in directly (they join via the parent account).
    /// </summary>
    [Index(nameof(Email), IsUnique = true)]
    public class User : AuditEntity
    {
        [MaxLength(256)]
        public string Email { get; set; } = null!;

        [MaxLength(512)]
        public string PasswordHash { get; set; } = null!;

        [MaxLength(100)]
        public string FirstName { get; set; } = null!;

        [MaxLength(100)]
        public string LastName { get; set; } = null!;

        [MaxLength(20)]
        public string? Phone { get; set; }

        public UserRole Role { get; set; }

        public UserStatus Status { get; set; } = UserStatus.Active;

        /// <summary>IANA time zone id; scheduling renders session times in this zone.</summary>
        [MaxLength(64)]
        public string TimeZoneId { get; set; } = "Asia/Kolkata";

        public DateTime? LastLoginAtUtc { get; set; }

        public ParentProfile? ParentProfile { get; set; }

        public TeacherProfile? TeacherProfile { get; set; }
    }
}
