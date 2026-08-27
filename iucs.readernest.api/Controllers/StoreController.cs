using iucs.readernest.api.Auth;
using iucs.readernest.application.Dto.Admission;
using iucs.readernest.application.Dto.Courses;
using iucs.readernest.application.Services;
using iucs.readernest.domain.Enums;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace iucs.readernest.api.Controllers
{
    /// <summary>
    /// Public course catalog and "Enroll now" inquiry form — no login required, deliberately:
    /// the whole point is a visitor who doesn't have an account yet. See StoreInquiriesController
    /// for the staff-facing follow-up queue this feeds.
    /// </summary>
    [ApiController]
    [Route("api/store")]
    public class StoreController : ControllerBase
    {
        private readonly IStoreService _storeService;
        private readonly IDepartmentService _departmentService;

        public StoreController(IStoreService storeService, IDepartmentService departmentService)
        {
            _storeService = storeService;
            _departmentService = departmentService;
        }

        [HttpGet("plans")]
        public async Task<ActionResult<IReadOnlyList<StorePlanDto>>> Plans(CancellationToken cancellationToken)
        {
            return Ok(await _storeService.ListPublicPlansAsync(cancellationToken));
        }

        /// <summary>Active departments only, for the public demo-booking form's optional department filter.</summary>
        [HttpGet("departments")]
        public async Task<ActionResult<IReadOnlyList<DepartmentDto>>> Departments(CancellationToken cancellationToken)
        {
            return Ok(await _departmentService.ListAsync(includeInactive: false, cancellationToken));
        }

        [HttpPost("inquiries")]
        [EnableRateLimiting("store-inquiry")]
        public async Task<ActionResult<StoreInquiryDto>> CreateInquiry(
            CreateStoreInquiryRequest request,
            CancellationToken cancellationToken)
        {
            var inquiry = await _storeService.CreateInquiryAsync(request, cancellationToken);
            return CreatedAtAction(nameof(Plans), null, inquiry);
        }

        /// <summary>Public "Book a free demo" — no account needed; a teacher is always auto-assigned.</summary>
        [HttpPost("demo-bookings")]
        [EnableRateLimiting("store-inquiry")]
        public async Task<ActionResult<StoreDemoBookingConfirmationDto>> BookDemo(
            CreateStoreDemoBookingRequest request,
            CancellationToken cancellationToken)
        {
            var confirmation = await _storeService.BookDemoAsync(request, cancellationToken);
            return CreatedAtAction(nameof(Plans), null, confirmation);
        }

        /// <summary>Which 30-minute slots on a given day still have a teacher free — lets the
        /// booking form offer real openings instead of the visitor guessing a time.</summary>
        [HttpGet("demo-availability")]
        [EnableRateLimiting("store-inquiry")]
        public async Task<ActionResult<IReadOnlyList<AvailableDemoSlotDto>>> DemoAvailability(
            [FromQuery] DateOnly date,
            [FromQuery] Guid? departmentId,
            CancellationToken cancellationToken)
        {
            return Ok(await _storeService.ListAvailableDemoSlotsAsync(date, departmentId, cancellationToken));
        }
    }

    /// <summary>Admission-team follow-up queue for public store inquiries.</summary>
    // No [Authorize(Roles=...)] here, deliberately: gated purely on the Admission permission
    // claim, same as EnrollmentFormsController's and DemoBookingsController's admin-side
    // actions for the exact same module. A hard-coded Admin/SubAdmin role list would lock out
    // AdmissionTeam accounts even though they're granted full Admission access by default and
    // this queue is literally their job — see RequiredSystemRolePermissions.cs's note that an
    // Authorize(Roles=...) list must include AdmissionTeam for a permission-gated admission
    // endpoint to actually be reachable by that role.
    [ApiController]
    [Route("api/store/inquiries")]
    public class StoreInquiriesController : ControllerBase
    {
        private readonly IStoreService _storeService;

        public StoreInquiriesController(IStoreService storeService)
        {
            _storeService = storeService;
        }

        [HttpGet]
        [HasPermission(PermissionModule.Admission, PermissionAction.View)]
        public async Task<ActionResult<IReadOnlyList<StoreInquiryDto>>> List(
            [FromQuery] StoreInquiryStatus? status,
            CancellationToken cancellationToken)
        {
            return Ok(await _storeService.ListInquiriesAsync(status, cancellationToken));
        }

        [HttpPut("{id:guid}/status")]
        [HasPermission(PermissionModule.Admission, PermissionAction.Edit)]
        public async Task<ActionResult<StoreInquiryDto>> UpdateStatus(
            Guid id,
            UpdateStoreInquiryStatusRequest request,
            CancellationToken cancellationToken)
        {
            return Ok(await _storeService.UpdateInquiryStatusAsync(id, request, cancellationToken));
        }
    }
}
