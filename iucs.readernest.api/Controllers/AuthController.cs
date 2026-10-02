using System.Security.Claims;
using iucs.readernest.application.Dto.Auth;
using iucs.readernest.application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace iucs.readernest.api.Controllers
{
    [ApiController]
    [Route("api/auth")]
    public class AuthController : ControllerBase
    {
        private readonly IAuthService _authService;

        public AuthController(IAuthService authService)
        {
            _authService = authService;
        }

        [HttpPost("login")]
        [AllowAnonymous]
        [EnableRateLimiting("login")]
        public async Task<ActionResult<LoginResponse>> Login(LoginRequest request, CancellationToken cancellationToken)
        {
            return Ok(await _authService.LoginAsync(request, cancellationToken));
        }

        [HttpGet("me")]
        [Authorize]
        public async Task<ActionResult<LoginResponse>> Me(CancellationToken cancellationToken)
        {
            var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            return Ok(await _authService.GetCurrentUserAsync(userId, cancellationToken));
        }

        /// <summary>
        /// 204 if the address/mobile number has an account and the reset link (or WhatsApp-only
        /// staff alert) was sent; 404 otherwise — see AuthService.RequestPinResetAsync.
        /// </summary>
        [HttpPost("forgot-pin")]
        [AllowAnonymous]
        [EnableRateLimiting("pin-reset")]
        public async Task<IActionResult> ForgotPin(ForgotPinRequest request, CancellationToken cancellationToken)
        {
            await _authService.RequestPinResetAsync(request, cancellationToken);
            return NoContent();
        }

        /// <summary>The signed-in user (any role, Admin included) changes their own PIN.</summary>
        [HttpPost("change-pin")]
        [Authorize]
        [EnableRateLimiting("pin-reset")]
        public async Task<IActionResult> ChangePin(ChangePinRequest request, CancellationToken cancellationToken)
        {
            var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            await _authService.ChangePinAsync(userId, request, cancellationToken);
            return NoContent();
        }

        [HttpPost("reset-pin")]
        [AllowAnonymous]
        [EnableRateLimiting("pin-reset")]
        public async Task<IActionResult> ResetPin(ResetPinRequest request, CancellationToken cancellationToken)
        {
            await _authService.ResetPinAsync(request, cancellationToken);
            return NoContent();
        }
    }
}
