using iucs.readernest.application.Dto.Admission;
using iucs.readernest.application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace iucs.readernest.api.Controllers
{
    /// <summary>
    /// Anonymous parent-facing side of the portal admission flow: the /pay/{token} page a counsellor
    /// shares. The parent has no login yet (it is only emailed after the counsellor clicks Enroll),
    /// so access is by the unguessable token alone and the responses carry no ids or contact details.
    /// </summary>
    [ApiController]
    [Route("api/public/admission-payments")]
    [AllowAnonymous]
    [EnableRateLimiting("admission-payment")]
    public class AdmissionPaymentsController : ControllerBase
    {
        private readonly IAdmissionPaymentService _admissionPaymentService;

        public AdmissionPaymentsController(IAdmissionPaymentService admissionPaymentService)
        {
            _admissionPaymentService = admissionPaymentService;
        }

        [HttpGet("{token}")]
        public async Task<ActionResult<PublicAdmissionPaymentDto>> Get(string token, CancellationToken cancellationToken)
        {
            return Ok(await _admissionPaymentService.GetPublicPaymentAsync(token, cancellationToken));
        }

        /// <summary>Records the Terms and Conditions acceptance and returns the gateway checkout to send the parent to.</summary>
        [HttpPost("{token}/start")]
        public async Task<ActionResult<StartPublicAdmissionPaymentResultDto>> Start(
            string token,
            StartPublicAdmissionPaymentRequest request,
            CancellationToken cancellationToken)
        {
            return Ok(await _admissionPaymentService.StartPublicPaymentAsync(token, request, cancellationToken));
        }
    }
}
