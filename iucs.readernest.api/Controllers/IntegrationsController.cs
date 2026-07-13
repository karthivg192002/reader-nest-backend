using iucs.readernest.api.Auth;
using iucs.readernest.application.Dto.Integrations;
using iucs.readernest.application.Services;
using iucs.readernest.domain.Enums;
using Microsoft.AspNetCore.Mvc;

namespace iucs.readernest.api.Controllers
{
    /// <summary>
    /// Master list of third-party integrations (Email, WhatsApp, Razorpay, Cashfree,
    /// Zoom, Jitsi Meet, ...) shown on the admin Settings &amp; Branding → Integrations tab.
    /// </summary>
    [ApiController]
    [Route("api/integrations")]
    public class IntegrationsController : ControllerBase
    {
        private readonly IIntegrationService _integrationService;

        public IntegrationsController(IIntegrationService integrationService)
        {
            _integrationService = integrationService;
        }

        [HttpGet]
        [HasPermission(PermissionModule.Settings, PermissionAction.View)]
        public async Task<ActionResult<IReadOnlyList<IntegrationDto>>> List(CancellationToken cancellationToken)
        {
            return Ok(await _integrationService.ListAsync(cancellationToken));
        }

        [HttpPost]
        [HasPermission(PermissionModule.Settings, PermissionAction.Edit)]
        public async Task<ActionResult<IntegrationDto>> Create(SaveIntegrationRequest request, CancellationToken cancellationToken)
        {
            var integration = await _integrationService.CreateAsync(request, cancellationToken);
            return CreatedAtAction(nameof(List), null, integration);
        }

        [HttpPut("{id:guid}")]
        [HasPermission(PermissionModule.Settings, PermissionAction.Edit)]
        public async Task<ActionResult<IntegrationDto>> Update(
            Guid id,
            SaveIntegrationRequest request,
            CancellationToken cancellationToken)
        {
            return Ok(await _integrationService.UpdateAsync(id, request, cancellationToken));
        }

        [HttpDelete("{id:guid}")]
        [HasPermission(PermissionModule.Settings, PermissionAction.Edit)]
        public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
        {
            await _integrationService.DeleteAsync(id, cancellationToken);
            return NoContent();
        }
    }
}
