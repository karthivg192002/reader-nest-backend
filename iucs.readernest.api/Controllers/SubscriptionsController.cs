using iucs.readernest.api.Auth;
using iucs.readernest.application.Dto.Billing;
using iucs.readernest.application.Services;
using iucs.readernest.domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace iucs.readernest.api.Controllers
{
    // Parent also carries BillingFinance:View for their own /parent/billing screen
    // (served separately by ParentPortalController) — without a role restriction that
    // same claim reaches this unscoped, admin-only subscriptions screen too.
    // AdmissionTeam is included deliberately: RequiredSystemRolePermissions grants every
    // admission account BillingFinance:View/Edit/Approve as a baseline, and /admission/packages
    // (SubAdminPackages, reused via route) calls this endpoint unconditionally alongside
    // package-plans — without AdmissionTeam here, every real admission-staff account 403's
    // on that page despite the menu correctly showing it. Confirmed live via a fresh
    // AdmissionTeam test account.
    [ApiController]
    [Route("api/subscriptions")]
    [Authorize(Roles = $"{nameof(UserRole.Admin)},{nameof(UserRole.SubAdmin)},{nameof(UserRole.AdmissionTeam)}")]
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
