using System.Security.Claims;
using iucs.readernest.api.Auth;
using iucs.readernest.application.Dto.Payouts;
using iucs.readernest.application.Services;
using iucs.readernest.domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace iucs.readernest.api.Controllers
{
    /// <summary>
    /// Non-teacher staff pay: fixed monthly salary (Coordinator, Relationship Manager, Admin) or
    /// a percentage of monthly collection (Admission). Teachers keep their own batch-wise/
    /// per-minute payout under /api/payouts.
    /// </summary>
    [ApiController]
    [Route("api/staff-payouts")]
    public class StaffPayoutsController : ControllerBase
    {
        private const string StaffRoles = $"{nameof(UserRole.SubAdmin)},{nameof(UserRole.AdmissionTeam)},{nameof(UserRole.Admin)}";

        private readonly IStaffPayoutService _staffPayoutService;

        public StaffPayoutsController(IStaffPayoutService staffPayoutService)
        {
            _staffPayoutService = staffPayoutService;
        }

        /// <summary>Every Coordinator/Relationship Manager/Admission/Admin account, for the compensation-settings screen.</summary>
        [HttpGet("eligible-staff")]
        [HasPermission(PermissionModule.Payouts, PermissionAction.View)]
        public async Task<ActionResult<IReadOnlyList<EligibleStaffDto>>> ListEligibleStaff(CancellationToken cancellationToken)
        {
            return Ok(await _staffPayoutService.ListEligibleStaffAsync(cancellationToken));
        }

        /// <summary>Sets or edits one staff member's fixed salary or collection percentage.</summary>
        [HttpPut("compensation/{userId:guid}")]
        [HasPermission(PermissionModule.Payouts, PermissionAction.Edit)]
        public async Task<ActionResult<StaffCompensationSettingDto>> SetCompensation(
            Guid userId, SaveStaffCompensationRequest request, CancellationToken cancellationToken)
        {
            return Ok(await _staffPayoutService.SetCompensationAsync(userId, request, cancellationToken));
        }

        /// <summary>The caller's own current month plus every past month on record — "My Salary"/"My Earnings" for Coordinator/RM/Admission/Admin.</summary>
        [HttpGet("mine")]
        [Authorize(Roles = StaffRoles)]
        public async Task<ActionResult<StaffEarningsDto>> Mine(CancellationToken cancellationToken)
        {
            var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            return Ok(await _staffPayoutService.GetEarningsAsync(userId, cancellationToken));
        }

        /// <summary>Admin/Management oversight: any one staff member's current month plus history.</summary>
        [HttpGet("{userId:guid}")]
        [HasPermission(PermissionModule.Payouts, PermissionAction.View)]
        public async Task<ActionResult<StaffEarningsDto>> Get(Guid userId, CancellationToken cancellationToken)
        {
            return Ok(await _staffPayoutService.GetEarningsAsync(userId, cancellationToken));
        }

        /// <summary>Marks a frozen (past-month) payout as paid.</summary>
        [HttpPost("{id:guid}/mark-paid")]
        [HasPermission(PermissionModule.Payouts, PermissionAction.Edit)]
        public async Task<ActionResult<StaffPayoutDto>> MarkPaid(Guid id, CancellationToken cancellationToken)
        {
            return Ok(await _staffPayoutService.MarkPaidAsync(id, cancellationToken));
        }
    }
}
