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

        /// <summary>
        /// Route the frontend should navigate to right after login — the user's
        /// assigned role's configured default route, or the portal home for
        /// their account type if none is set.
        /// </summary>
        public string DefaultRoute { get; set; } = null!;

        /// <summary>
        /// Every portal key this session may enter — the home portal plus any other
        /// portal holding a menu item this role has been explicitly granted View on via
        /// Menu Access. RequireAuth admits a route when its required portal is in this
        /// list, letting a cross-portal grant actually open the page, not just show a link.
        /// </summary>
        public IReadOnlyList<string> AccessiblePortals { get; set; } = [];
    }
}
