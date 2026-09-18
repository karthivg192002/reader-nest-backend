using iucs.readernest.api.Auth;
using iucs.readernest.application.Dto.Communication;
using iucs.readernest.application.Services;
using iucs.readernest.domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace iucs.readernest.api.Controllers
{
    /// <summary>
    /// Email Template Master: the admin-designed Subject/HtmlBody every automated
    /// system email renders from. Shown on the admin Settings → Email Templates screen.
    /// Parent also carries Communication:View for their own /parent/notifications screen —
    /// without a role restriction that same claim would reach this system-template config too.
    /// AdmissionTeam is included deliberately (not just SubAdmin): it's a distinct backend role
    /// from SubAdmin for real delegated-portal accounts, and /admission/email-templates already
    /// exists in the menu system for any admin who grants Communication to an admission account
    /// — see the same fix already applied to PackagePlansController/PaymentAccountsController/
    /// SubscriptionsController/ResourcesController for the identical gap.
    /// </summary>
    [ApiController]
    [Route("api/email-templates")]
    [Authorize(Roles = $"{nameof(UserRole.Admin)},{nameof(UserRole.SubAdmin)},{nameof(UserRole.AdmissionTeam)}")]
    public class EmailTemplatesController : ControllerBase
    {
        private readonly IEmailTemplateService _emailTemplateService;

        public EmailTemplatesController(IEmailTemplateService emailTemplateService)
        {
            _emailTemplateService = emailTemplateService;
        }

        [HttpGet]
        [HasPermission(PermissionModule.Communication, PermissionAction.View)]
        public async Task<ActionResult<IReadOnlyList<EmailTemplateDto>>> List(CancellationToken cancellationToken)
        {
            return Ok(await _emailTemplateService.ListAsync(cancellationToken));
        }

        [HttpGet("{id:guid}")]
        [HasPermission(PermissionModule.Communication, PermissionAction.View)]
        public async Task<ActionResult<EmailTemplateDto>> Get(Guid id, CancellationToken cancellationToken)
        {
            return Ok(await _emailTemplateService.GetAsync(id, cancellationToken));
        }

        [HttpPut("{id:guid}")]
        [HasPermission(PermissionModule.Communication, PermissionAction.Edit)]
        public async Task<ActionResult<EmailTemplateDto>> Update(
            Guid id,
            SaveEmailTemplateRequest request,
            CancellationToken cancellationToken)
        {
            return Ok(await _emailTemplateService.UpdateAsync(id, request, cancellationToken));
        }

        [HttpPost("{id:guid}/preview")]
        [HasPermission(PermissionModule.Communication, PermissionAction.View)]
        public async Task<ActionResult<EmailTemplatePreviewDto>> Preview(
            Guid id,
            PreviewEmailTemplateRequest request,
            CancellationToken cancellationToken)
        {
            return Ok(await _emailTemplateService.PreviewAsync(id, request, cancellationToken));
        }
    }
}
