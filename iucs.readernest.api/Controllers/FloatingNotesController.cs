using System.Security.Claims;
using iucs.readernest.application.Dto.Notes;
using iucs.readernest.application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace iucs.readernest.api.Controllers
{
    /// <summary>The signed-in user's own floating notes widget, private to that user.</summary>
    [ApiController]
    [Route("api/notes")]
    [Authorize]
    public class FloatingNotesController : ControllerBase
    {
        private readonly IFloatingNoteService _notes;

        public FloatingNotesController(IFloatingNoteService notes)
        {
            _notes = notes;
        }

        [HttpGet]
        public async Task<ActionResult<IReadOnlyList<FloatingNoteDto>>> Mine(CancellationToken cancellationToken)
        {
            return Ok(await _notes.ListMineAsync(UserId(), cancellationToken));
        }

        [HttpPost]
        public async Task<ActionResult<FloatingNoteDto>> Create(SaveFloatingNoteRequest request, CancellationToken cancellationToken)
        {
            var note = await _notes.CreateAsync(UserId(), request, cancellationToken);
            return CreatedAtAction(nameof(Mine), null, note);
        }

        [HttpPut("{id:guid}")]
        public async Task<ActionResult<FloatingNoteDto>> Update(Guid id, SaveFloatingNoteRequest request, CancellationToken cancellationToken)
        {
            return Ok(await _notes.UpdateAsync(UserId(), id, request, cancellationToken));
        }

        [HttpDelete("{id:guid}")]
        public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
        {
            await _notes.DeleteAsync(UserId(), id, cancellationToken);
            return NoContent();
        }

        private Guid UserId() => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
    }
}
