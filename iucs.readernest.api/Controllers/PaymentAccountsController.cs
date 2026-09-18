using iucs.readernest.api.Auth;
using iucs.readernest.application.Dto.Billing;
using iucs.readernest.application.Services;
using iucs.readernest.domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace iucs.readernest.api.Controllers
{
    /// <summary>
    /// Department payment accounts + parent→account mapping (admin Payment Gateway Mapping screen).
    /// Parent also carries BillingFinance:View for their own /parent/billing screen (served
    /// separately by ParentPortalController) — without a role restriction that same claim
    /// would reach this gateway-wiring configuration screen too.
    /// AdmissionTeam is included deliberately: RequiredSystemRolePermissions grants every
    /// admission account BillingFinance:View/Edit/Approve as a baseline, and /admission/payment-mapping
    /// (SubAdminPaymentMapping, reused via route) calls this endpoint unconditionally — without
    /// AdmissionTeam here, every real admission-staff account 403's on that page despite the
    /// menu correctly showing it. Confirmed live via a fresh AdmissionTeam test account.
    /// </summary>
    [ApiController]
    [Route("api/payment-accounts")]
    [Authorize(Roles = $"{nameof(UserRole.Admin)},{nameof(UserRole.SubAdmin)},{nameof(UserRole.AdmissionTeam)}")]
    public class PaymentAccountsController : ControllerBase
    {
        private readonly IBillingService _billingService;

        public PaymentAccountsController(IBillingService billingService)
        {
            _billingService = billingService;
        }

        [HttpGet]
        [HasPermission(PermissionModule.BillingFinance, PermissionAction.View)]
        public async Task<ActionResult<IReadOnlyList<PaymentAccountDto>>> List(CancellationToken cancellationToken)
        {
            return Ok(await _billingService.ListPaymentAccountsAsync(cancellationToken));
        }

        [HttpPut("mapping")]
        [HasPermission(PermissionModule.BillingFinance, PermissionAction.Edit)]
        public async Task<IActionResult> SetMapping(SavePaymentMappingRequest request, CancellationToken cancellationToken)
        {
            await _billingService.SetParentPaymentAccountAsync(request, cancellationToken);
            return NoContent();
        }

        /// <summary>Admin edit of a department account's gateway wiring (name/provider/ref/active).</summary>
        [HttpPut("{id:guid}")]
        [HasPermission(PermissionModule.BillingFinance, PermissionAction.Edit)]
        public async Task<ActionResult<PaymentAccountDto>> Update(
            Guid id,
            UpdatePaymentAccountRequest request,
            CancellationToken cancellationToken)
        {
            return Ok(await _billingService.UpdatePaymentAccountAsync(id, request, cancellationToken));
        }
    }
}
