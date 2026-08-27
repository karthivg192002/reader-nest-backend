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
    // Role (alone) and Role+Status together are the two shapes actually filtered on —
    // the Users list, teacher/parent lookups, permission backfill, and reminder/digest
    // background services all query one or both. Leading column Role also serves any
    // Role-only filter without needing a second single-column index.
    [Index(nameof(Role), nameof(Status))]
    public class User : AuditEntity
    {
        [MaxLength(256)]
        public string Email { get; set; } = null!;

        /// <summary>Bcrypt hash of the account's numeric login PIN.</summary>
        [MaxLength(512)]
        public string PinHash { get; set; } = null!;

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

        /// <summary>
        /// Long-lived secret for the personal iCalendar feed URL (external calendar
        /// apps can't send a JWT). Created on first request; null until then.
        /// </summary>
        public Guid? CalendarFeedToken { get; set; }

        /// <summary>
        /// The member's permanent personal meeting room (Zoom-style): one stable link,
        /// startable any time. Minted on first request; null until then.
        /// </summary>
        [MaxLength(64)]
        public string? PersonalMeetingRoomId { get; set; }

        public DateTime? LastLoginAtUtc { get; set; }

        /// <summary>
        /// Consecutive failed login attempts since the last success. The login endpoint's
        /// rate limit (see Program.cs's "login" policy) partitions by IP only — with a 4-digit
        /// PIN's 10,000-value keyspace, an attacker who knows one target's email can spread
        /// attempts across a modest number of source IPs and brute-force that one account with
        /// no server-side signal it's under attack. This adds a per-account lockout on top.
        /// </summary>
        public int FailedLoginAttempts { get; set; }

        /// <summary>Set once FailedLoginAttempts crosses AuthService's threshold; null once cleared by a successful login.</summary>
        public DateTime? LockoutEndUtc { get; set; }

        /// <summary>
        /// Named role (preset) currently assigned; drives the post-login default
        /// route. Only meaningful for Sub Admin accounts today.
        /// </summary>
        public Guid? RoleDefinitionId { get; set; }

        public RoleDefinition? RoleDefinition { get; set; }

        public ParentProfile? ParentProfile { get; set; }

        public TeacherProfile? TeacherProfile { get; set; }
    }
}
