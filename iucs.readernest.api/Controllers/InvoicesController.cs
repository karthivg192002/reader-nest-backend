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

        /// <summary>Shareable Pay Now link, routed through the invoice's department gateway account.</summary>
        [HttpPost("{id:guid}/payment-link")]
        [HasPermission(PermissionModule.BillingFinance, PermissionAction.Edit)]
        public async Task<ActionResult<PaymentLinkDto>> CreatePaymentLink(Guid id, CancellationToken cancellationToken)
        {
            return Ok(await _billingService.CreatePaymentLinkAsync(id, cancellationToken));
        }

        /// <summary>Pending parent cash intents awaiting staff confirmation.</summary>
        [HttpGet("cash-intents")]
        [HasPermission(PermissionModule.BillingFinance, PermissionAction.View)]
        public async Task<ActionResult<IReadOnlyList<CashIntentDto>>> ListCashIntents(CancellationToken cancellationToken)
        {
            return Ok(await _billingService.ListPendingCashIntentsAsync(cancellationToken));
        }

        /// <summary>
        /// Confirms the cash was collected: settles the intent, generates the receipt and updates
        /// the invoice. Gated on Approve specifically (not Edit) — a login only sees the confirm
        /// action once an Admin has explicitly granted it Approve on Billing &amp; Finance.
        /// </summary>
        [HttpPost("cash-intents/{transactionId:guid}/confirm")]
        [HasPermission(PermissionModule.BillingFinance, PermissionAction.Approve)]
        public async Task<ActionResult<CashIntentDto>> ConfirmCashIntent(
            Guid transactionId,
            ConfirmCashIntentRequest request,
            CancellationToken cancellationToken)
        {
            return Ok(await _billingService.ConfirmCashIntentAsync(transactionId, request, cancellationToken));
        }

        /// <summary>Same Approve gate as confirmation — rejecting is the other half of the same decision.</summary>
        [HttpPost("cash-intents/{transactionId:guid}/reject")]
        [HasPermission(PermissionModule.BillingFinance, PermissionAction.Approve)]
        public async Task<IActionResult> RejectCashIntent(
            Guid transactionId,
            RejectCashIntentRequest request,
            CancellationToken cancellationToken)
        {
            await _billingService.RejectCashIntentAsync(transactionId, request, cancellationToken);
            return NoContent();
        }

        [HttpGet("suspensions")]
        [HasPermission(PermissionModule.BillingFinance, PermissionAction.View)]
        public async Task<ActionResult<IReadOnlyList<FeeSuspensionDto>>> ListSuspensions(
            [FromQuery] SuspensionStatus? status,
            CancellationToken cancellationToken)
        {
            return Ok(await _billingService.ListSuspensionsAsync(status, cancellationToken));
        }

        /// <summary>Manual admin restoration of a fee-suspended account.</summary>
        [HttpPost("suspensions/{id:guid}/lift")]
        [HasPermission(PermissionModule.BillingFinance, PermissionAction.Approve)]
        public async Task<ActionResult<FeeSuspensionDto>> LiftSuspension(Guid id, CancellationToken cancellationToken)
        {
            return Ok(await _billingService.LiftSuspensionAsync(id, cancellationToken));
        }

        [HttpGet("refunds")]
        [HasPermission(PermissionModule.BillingFinance, PermissionAction.View)]
        public async Task<ActionResult<IReadOnlyList<RefundDto>>> ListRefunds(CancellationToken cancellationToken)
        {
            return Ok(await _billingService.ListRefundsAsync(cancellationToken));
        }

        [HttpPost("refunds")]
        [HasPermission(PermissionModule.BillingFinance, PermissionAction.Create)]
        public async Task<ActionResult<RefundDto>> RequestRefund(RequestRefundRequest request, CancellationToken cancellationToken)
        {
            return Ok(await _billingService.RequestRefundAsync(request, cancellationToken));
        }

        [HttpPost("refunds/{id:guid}/review")]
        [HasPermission(PermissionModule.BillingFinance, PermissionAction.Approve)]
        public async Task<ActionResult<RefundDto>> ReviewRefund(
            Guid id,
            ReviewRefundRequest request,
            CancellationToken cancellationToken)
        {
            return Ok(await _billingService.ReviewRefundAsync(id, request, cancellationToken));
        }
    }
}
