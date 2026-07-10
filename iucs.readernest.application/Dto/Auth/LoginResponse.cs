using iucs.readernest.application.Dto.Users;

namespace iucs.readernest.application.Dto.Auth
{
    public class LoginResponse
    {
        public string AccessToken { get; set; } = null!;

        public DateTime ExpiresAtUtc { get; set; }

        public UserDto User { get; set; } = null!;

        /// <summary>"Module:Action" grants for Sub Admins; empty for other roles (Admin holds all implicitly).</summary>
        public IReadOnlyList<string> Permissions { get; set; } = [];
    }
}
