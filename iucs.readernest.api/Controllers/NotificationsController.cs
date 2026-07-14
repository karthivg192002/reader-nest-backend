using System.Security.Claims;
using iucs.readernest.application.Dto.Communication;
using iucs.readernest.application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace iucs.readernest.api.Controllers
{
    /// <summary>The signed-in user's own notification feed, powering the top-bar bell.</summary>
    [ApiController]
    [Route("api/notifications")]
    [Authorize]
    public class NotificationsController : ControllerBase
    {
        private readonly INotificationService _notifications;

        public NotificationsController(INotificationService notifications)
        {
            _notifications = notifications;
        }

        [HttpGet("mine")]
        public async Task<ActionResult<NotificationFeedDto>> Mine(
            [FromQuery] int take = 30,
            CancellationToken cancellationToken = default)
        {
            return Ok(await _notifications.GetFeedForUserAsync(UserId(), take, cancellationToken));
        }

        [HttpPost("{id:guid}/read")]
        public async Task<IActionResult> MarkRead(Guid id, CancellationToken cancellationToken)
        {
            await _notifications.MarkReadAsync(UserId(), id, cancellationToken);
            return NoContent();
        }

        [HttpPost("read-all")]
        public async Task<IActionResult> MarkAllRead(CancellationToken cancellationToken)
        {
            await _notifications.MarkAllReadAsync(UserId(), cancellationToken);
            return NoContent();
        }

        private Guid UserId() => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
    }
}
