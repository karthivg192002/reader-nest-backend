using System.ComponentModel.DataAnnotations;
using iucs.readernest.domain.Enums;

namespace iucs.readernest.application.Dto.Users
{
    public class CreateUserRequest
    {
        [Required]
        [EmailAddress]
        [MaxLength(256)]
        public string Email { get; set; } = null!;

        [Required]
        [MaxLength(100)]
        public string FirstName { get; set; } = null!;

        /// <summary>Optional — single-word names are stored with an empty last name, not a duplicate.</summary>
        [MaxLength(100)]
        public string? LastName { get; set; }

        [MaxLength(20)]
        public string? Phone { get; set; }

        [Required]
        public UserRole Role { get; set; }

        [MaxLength(64)]
        public string? TimeZoneId { get; set; }

        /// <summary>Primary department for teachers; ignored for other roles.</summary>
        public Guid? DepartmentId { get; set; }

        /// <summary>Named role (preset) to assign immediately; only valid when Role is Sub Admin.</summary>
        public Guid? RoleDefinitionId { get; set; }

        /// <summary>
        /// Internal only (never bound from a request body): create the account without sending
        /// the welcome email. The portal admission flow makes the parent's account when it issues
        /// the payment link, but the parent only gets their login once the counsellor clicks Enroll.
        /// </summary>
        [System.Text.Json.Serialization.JsonIgnore]
        public bool SuppressWelcomeEmail { get; set; }
    }
}
