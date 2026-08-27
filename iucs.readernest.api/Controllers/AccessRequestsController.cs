using System.Security.Claims;
using iucs.readernest.api.Auth;
using iucs.readernest.application.Dto.Users;
using iucs.readernest.application.Services;
using iucs.readernest.domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace iucs.readernest.api.Controllers
{
    /// <summary>
    /// A Relationship Manager's "request additional access" flow: submit, see your own
    /// history, or (Admin) list and review everyone's requests.
    /// </summary>
    [ApiController]
    [Route("api/access-requests")]
    public class AccessRequestsController : ControllerBase
    {
        private readonly IAccessRequestService _accessRequests;

        public AccessRequestsController(IAccessRequestService accessRequests)
        {
            _accessRequests = accessRequests;
        }

        /// <summary>A Sub Admin asks for modules outside their current grant.</summary>
        [HttpPost]
        [Authorize(Roles = nameof(UserRole.SubAdmin))]
        public async Task<ActionResult<AccessRequestDto>> Submit(SubmitAccessRequestRequest request, CancellationToken cancellationToken)
        {
            var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            return Ok(await _accessRequests.SubmitAsync(userId, request, cancellationToken));
        }

        [HttpGet("mine")]
        [Authorize(Roles = nameof(UserRole.SubAdmin))]
        public async Task<ActionResult<IReadOnlyList<AccessRequestDto>>> Mine(CancellationToken cancellationToken)
        {
            var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            return Ok(await _accessRequests.ListMineAsync(userId, cancellationToken));
        }

        [HttpGet]
        [HasPermission(PermissionModule.UserManagement, PermissionAction.View)]
        public async Task<ActionResult<IReadOnlyList<AccessRequestDto>>> List(
            [FromQuery] AccessRequestStatus? status,
            CancellationToken cancellationToken)
        {
            return Ok(await _accessRequests.ListAsync(status, cancellationToken));
        }

        /// <summary>Admin approval/rejection; the Relationship Manager is notified either way.
        /// Approving records the decision only — actually granting the modules is still done
        /// from Roles &amp; Permissions, same as any other permission change.</summary>
        [HttpPost("{id:guid}/review")]
        [HasPermission(PermissionModule.UserManagement, PermissionAction.Approve)]
        public async Task<ActionResult<AccessRequestDto>> Review(
            Guid id,
            ReviewAccessRequestRequest request,
            CancellationToken cancellationToken)
        {
            var reviewerId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            return Ok(await _accessRequests.ReviewAsync(id, reviewerId, request, cancellationToken));
        }
    }
}
