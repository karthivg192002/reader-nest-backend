using iucs.readernest.application.Services;
using iucs.readernest.domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;

namespace iucs.readernest.api.Auth
{
    public class PermissionRequirement : IAuthorizationRequirement
    {
        public PermissionRequirement(string permission)
        {
            Permission = permission;
        }

        /// <summary>"Module:Action" string matching the claims issued at login.</summary>
        public string Permission { get; }
    }

    public class PermissionAuthorizationHandler : AuthorizationHandler<PermissionRequirement>
    {
        private readonly IServiceScopeFactory _scopeFactory;

        public PermissionAuthorizationHandler(IServiceScopeFactory scopeFactory)
        {
            _scopeFactory = scopeFactory;
        }

        protected override async Task HandleRequirementAsync(
            AuthorizationHandlerContext context,
            PermissionRequirement requirement)
        {
            // A module an Admin has switched off refuses every action on it, Admin included.
            var module = requirement.Permission.Split(':')[0];
            using (var scope = _scopeFactory.CreateScope())
            {
                var modules = scope.ServiceProvider.GetRequiredService<IPermissionModuleService>();
                var disabled = await modules.GetDisabledKeysAsync();
                if (disabled.Contains(module))
                {
                    context.Fail();
                    return;
                }
            }

            if (context.User.IsInRole(nameof(UserRole.Admin)))
            {
                context.Succeed(requirement);
            }
            else if (context.User.HasClaim(JwtTokenService.PermissionClaimType, requirement.Permission))
            {
                context.Succeed(requirement);
            }
        }
    }

    /// <summary>
    /// Materialises "perm:Module:Action" policies on demand so each module/action
    /// pair doesn't need manual registration.
    /// </summary>
    public class PermissionPolicyProvider : DefaultAuthorizationPolicyProvider
    {
        public PermissionPolicyProvider(IOptions<AuthorizationOptions> options)
            : base(options)
        {
        }

        public override async Task<AuthorizationPolicy?> GetPolicyAsync(string policyName)
        {
            if (policyName.StartsWith(HasPermissionAttribute.PolicyPrefix, StringComparison.Ordinal))
            {
                var permission = policyName[HasPermissionAttribute.PolicyPrefix.Length..];
                return new AuthorizationPolicyBuilder()
                    .RequireAuthenticatedUser()
                    .AddRequirements(new PermissionRequirement(permission))
                    .Build();
            }

            return await base.GetPolicyAsync(policyName);
        }
    }
}
