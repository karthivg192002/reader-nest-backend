using iucs.readernest.domain.Enums;

namespace iucs.readernest.application.Dto.Users
{
    public class UserDto
    {
        public Guid Id { get; set; }

        public string Email { get; set; } = null!;

        public string FirstName { get; set; } = null!;

        public string LastName { get; set; } = null!;

        public string FullName => $"{FirstName} {LastName}";

        public string? Phone { get; set; }

        public UserRole Role { get; set; }

        public UserStatus Status { get; set; }

        public string TimeZoneId { get; set; } = null!;

        public Department? Department { get; set; }

        /// <summary>Assigned role (preset) id; only meaningful for Sub Admin accounts.</summary>
        public Guid? RoleDefinitionId { get; set; }

        public DateTime CreatedAtUtc { get; set; }

        public DateTime? LastLoginAtUtc { get; set; }
    }
}
