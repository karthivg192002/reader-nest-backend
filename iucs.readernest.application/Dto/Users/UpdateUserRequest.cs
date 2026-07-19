using System.ComponentModel.DataAnnotations;

namespace iucs.readernest.application.Dto.Users
{
    public class UpdateUserRequest
    {
        [Required]
        [MaxLength(100)]
        public string FirstName { get; set; } = null!;

        /// <summary>Optional — single-word names are stored with an empty last name, not a duplicate.</summary>
        [MaxLength(100)]
        public string? LastName { get; set; }

        [MaxLength(20)]
        public string? Phone { get; set; }

        [MaxLength(64)]
        public string? TimeZoneId { get; set; }
    }
}
