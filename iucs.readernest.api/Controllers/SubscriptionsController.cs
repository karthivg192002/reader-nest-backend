using iucs.readernest.api.Auth;
using iucs.readernest.application.Dto.Billing;
using iucs.readernest.application.Services;
using iucs.readernest.domain.Enums;
using Microsoft.AspNetCore.Mvc;

namespace iucs.readernest.api.Controllers
{
    [ApiController]
    [Route("api/subscriptions")]
    public class SubscriptionsController : ControllerBase
    {
        private readonly IBillingService _billingService;

        public SubscriptionsController(IBillingService billingService)
        {
            _billingService = billingService;
        }

        /// <summary>Renewal tracking: filter by status to see lapsed vs renewed subscriptions.</summary>
        [HttpGet]
        [HasPermission(PermissionModule.BillingFinance, PermissionAction.View)]
        public async Task<ActionResult<IReadOnlyList<SubscriptionDto>>> List(
            [FromQuery] SubscriptionStatus? status,
            CancellationToken cancellationToken)
        {
            return Ok(await _billingService.ListSubscriptionsAsync(status, cancellationToken));
        }

        [HttpPost]
        [HasPermission(PermissionModule.BillingFinance, PermissionAction.Create)]
        public async Task<ActionResult<SubscriptionDto>> Create(
            CreateSubscriptionRequest request,
            CancellationToken cancellationToken)
        {
            var subscription = await _billingService.CreateSubscriptionAsync(request, cancellationToken);
            return CreatedAtAction(nameof(List), null, subscription);
        }

        /// <summary>Renewal conversion: reactivates the subscription and restarts auto billing.</summary>
        [HttpPost("{id:guid}/renew")]
        [HasPermission(PermissionModule.BillingFinance, PermissionAction.Edit)]
        public async Task<ActionResult<SubscriptionDto>> Renew(Guid id, CancellationToken cancellationToken)
        {
            return Ok(await _billingService.RenewSubscriptionAsync(id, cancellationToken));
        }

        [HttpPost("{id:guid}/cancel")]
        [HasPermission(PermissionModule.BillingFinance, PermissionAction.Edit)]
        public async Task<ActionResult<SubscriptionDto>> Cancel(Guid id, CancellationToken cancellationToken)
        {
            return Ok(await _billingService.CancelSubscriptionAsync(id, cancellationToken));
        }
    }
}
