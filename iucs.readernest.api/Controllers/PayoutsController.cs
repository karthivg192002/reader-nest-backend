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

        /// <summary>Visibility rule: admin (or granted sub-admin) sees all payouts.</summary>
        [HttpGet]
        [HasPermission(PermissionModule.Payouts, PermissionAction.View)]
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

        /// <summary>Locks the month's total and emails the statement to the teacher.</summary>
        [HttpPost("{id:guid}/finalize")]
        [HasPermission(PermissionModule.Payouts, PermissionAction.Approve)]
        public async Task<ActionResult<PayoutDto>> Finalize(Guid id, CancellationToken cancellationToken)
        {
            return Ok(await _payoutService.FinalizeAsync(id, cancellationToken));
        }

        [HttpPost("{id:guid}/mark-paid")]
        [HasPermission(PermissionModule.Payouts, PermissionAction.Approve)]
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
        [HasPermission(PermissionModule.Payouts, PermissionAction.View)]
        public async Task<ActionResult<IReadOnlyList<PayoutRateDto>>> List(
            [FromQuery] Guid? teacherProfileId,
            CancellationToken cancellationToken)
        {
            return Ok(await _payoutService.ListRatesAsync(teacherProfileId, cancellationToken));
        }

        /// <summary>Configurable per-session rate by teacher and class duration.</summary>
        [HttpPost]
        [HasPermission(PermissionModule.Payouts, PermissionAction.Edit)]
        public async Task<ActionResult<PayoutRateDto>> SetRate(
            SavePayoutRateRequest request,
            CancellationToken cancellationToken)
        {
            return Ok(await _payoutService.SetRateAsync(request, cancellationToken));
        }
    }
}
