using System.Security.Claims;
using iucs.readernest.api.Auth;
using iucs.readernest.application.Dto.Admission;
using iucs.readernest.application.Services;
using iucs.readernest.domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace iucs.readernest.api.Controllers
{
    [ApiController]
    [Route("api/demo-bookings")]
    public class DemoBookingsController : ControllerBase
    {
        private readonly IDemoBookingService _demoBookingService;

        public DemoBookingsController(IDemoBookingService demoBookingService)
        {
            _demoBookingService = demoBookingService;
        }

        [HttpGet]
        [HasPermission(PermissionModule.Admission, PermissionAction.View)]
        public async Task<ActionResult<IReadOnlyList<DemoBookingDto>>> List(
            [FromQuery] ConversionStatus? status,
            CancellationToken cancellationToken)
        {
            return Ok(await _demoBookingService.ListAsync(status, cancellationToken));
        }

        /// <summary>Per-parent demo record: every demo each parent has taken, with auto-calculated fee totals.</summary>
        [HttpGet("parent-history")]
        [HasPermission(PermissionModule.Admission, PermissionAction.View)]
        public async Task<ActionResult<IReadOnlyList<ParentDemoHistoryDto>>> ParentHistory(
            [FromQuery] string? search,
            CancellationToken cancellationToken)
        {
            return Ok(await _demoBookingService.ListParentHistoryAsync(search, cancellationToken));
        }

        [HttpGet("{id:guid}")]
        [HasPermission(PermissionModule.Admission, PermissionAction.View)]
        public async Task<ActionResult<DemoBookingDto>> Get(Guid id, CancellationToken cancellationToken)
        {
            return Ok(await _demoBookingService.GetAsync(id, cancellationToken));
        }

        [HttpPost]
        [HasPermission(PermissionModule.Admission, PermissionAction.Create)]
        public async Task<ActionResult<DemoBookingDto>> Create(
            CreateDemoBookingRequest request,
            CancellationToken cancellationToken)
        {
            var booking = await _demoBookingService.CreateAsync(request, cancellationToken);
            return CreatedAtAction(nameof(Get), new { id = booking.Id }, booking);
        }

        [HttpPut("{id:guid}/conversion-status")]
        [HasPermission(PermissionModule.Admission, PermissionAction.Edit)]
        public async Task<ActionResult<DemoBookingDto>> UpdateConversionStatus(
            Guid id,
            UpdateConversionStatusRequest request,
            CancellationToken cancellationToken)
        {
            return Ok(await _demoBookingService.UpdateConversionStatusAsync(id, request, cancellationToken));
        }

        /// <summary>Mandatory post-demo feedback, submitted by the teacher who ran the demo.</summary>
        [HttpPost("{id:guid}/feedback")]
        [Authorize(Roles = nameof(UserRole.Teacher))]
        public async Task<ActionResult<DemoFeedbackDto>> SubmitFeedback(
            Guid id,
            SubmitDemoFeedbackRequest request,
            CancellationToken cancellationToken)
        {
            var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            return Ok(await _demoBookingService.SubmitFeedbackAsync(id, userId, request, cancellationToken));
        }

        /// <summary>Demo bookings assigned to the signed-in teacher.</summary>
        [HttpGet("mine")]
        [Authorize(Roles = nameof(UserRole.Teacher))]
        public async Task<ActionResult<IReadOnlyList<DemoBookingDto>>> Mine(CancellationToken cancellationToken)
        {
            var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            return Ok(await _demoBookingService.ListForTeacherUserAsync(userId, cancellationToken));
        }

        /// <summary>The signed-in teacher's own submitted feedback.</summary>
        [HttpGet("feedback/mine")]
        [Authorize(Roles = nameof(UserRole.Teacher))]
        public async Task<ActionResult<IReadOnlyList<DemoFeedbackDto>>> MyFeedback(CancellationToken cancellationToken)
        {
            var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            return Ok(await _demoBookingService.ListFeedbackForTeacherUserAsync(userId, cancellationToken));
        }

        /// <summary>Admission team review of all submitted demo feedback.</summary>
        [HttpGet("feedback")]
        [HasPermission(PermissionModule.Admission, PermissionAction.View)]
        public async Task<ActionResult<IReadOnlyList<DemoFeedbackDto>>> ListFeedback(CancellationToken cancellationToken)
        {
            return Ok(await _demoBookingService.ListFeedbackAsync(cancellationToken));
        }
    }
}
