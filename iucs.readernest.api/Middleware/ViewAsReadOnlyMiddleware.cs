using iucs.readernest.application.Common;
using Microsoft.AspNetCore.Mvc;

namespace iucs.readernest.api.Middleware
{
    /// <summary>
    /// Makes a "view as parent" session (see <see cref="ViewAsParent"/>) strictly look-only.
    /// A staff member viewing a parent's portal can load every screen, but anything that
    /// would change, pay for or join something is refused before it reaches a controller:
    /// <list type="bullet">
    ///   <item>every non-read request (POST/PUT/PATCH/DELETE), except closing the session itself;</item>
    ///   <item>live-class joins, which are GETs but would put the staff member into a real class as the parent;</item>
    ///   <item>the real-time hubs (classroom, monitoring) altogether.</item>
    /// </list>
    /// Runs after authentication, so only requests carrying a view-as token are affected.
    /// </summary>
    public class ViewAsReadOnlyMiddleware
    {
        private const string EndSessionPath = "/api/auth/view-as/end";

        private static readonly string[] BlockedPathEndings = ["/join", "/jitsi-join", "/guest-join", "/join-link", "/observer-join"];

        private readonly RequestDelegate _next;

        public ViewAsReadOnlyMiddleware(RequestDelegate next)
        {
            _next = next;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            if (context.User.HasClaim(c => c.Type == ViewAsParent.ActorClaimType) && IsBlocked(context.Request))
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                await context.Response.WriteAsJsonAsync(new ProblemDetails
                {
                    Status = StatusCodes.Status403Forbidden,
                    Title = "Read-only view",
                    Detail = "You're viewing this parent's account in read-only mode, so nothing can be changed, paid or joined from here.",
                }, (System.Text.Json.JsonSerializerOptions?)null, "application/problem+json");
                return;
            }

            await _next(context);
        }

        private static bool IsBlocked(HttpRequest request)
        {
            var path = request.Path.Value?.TrimEnd('/') ?? string.Empty;

            if (request.Path.StartsWithSegments("/hubs"))
            {
                return true;
            }

            if (BlockedPathEndings.Any(ending => path.EndsWith(ending, StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }

            var isRead = HttpMethods.IsGet(request.Method) || HttpMethods.IsHead(request.Method) || HttpMethods.IsOptions(request.Method);
            return !isRead && !path.Equals(EndSessionPath, StringComparison.OrdinalIgnoreCase);
        }
    }
}
