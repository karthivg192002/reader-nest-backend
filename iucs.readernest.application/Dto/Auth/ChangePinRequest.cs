using System.ComponentModel.DataAnnotations;

namespace iucs.readernest.application.Dto.Auth
{
    /// <summary>A signed-in user changing their own PIN (Admin included): the current PIN proves it's them.</summary>
    public class ChangePinRequest
    {
        [Required]
        [RegularExpression(@"^\d{4}$", ErrorMessage = "PIN must be exactly 4 digits.")]
        public string CurrentPin { get; set; } = null!;

        [Required]
        [RegularExpression(@"^\d{4}$", ErrorMessage = "PIN must be exactly 4 digits.")]
        public string NewPin { get; set; } = null!;
    }
}
