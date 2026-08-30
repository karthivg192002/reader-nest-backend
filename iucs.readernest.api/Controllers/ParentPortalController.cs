using System.Security.Claims;
using iucs.readernest.application.Dto.Billing;
using iucs.readernest.application.Dto.Enrollment;
using iucs.readernest.application.Dto.Portal;
using iucs.readernest.application.Dto.Resources;
using iucs.readernest.application.Dto.Sessions;
using iucs.readernest.application.Services;
using iucs.readernest.domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace iucs.readernest.api.Controllers
{
    /// <summary>Everything the signed-in parent's unified dashboard needs.</summary>
    [ApiController]
    [Route("api/parent-portal")]
    [Authorize(Roles = nameof(UserRole.Parent))]
    public class ParentPortalController : ControllerBase
    {
        private readonly IParentPortalService _parentPortal;
        private readonly IEnrollmentService _enrollmentService;
        private readonly IIntegrationService _integrationService;

        public ParentPortalController(
            IParentPortalService parentPortal,
            IEnrollmentService enrollmentService,
            IIntegrationService integrationService)
        {
            _parentPortal = parentPortal;
            _enrollmentService = enrollmentService;
            _integrationService = integrationService;
        }

        [HttpGet("dashboard")]
        public async Task<ActionResult<ParentDashboardDto>> Dashboard(CancellationToken cancellationToken)
        {
            return Ok(await _parentPortal.GetDashboardAsync(UserId(), cancellationToken));
        }

        [HttpGet("children")]
        public async Task<ActionResult<IReadOnlyList<ChildDto>>> Children(CancellationToken cancellationToken)
        {
            return Ok(await _enrollmentService.ListChildrenForParentUserAsync(UserId(), cancellationToken));
        }

        [HttpGet("schedule")]
        public async Task<ActionResult<IReadOnlyList<ClassSessionDto>>> Schedule(
            [FromQuery] DateTime fromUtc,
            [FromQuery] DateTime toUtc,
            CancellationToken cancellationToken)
        {
            return Ok(await _parentPortal.GetScheduleAsync(UserId(), fromUtc, toUtc, cancellationToken));
        }

        /// <summary>Granted resources; returns 400 with the Pay Now message while fee-suspended.</summary>
        [HttpGet("resources")]
        public async Task<ActionResult<IReadOnlyList<ResourceDto>>> Resources(CancellationToken cancellationToken)
        {
            return Ok(await _parentPortal.GetResourcesAsync(UserId(), cancellationToken));
        }

        [HttpGet("invoices")]
        public async Task<ActionResult<IReadOnlyList<InvoiceDto>>> Invoices(CancellationToken cancellationToken)
        {
            return Ok(await _parentPortal.GetInvoicesAsync(UserId(), cancellationToken));
        }

        /// <summary>Enabled payment methods (gateways + Cash) for the Pay Now popup, from Settings → Integrations.</summary>
        [HttpGet("payment-methods")]
        public async Task<ActionResult<IReadOnlyList<PaymentMethodOptionDto>>> PaymentMethods(CancellationToken cancellationToken)
        {
            return Ok(await _integrationService.GetEnabledPaymentMethodsAsync(cancellationToken));
        }

        /// <summary>
        /// Downloadable invoice for the parent (ownership-checked): the same "Bill of Supply"
        /// PDF the admin side generates — was previously its own separate, unbranded HTML
        /// document with a generic "Meet to Manage" placeholder header, matching neither the
        /// org's real invoice design nor the admin-facing PDF.
        /// </summary>
        [HttpGet("invoices/{id:guid}/download")]
        public async Task<IActionResult> DownloadInvoice(
            Guid id,
            [FromServices] IBillingService billingService,
            CancellationToken cancellationToken)
        {
            var (content, fileName) = await billingService.GenerateParentInvoicePdfAsync(UserId(), id, cancellationToken);
            return File(content, "application/pdf", fileName);
        }

        /// <summary>
        /// Pay Now: "cash" records a pending intent for admin confirmation; a gateway key
        /// returns a checkout URL whose webhook settles the invoice automatically.
        /// </summary>
        [HttpPost("invoices/{id:guid}/pay")]
        public async Task<ActionResult<ParentPaymentResultDto>> PayInvoice(
            Guid id,
            InitiateParentPaymentRequest request,
            [FromServices] IBillingService billingService,
            CancellationToken cancellationToken)
        {
            return Ok(await billingService.InitiateParentPaymentAsync(UserId(), id, request, cancellationToken));
        }

        /// <summary>
        /// Pay Now, in-page variant: creates a gateway order and returns what the Razorpay
        /// popup (checkout.js) needs, so the payer completes payment without leaving the page.
        /// </summary>
        [HttpPost("invoices/{id:guid}/checkout")]
        public async Task<ActionResult<InlineCheckoutDto>> StartInlineCheckout(
            Guid id,
            InitiateParentPaymentRequest request,
            [FromServices] IBillingService billingService,
            CancellationToken cancellationToken)
        {
            return Ok(await billingService.StartParentInlineCheckoutAsync(UserId(), id, request, cancellationToken));
        }

        /// <summary>
        /// Settles an in-page checkout from the popup's success proof (order id, payment id,
        /// signature) after server-side signature verification. Returns the refreshed invoice
        /// so the UI flips to Paid immediately.
        /// </summary>
        [HttpPost("invoices/{id:guid}/checkout/verify")]
        public async Task<ActionResult<InvoiceDto>> VerifyInlineCheckout(
            Guid id,
            VerifyInlineCheckoutRequest request,
            [FromServices] IBillingService billingService,
            CancellationToken cancellationToken)
        {
            return Ok(await billingService.VerifyParentInlineCheckoutAsync(UserId(), id, request, cancellationToken));
        }

        /// <summary>
        /// After returning from the gateway checkout, asks the gateway directly whether the
        /// link is paid and settles the invoice if so — no webhook required. Returns the
        /// refreshed invoice so the UI can flip to Paid.
        /// </summary>
        [HttpPost("invoices/{id:guid}/refresh-payment")]
        public async Task<ActionResult<InvoiceDto>> RefreshPayment(
            Guid id,
            [FromServices] IBillingService billingService,
            CancellationToken cancellationToken)
        {
            return Ok(await billingService.ReconcileInvoicePaymentAsync(UserId(), id, cancellationToken));
        }

        /// <summary>Non-expired recordings for a session, once the caller's own child is confirmed enrolled in its batch.</summary>
        [HttpGet("sessions/{sessionId:guid}/recordings")]
        public async Task<ActionResult<IReadOnlyList<SessionRecordingDto>>> SessionRecordings(
            Guid sessionId,
            CancellationToken cancellationToken)
        {
            return Ok(await _parentPortal.GetRecordingsAsync(UserId(), sessionId, cancellationToken));
        }

        /// <summary>Grant-checked worksheet download (books stay view-only).</summary>
        [HttpGet("resources/{id:guid}/download")]
        public async Task<IActionResult> DownloadResource(
            Guid id,
            [FromServices] IResourceService resourceService,
            [FromServices] application.Common.Interfaces.IFileStorage fileStorage,
            CancellationToken cancellationToken)
        {
            await _parentPortal.GetResourceForDownloadAsync(UserId(), id, cancellationToken);

            var resource = await resourceService.GetForDownloadAsync(id, cancellationToken);
            var stream = await fileStorage.OpenReadAsync(resource.FileUrl, cancellationToken);
            if (stream is null)
            {
                return NotFound();
            }

            var mimeType = string.IsNullOrWhiteSpace(resource.MimeType) ? "application/octet-stream" : resource.MimeType;
            return File(stream, mimeType, $"{resource.Title}{Path.GetExtension(resource.FileUrl)}");
        }

        private Guid UserId() => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
    }
}
