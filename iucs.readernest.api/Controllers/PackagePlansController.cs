using iucs.readernest.api.Auth;
using iucs.readernest.application.Dto.Billing;
using iucs.readernest.application.Services;
using iucs.readernest.domain.Enums;
using Microsoft.AspNetCore.Mvc;

namespace iucs.readernest.api.Controllers
{
    [ApiController]
    [Route("api/package-plans")]
    public class PackagePlansController : ControllerBase
    {
        private readonly IBillingService _billingService;

        public PackagePlansController(IBillingService billingService)
        {
            _billingService = billingService;
        }

        [HttpGet]
        [HasPermission(PermissionModule.BillingFinance, PermissionAction.View)]
        public async Task<ActionResult<IReadOnlyList<PackagePlanDto>>> List(CancellationToken cancellationToken)
        {
            return Ok(await _billingService.ListPlansAsync(cancellationToken));
        }

        [HttpPost]
        [HasPermission(PermissionModule.BillingFinance, PermissionAction.Create)]
        public async Task<ActionResult<PackagePlanDto>> Create(SavePackagePlanRequest request, CancellationToken cancellationToken)
        {
            var plan = await _billingService.CreatePlanAsync(request, cancellationToken);
            return CreatedAtAction(nameof(List), null, plan);
        }

        [HttpPut("{id:guid}")]
        [HasPermission(PermissionModule.BillingFinance, PermissionAction.Edit)]
        public async Task<ActionResult<PackagePlanDto>> Update(
            Guid id,
            SavePackagePlanRequest request,
            CancellationToken cancellationToken)
        {
            return Ok(await _billingService.UpdatePlanAsync(id, request, cancellationToken));
        }
    }
}
