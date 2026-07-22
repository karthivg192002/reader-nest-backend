using System.ComponentModel.DataAnnotations;
using iucs.readernest.domain.Enums;

namespace iucs.readernest.application.Dto.Users
{
    public class ChangeUserRoleRequest
    {
        [Required]
        public UserRole Role { get; set; }
    }
}
