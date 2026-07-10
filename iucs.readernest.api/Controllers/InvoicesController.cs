using iucs.readernest.api.Auth;
using iucs.readernest.application.Dto.Billing;
using iucs.readernest.application.Services;
using iucs.readernest.domain.Enums;
using Microsoft.AspNetCore.Mvc;

namespace iucs.readernest.api.Controllers
{
    [ApiController]
    [Route("api/invoices")]
    public class InvoicesController : ControllerBase
    {
        private readonly IBillingService _billingService;

        public InvoicesController(IBillingService billingService)
        {
            _billingService = billingService;
        }

        [HttpGet]
        [HasPermission(PermissionModule.BillingFinance, PermissionAction.View)]
        public async Task<ActionResult<IReadOnlyList<InvoiceDto>>> List(
            [FromQuery] InvoiceStatus? status,
            [FromQuery] Guid? parentProfileId,
            CancellationToken cancellationToken)
        {
            return Ok(await _billingService.ListInvoicesAsync(status, parentProfileId, cancellationToken));
        }

        [HttpPost]
        [HasPermission(PermissionModule.BillingFinance, PermissionAction.Create)]
        public async Task<ActionResult<InvoiceDto>> Create(CreateInvoiceRequest request, CancellationToken cancellationToken)
        {
            var invoice = await _billingService.CreateInvoiceAsync(request, cancellationToken);
            return CreatedAtAction(nameof(List), null, invoice);
        }

        [HttpPost("{id:guid}/payments")]
        [HasPermission(PermissionModule.BillingFinance, PermissionAction.Edit)]
        public async Task<ActionResult<InvoiceDto>> RecordPayment(
            Guid id,
            RecordPaymentRequest request,
            CancellationToken cancellationToken)
        {
            return Ok(await _billingService.RecordPaymentAsync(id, request, cancellationToken));
        }
    }
}
