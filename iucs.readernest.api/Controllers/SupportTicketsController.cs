using System.Security.Claims;
using iucs.readernest.api.Auth;
using iucs.readernest.application.Dto.Communication;
using iucs.readernest.application.Services;
using iucs.readernest.domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace iucs.readernest.api.Controllers
{
    /// <summary>
    /// Parent support tickets. Parents raise and follow up on their own tickets ("mine" routes);
    /// Relationship Managers and Admin work the whole queue under the SupportTickets module.
    /// </summary>
    [ApiController]
    [Route("api/support-tickets")]
    public class SupportTicketsController : ControllerBase
    {
        private readonly ISupportTicketService _tickets;

        public SupportTicketsController(ISupportTicketService tickets)
        {
            _tickets = tickets;
        }

        [HttpGet("mine")]
        [Authorize(Roles = nameof(UserRole.Parent))]
        public async Task<ActionResult<IReadOnlyList<SupportTicketSummaryDto>>> ListMine(CancellationToken cancellationToken)
        {
            return Ok(await _tickets.ListForParentAsync(UserId(), cancellationToken));
        }

        [HttpGet("mine/{id:guid}")]
        [Authorize(Roles = nameof(UserRole.Parent))]
        public async Task<ActionResult<SupportTicketDetailDto>> GetMine(Guid id, CancellationToken cancellationToken)
        {
            return Ok(await _tickets.GetForParentAsync(UserId(), id, cancellationToken));
        }

        [HttpPost("mine")]
        [Authorize(Roles = nameof(UserRole.Parent))]
        public async Task<ActionResult<SupportTicketDetailDto>> Create(
            CreateSupportTicketRequest request, CancellationToken cancellationToken)
        {
            var result = await _tickets.CreateAsync(UserId(), request, cancellationToken);
            return CreatedAtAction(nameof(GetMine), new { id = result.Id }, result);
        }

        [HttpPost("mine/{id:guid}/messages")]
        [Authorize(Roles = nameof(UserRole.Parent))]
        public async Task<ActionResult<SupportTicketDetailDto>> ReplyMine(
            Guid id, ReplySupportTicketRequest request, CancellationToken cancellationToken)
        {
            return Ok(await _tickets.ParentReplyAsync(UserId(), id, request, cancellationToken));
        }

        [HttpGet]
        [HasPermission(PermissionModule.SupportTickets, PermissionAction.View)]
        public async Task<ActionResult<IReadOnlyList<SupportTicketSummaryDto>>> List(
            [FromQuery] SupportTicketStatus? status, [FromQuery] string? search, CancellationToken cancellationToken)
        {
            return Ok(await _tickets.ListForStaffAsync(status, search, cancellationToken));
        }

        [HttpGet("counts")]
        [HasPermission(PermissionModule.SupportTickets, PermissionAction.View)]
        public async Task<ActionResult<SupportTicketCountsDto>> Counts(CancellationToken cancellationToken)
        {
            return Ok(await _tickets.GetCountsAsync(cancellationToken));
        }

        [HttpGet("{id:guid}")]
        [HasPermission(PermissionModule.SupportTickets, PermissionAction.View)]
        public async Task<ActionResult<SupportTicketDetailDto>> Get(Guid id, CancellationToken cancellationToken)
        {
            return Ok(await _tickets.GetForStaffAsync(id, cancellationToken));
        }

        [HttpPost("{id:guid}/messages")]
        [HasPermission(PermissionModule.SupportTickets, PermissionAction.Edit)]
        public async Task<ActionResult<SupportTicketDetailDto>> Reply(
            Guid id, ReplySupportTicketRequest request, CancellationToken cancellationToken)
        {
            return Ok(await _tickets.StaffReplyAsync(UserId(), id, request, cancellationToken));
        }

        [HttpPut("{id:guid}/status")]
        [HasPermission(PermissionModule.SupportTickets, PermissionAction.Edit)]
        public async Task<ActionResult<SupportTicketDetailDto>> UpdateStatus(
            Guid id, UpdateSupportTicketStatusRequest request, CancellationToken cancellationToken)
        {
            return Ok(await _tickets.UpdateStatusAsync(UserId(), id, request.Status, cancellationToken));
        }

        private Guid UserId() => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
    }
}
