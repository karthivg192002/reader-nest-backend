using iucs.readernest.api.Auth;
using iucs.readernest.application.Dto.Admission;
using iucs.readernest.application.Services;
using iucs.readernest.domain.Enums;
using Microsoft.AspNetCore.Mvc;

namespace iucs.readernest.api.Controllers
{
    [ApiController]
    [Route("api/demo-bookings")]
    public class DemoBookingsController : ControllerBase
    {
        private readonly IDemoBookingService _demoBookingService;

        public DemoBookingsController(IDemoBookingService demoBookingService)
        {
            _demoBookingService = demoBookingService;
        }

        [HttpGet]
        [HasPermission(PermissionModule.Admission, PermissionAction.View)]
        public async Task<ActionResult<IReadOnlyList<DemoBookingDto>>> List(
            [FromQuery] ConversionStatus? status,
            CancellationToken cancellationToken)
        {
            return Ok(await _demoBookingService.ListAsync(status, cancellationToken));
        }

        [HttpGet("{id:guid}")]
        [HasPermission(PermissionModule.Admission, PermissionAction.View)]
        public async Task<ActionResult<DemoBookingDto>> Get(Guid id, CancellationToken cancellationToken)
        {
            return Ok(await _demoBookingService.GetAsync(id, cancellationToken));
        }

        [HttpPost]
        [HasPermission(PermissionModule.Admission, PermissionAction.Create)]
        public async Task<ActionResult<DemoBookingDto>> Create(
            CreateDemoBookingRequest request,
            CancellationToken cancellationToken)
        {
            var booking = await _demoBookingService.CreateAsync(request, cancellationToken);
            return CreatedAtAction(nameof(Get), new { id = booking.Id }, booking);
        }

        [HttpPut("{id:guid}/conversion-status")]
        [HasPermission(PermissionModule.Admission, PermissionAction.Edit)]
        public async Task<ActionResult<DemoBookingDto>> UpdateConversionStatus(
            Guid id,
            UpdateConversionStatusRequest request,
            CancellationToken cancellationToken)
        {
            return Ok(await _demoBookingService.UpdateConversionStatusAsync(id, request, cancellationToken));
        }
    }
}
