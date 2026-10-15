using System.ComponentModel.DataAnnotations;

namespace iucs.readernest.application.Dto.Auth
{
    public class LoginRequest
    {
        /// <summary>The account's email, or (for a parent) their mobile number (see ParentLogin).</summary>
        [Required]
        [MaxLength(256)]
        public string Email { get; set; } = null!;

        [Required]
        [RegularExpression(@"^\d{4}$", ErrorMessage = "PIN must be exactly 4 digits.")]
        public string Pin { get; set; } = null!;

        /// <summary>"Remember me" on the sign-in form: issue a long-lived login instead of an 8-hour one.</summary>
        public bool RememberMe { get; set; }
    }
}
