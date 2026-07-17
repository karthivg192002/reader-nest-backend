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
        /// Downloadable invoice for the parent (ownership-checked): a self-contained,
        /// print-ready HTML document (print → Save as PDF). Format is finalizable once
        /// the client supplies their letterhead.
        /// </summary>
        [HttpGet("invoices/{id:guid}/download")]
        public async Task<IActionResult> DownloadInvoice(
            Guid id,
            [FromServices] IBillingService billingService,
            CancellationToken cancellationToken)
        {
            var (invoice, parentName) = await billingService.GetParentInvoiceAsync(UserId(), id, cancellationToken);
            var balance = invoice.Amount - invoice.AmountPaid;
            var html = $$"""
<!DOCTYPE html>
<html><head><meta charset="utf-8"><title>Invoice {{invoice.InvoiceNumber}}</title>
<style>
  body { font-family: 'Segoe UI', Arial, sans-serif; margin: 40px; color: #1a1a2e; }
  .head { display: flex; justify-content: space-between; border-bottom: 3px solid #1F6FE0; padding-bottom: 16px; }
  .brand { font-size: 22px; font-weight: 800; color: #1F6FE0; }
  h1 { font-size: 18px; margin: 24px 0 4px; }
  table { width: 100%; border-collapse: collapse; margin-top: 16px; }
  th, td { text-align: left; padding: 10px 12px; border-bottom: 1px solid #e5e7eb; }
  th { background: #f3f6fc; font-size: 12px; text-transform: uppercase; letter-spacing: .04em; }
  .total td { font-weight: 800; border-top: 2px solid #1a1a2e; }
  .muted { color: #667085; font-size: 13px; }
  .badge { display: inline-block; padding: 3px 10px; border-radius: 999px; font-size: 12px; font-weight: 700;
           background: {{(invoice.Status == domain.Enums.InvoiceStatus.Paid ? "#e8f7ee; color:#177245" : "#fdf1e2; color:#a05a00")}}; }
</style></head>
<body>
  <div class="head">
    <div><div class="brand">The Reader Nest</div><div class="muted">Learning Management &amp; Virtual Classroom</div></div>
    <div style="text-align:right"><div style="font-size:20px;font-weight:800">INVOICE</div><div class="muted">{{invoice.InvoiceNumber}}</div></div>
  </div>
  <h1>Billed to</h1>
  <div>{{System.Net.WebUtility.HtmlEncode(parentName)}}</div>
  <div class="muted">Issued {{invoice.IssuedAtUtc:dd MMM yyyy}} &middot; Due {{invoice.DueDate:dd MMM yyyy}} &middot; <span class="badge">{{invoice.Status}}</span></div>
  <table>
    <tr><th>Description</th><th style="text-align:right">Amount ({{invoice.Currency}})</th></tr>
    <tr><td>{{invoice.Department}} programme fees</td><td style="text-align:right">{{invoice.Amount:0.00}}</td></tr>
    <tr><td>Paid to date</td><td style="text-align:right">-{{invoice.AmountPaid:0.00}}</td></tr>
    <tr class="total"><td>Balance due</td><td style="text-align:right">{{balance:0.00}}</td></tr>
  </table>
  <p class="muted">Pay securely from the parent portal (Payments &amp; Billing → Pay Now). This is a system-generated invoice.</p>
</body></html>
""";
            return File(
                System.Text.Encoding.UTF8.GetBytes(html),
                "text/html",
                $"invoice-{invoice.InvoiceNumber}.html");
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
            var absolutePath = fileStorage.GetAbsolutePath(resource.FileUrl);
            if (!System.IO.File.Exists(absolutePath))
            {
                return NotFound();
            }

            var mimeType = string.IsNullOrWhiteSpace(resource.MimeType) ? "application/octet-stream" : resource.MimeType;
            return PhysicalFile(absolutePath, mimeType, $"{resource.Title}{Path.GetExtension(resource.FileUrl)}");
        }

        private Guid UserId() => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
    }
}
