using iucs.readernest.api.Auth;
using iucs.readernest.application.Dto.Communication;
using iucs.readernest.application.Services;
using iucs.readernest.domain.Enums;
using Microsoft.AspNetCore.Mvc;

namespace iucs.readernest.api.Controllers
{
    /// <summary>
    /// Email Template Master: the admin-designed Subject/HtmlBody every automated
    /// system email renders from. Shown on the admin Settings → Email Templates screen.
    /// </summary>
    [ApiController]
    [Route("api/email-templates")]
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
