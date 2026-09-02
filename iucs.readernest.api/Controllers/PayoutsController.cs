using System.Security.Claims;
using iucs.readernest.api.Auth;
using iucs.readernest.application.Dto.Payouts;
using iucs.readernest.application.Services;
using iucs.readernest.domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace iucs.readernest.api.Controllers
{
    [ApiController]
    [Route("api/payouts")]
    public class PayoutsController : ControllerBase
    {
        private readonly IPayoutService _payoutService;

        public PayoutsController(IPayoutService payoutService)
        {
            _payoutService = payoutService;
        }

        /// <summary>Visibility rule: admin (or a role explicitly granted Payouts:View, e.g. via Roles &amp; Menu Access) sees all payouts.</summary>
        [HttpGet]
        [HasPermission(PermissionModule.Payouts, PermissionAction.View)] // #6: payout/salary details are Admin-only unless explicitly granted
        public async Task<ActionResult<IReadOnlyList<PayoutDto>>> List(
            [FromQuery] int? year,
            [FromQuery] int? month,
            [FromQuery] Guid? teacherProfileId,
            CancellationToken cancellationToken)
        {
            return Ok(await _payoutService.ListAsync(year, month, teacherProfileId, cancellationToken));
        }

        /// <summary>Visibility rule: a teacher sees only their own payouts.</summary>
        [HttpGet("mine")]
        [Authorize(Roles = nameof(UserRole.Teacher))]
        public async Task<ActionResult<IReadOnlyList<PayoutDto>>> Mine(CancellationToken cancellationToken)
        {
            var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            return Ok(await _payoutService.ListForTeacherUserAsync(userId, cancellationToken));
        }

        /// <summary>Correction to one accrued line item -- the only way to act on a RequiresReview flag. Only while the payout is still Pending.</summary>
        [HttpPut("{id:guid}/items/{itemId:guid}")]
        [HasPermission(PermissionModule.Payouts, PermissionAction.Edit)] // #6: payout/salary details are Admin-only unless explicitly granted
        public async Task<ActionResult<PayoutDto>> AdjustItem(
            Guid id,
            Guid itemId,
            AdjustPayoutItemRequest request,
            CancellationToken cancellationToken)
        {
            return Ok(await _payoutService.AdjustItemAsync(id, itemId, request, cancellationToken));
        }

        /// <summary>Locks the month's total and emails the statement to the teacher. Gated on Approve, same as other consequential financial decisions (see InvoicesController).</summary>
        [HttpPost("{id:guid}/finalize")]
        [HasPermission(PermissionModule.Payouts, PermissionAction.Approve)] // #6: payout/salary details are Admin-only unless explicitly granted
        public async Task<ActionResult<PayoutDto>> Finalize(Guid id, CancellationToken cancellationToken)
        {
            return Ok(await _payoutService.FinalizeAsync(id, cancellationToken));
        }

        [HttpPost("{id:guid}/mark-paid")]
        [HasPermission(PermissionModule.Payouts, PermissionAction.Approve)] // #6: payout/salary details are Admin-only unless explicitly granted
        public async Task<ActionResult<PayoutDto>> MarkPaid(Guid id, CancellationToken cancellationToken)
        {
            return Ok(await _payoutService.MarkPaidAsync(id, cancellationToken));
        }
    }

    [ApiController]
    [Route("api/payout-rates")]
    public class PayoutRatesController : ControllerBase
    {
        private readonly IPayoutService _payoutService;

        public PayoutRatesController(IPayoutService payoutService)
        {
            _payoutService = payoutService;
        }

        [HttpGet]
        [HasPermission(PermissionModule.Payouts, PermissionAction.View)] // #6: payout/salary details are Admin-only unless explicitly granted
        public async Task<ActionResult<IReadOnlyList<PayoutRateDto>>> List(
            [FromQuery] Guid? teacherProfileId,
            CancellationToken cancellationToken)
        {
            return Ok(await _payoutService.ListRatesAsync(teacherProfileId, cancellationToken));
        }

        /// <summary>Configurable per-session rate by teacher and class duration. A single upsert endpoint (new card or edit of an existing one), so gated on Edit — matches the frontend's "Configure"/row-edit actions, both of which call this.</summary>
        [HttpPost]
        [HasPermission(PermissionModule.Payouts, PermissionAction.Edit)] // #6: payout/salary details are Admin-only unless explicitly granted
        public async Task<ActionResult<PayoutRateDto>> SetRate(
            SavePayoutRateRequest request,
            CancellationToken cancellationToken)
        {
            return Ok(await _payoutService.SetRateAsync(request, cancellationToken));
        }
    }
}
