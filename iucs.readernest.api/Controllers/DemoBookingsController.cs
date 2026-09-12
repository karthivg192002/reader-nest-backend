using System.Security.Claims;
using iucs.readernest.api.Auth;
using iucs.readernest.application.Dto.Admission;
using iucs.readernest.application.Services;
using iucs.readernest.domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

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

        /// <summary>Manually override the teacher assigned to a demo booking.</summary>
        [HttpPut("{id:guid}/teacher")]
        [HasPermission(PermissionModule.Admission, PermissionAction.Edit)]
        public async Task<ActionResult<DemoBookingDto>> ReassignTeacher(
            Guid id,
            ReassignTeacherRequest request,
            CancellationToken cancellationToken)
        {
            return Ok(await _demoBookingService.ReassignTeacherAsync(id, request, cancellationToken));
        }

        /// <summary>Manually re-send the demo's join link to the parent, invitees and teacher.</summary>
        [HttpPost("{id:guid}/resend-link")]
        [HasPermission(PermissionModule.Admission, PermissionAction.Edit)]
        public async Task<ActionResult<DemoBookingDto>> ResendLink(Guid id, CancellationToken cancellationToken)
        {
            return Ok(await _demoBookingService.ResendLinkAsync(id, cancellationToken));
        }

        /// <summary>
        /// The parent's join link for this demo -- a short, stable URL (no JWT dangling off the
        /// end that reads as broken/suspicious pasted into WhatsApp or email) staff can copy and
        /// share manually. Points at this controller's own public <see cref="Join"/> redirect,
        /// which re-resolves the room/domain/token fresh on every click and never expires --
        /// unlike the old short-link wrapper this replaced, copying it now versus a parent
        /// opening it next month behave identically instead of the latter silently 404-ing.
        /// </summary>
        [HttpGet("{id:guid}/join-link")]
        [HasPermission(PermissionModule.Admission, PermissionAction.View)]
        public async Task<ActionResult<object>> GetJoinLink(Guid id, CancellationToken cancellationToken)
        {
            var joinUrl = await _demoBookingService.GetJoinLinkAsync(id, cancellationToken);
            return Ok(new { joinUrl });
        }

        /// <summary>
        /// The link a parent/invitee actually clicks -- from the confirmation email, a resend, or
        /// staff's "Copy Link". Deliberately public and unauthenticated (a demo lead has no
        /// account to sign in with) and deliberately NOT a static URL: it resolves the current
        /// Jitsi domain and mints a fresh signed token on every single hit, so it can never go
        /// stale the way a URL with those baked in at send time could -- reported live as a
        /// parent's join link 404-ing while the teacher, who always re-resolves fresh through
        /// the authenticated app, kept joining fine. Never expires -- see
        /// ResolveLiveJoinUrlAsync's own remarks. <paramref name="p"/> selects one of the
        /// booking's extra invitees; omitted, this is the primary parent's own link.
        /// </summary>
        [HttpGet("{id:guid}/join")]
        [EnableRateLimiting("demo-join")]
        public async Task<IActionResult> Join(Guid id, [FromQuery] Guid? p, CancellationToken cancellationToken)
        {
            var url = await _demoBookingService.ResolveLiveJoinUrlAsync(id, p, cancellationToken);
            return url is null
                ? NotFound("This demo booking (or that invitee) no longer exists.")
                : Redirect(url);
        }

        /// <summary>
        /// Permanently removes a demo booking -- a test entry, a mistaken double-booking -- and
        /// frees the teacher's slot. Refused once the lead is already invoiced or Enrolled; use
        /// its conversion status for that instead (see <see cref="UpdateConversionStatus"/>).
        /// </summary>
        [HttpDelete("{id:guid}")]
        [HasPermission(PermissionModule.Admission, PermissionAction.Delete)]
        public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
        {
            await _demoBookingService.DeleteAsync(id, cancellationToken);
            return NoContent();
        }

        /// <summary>Every active teacher's load around this booking's slot, for the reassignment page.</summary>
        [HttpGet("{id:guid}/teacher-workload")]
        [HasPermission(PermissionModule.Admission, PermissionAction.View)]
        public async Task<ActionResult<IReadOnlyList<TeacherWorkloadDto>>> TeacherWorkload(Guid id, CancellationToken cancellationToken)
        {
            return Ok(await _demoBookingService.GetTeacherWorkloadAsync(id, cancellationToken));
        }

        /// <summary>Every manual teacher reassignment made on this booking, newest first.</summary>
        [HttpGet("{id:guid}/reassignment-history")]
        [HasPermission(PermissionModule.Admission, PermissionAction.View)]
        public async Task<ActionResult<IReadOnlyList<DemoReassignmentHistoryDto>>> ReassignmentHistory(Guid id, CancellationToken cancellationToken)
        {
            return Ok(await _demoBookingService.GetReassignmentHistoryAsync(id, cancellationToken));
        }

        /// <summary>Every follow-up note ever logged on this booking, newest first.</summary>
        [HttpGet("{id:guid}/follow-up-notes")]
        [HasPermission(PermissionModule.Admission, PermissionAction.View)]
        public async Task<ActionResult<IReadOnlyList<DemoBookingFollowUpDto>>> FollowUpNotes(Guid id, CancellationToken cancellationToken)
        {
            return Ok(await _demoBookingService.GetFollowUpNotesAsync(id, cancellationToken));
        }
    }
}
