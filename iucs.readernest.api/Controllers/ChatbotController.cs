using System.Security.Claims;
using iucs.readernest.api.Auth;
using iucs.readernest.application.Dto.Communication;
using iucs.readernest.application.Services;
using iucs.readernest.domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace iucs.readernest.api.Controllers
{
    /// <summary>
    /// The "Ask a Doubt" chatbot: every signed-in role can ask and browse their own history
    /// (mirrors FloatingNotesController's self-service shape); FAQ content management and
    /// teacher escalation triage are gated separately below.
    /// </summary>
    [ApiController]
    [Route("api/chatbot")]
    [Authorize]
    public class ChatbotController : ControllerBase
    {
        private readonly IChatbotService _chatbot;

        public ChatbotController(IChatbotService chatbot)
        {
            _chatbot = chatbot;
        }

        [HttpGet("faqs")]
        public async Task<ActionResult<IReadOnlyList<ChatFaqDto>>> ActiveFaqs(CancellationToken cancellationToken)
        {
            return Ok(await _chatbot.ListActiveFaqsAsync(cancellationToken));
        }

        [HttpPost("ask")]
        public async Task<ActionResult<AskChatbotResponse>> Ask(AskChatbotRequest request, CancellationToken cancellationToken)
        {
            return Ok(await _chatbot.AskAsync(UserId(), request, cancellationToken));
        }

        [HttpGet("history")]
        public async Task<ActionResult<IReadOnlyList<ChatMessageDto>>> History(CancellationToken cancellationToken)
        {
            return Ok(await _chatbot.ListMyMessagesAsync(UserId(), cancellationToken));
        }

        [HttpPut("messages/{id:guid}/feedback")]
        public async Task<ActionResult<ChatMessageDto>> SubmitFeedback(Guid id, SubmitChatFeedbackRequest request, CancellationToken cancellationToken)
        {
            return Ok(await _chatbot.SubmitFeedbackAsync(UserId(), id, request, cancellationToken));
        }

        // FAQ content management — same audience as Email Templates/Progress Reports.
        [HttpGet("admin/faqs")]
        [Authorize(Roles = $"{nameof(UserRole.Admin)},{nameof(UserRole.SubAdmin)}")]
        [HasPermission(PermissionModule.Communication, PermissionAction.View)]
        public async Task<ActionResult<IReadOnlyList<ChatFaqDto>>> AllFaqs(CancellationToken cancellationToken)
        {
            return Ok(await _chatbot.ListAllFaqsAsync(cancellationToken));
        }

        [HttpPost("admin/faqs")]
        [Authorize(Roles = $"{nameof(UserRole.Admin)},{nameof(UserRole.SubAdmin)}")]
        [HasPermission(PermissionModule.Communication, PermissionAction.Create)]
        public async Task<ActionResult<ChatFaqDto>> CreateFaq(SaveChatFaqRequest request, CancellationToken cancellationToken)
        {
            var faq = await _chatbot.CreateFaqAsync(request, cancellationToken);
            return CreatedAtAction(nameof(AllFaqs), null, faq);
        }

        [HttpPut("admin/faqs/{id:guid}")]
        [Authorize(Roles = $"{nameof(UserRole.Admin)},{nameof(UserRole.SubAdmin)}")]
        [HasPermission(PermissionModule.Communication, PermissionAction.Edit)]
        public async Task<ActionResult<ChatFaqDto>> UpdateFaq(Guid id, SaveChatFaqRequest request, CancellationToken cancellationToken)
        {
            return Ok(await _chatbot.UpdateFaqAsync(id, request, cancellationToken));
        }

        [HttpDelete("admin/faqs/{id:guid}")]
        [Authorize(Roles = $"{nameof(UserRole.Admin)},{nameof(UserRole.SubAdmin)}")]
        [HasPermission(PermissionModule.Communication, PermissionAction.Delete)]
        public async Task<IActionResult> DeleteFaq(Guid id, CancellationToken cancellationToken)
        {
            await _chatbot.DeleteFaqAsync(id, cancellationToken);
            return NoContent();
        }

        // Escalation triage — Admin/SubAdmin plus Teacher, who the doubt actually gets routed to.
        [HttpGet("escalations")]
        [Authorize(Roles = $"{nameof(UserRole.Admin)},{nameof(UserRole.SubAdmin)},{nameof(UserRole.Teacher)}")]
        [HasPermission(PermissionModule.Communication, PermissionAction.View)]
        public async Task<ActionResult<IReadOnlyList<ChatEscalationDto>>> Escalations(
            [FromQuery] ChatEscalationStatus? status,
            CancellationToken cancellationToken)
        {
            return Ok(await _chatbot.ListEscalationsAsync(status, cancellationToken));
        }

        [HttpPut("escalations/{id:guid}/resolve")]
        [Authorize(Roles = $"{nameof(UserRole.Admin)},{nameof(UserRole.SubAdmin)},{nameof(UserRole.Teacher)}")]
        [HasPermission(PermissionModule.Communication, PermissionAction.Edit)]
        public async Task<ActionResult<ChatEscalationDto>> ResolveEscalation(
            Guid id,
            ResolveChatEscalationRequest request,
            CancellationToken cancellationToken)
        {
            return Ok(await _chatbot.ResolveEscalationAsync(id, UserId(), request, cancellationToken));
        }

        // Usage analytics — admin-only.
        [HttpGet("usage-stats")]
        [Authorize(Roles = $"{nameof(UserRole.Admin)},{nameof(UserRole.SubAdmin)}")]
        [HasPermission(PermissionModule.Communication, PermissionAction.View)]
        public async Task<ActionResult<ChatbotUsageStatsDto>> UsageStats(CancellationToken cancellationToken)
        {
            return Ok(await _chatbot.GetUsageStatsAsync(cancellationToken));
        }

        private Guid UserId() => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
    }
}
