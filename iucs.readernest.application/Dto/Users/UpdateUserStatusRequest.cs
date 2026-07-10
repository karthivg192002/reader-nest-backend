using System.ComponentModel.DataAnnotations;
using iucs.readernest.domain.Enums;

namespace iucs.readernest.application.Dto.Users
{
    public class UpdateUserStatusRequest
    {
        [Required]
        public UserStatus Status { get; set; }
    }
}
