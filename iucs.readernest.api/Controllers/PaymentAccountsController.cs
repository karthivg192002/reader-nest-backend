using iucs.readernest.api.Auth;
using iucs.readernest.application.Dto.Billing;
using iucs.readernest.application.Services;
using iucs.readernest.domain.Enums;
using Microsoft.AspNetCore.Mvc;

namespace iucs.readernest.api.Controllers
{
    /// <summary>Department payment accounts + parent→account mapping (admin Payment Gateway Mapping screen).</summary>
    [ApiController]
    [Route("api/payment-accounts")]
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
    }
}
