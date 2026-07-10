using iucs.readernest.domain.Enums;
using Microsoft.AspNetCore.Authorization;

namespace iucs.readernest.api.Auth
{
    /// <summary>
    /// Gates an endpoint on a module/action grant. Admins pass implicitly;
    /// Sub Admins need the matching permission row; other roles are denied.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
    public class HasPermissionAttribute : AuthorizeAttribute
    {
        public const string PolicyPrefix = "perm:";

        public HasPermissionAttribute(PermissionModule module, PermissionAction action)
        {
            Policy = $"{PolicyPrefix}{module}:{action}";
        }
    }
}
