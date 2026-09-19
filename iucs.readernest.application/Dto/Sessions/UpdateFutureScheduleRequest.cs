using System.ComponentModel.DataAnnotations;

namespace iucs.readernest.application.Dto.Sessions
{
    /// <summary>
    /// Edits a batch's schedule going forward without touching sessions already
    /// completed/in progress or re-entering the whole plan from scratch (the "Manage" dialog
    /// previously offered nothing between re-typing everything via GenerateScheduleAsync — which
    /// refuses to run once any session exists — and cancelling/rescheduling every remaining
    /// session one at a time). <see cref="Slots"/> is the full intended weekly pattern from now
    /// on, not a diff: pass every weekday the batch should meet on, each with its own time,
    /// exactly like GenerateScheduleRequest.Slots.
    /// </summary>
    public class UpdateFutureScheduleRequest
    {
        /// <summary>One entry per weekday this batch should meet on going forward. A given
        /// DayOfWeek must appear at most once. When this is the same set of weekdays the batch's
        /// remaining sessions already run on, only their time-of-day changes (dates untouched);
        /// otherwise the remaining sessions are cancelled and re-placed on the new weekdays.</summary>
        [Required]
        [MinLength(1)]
        public List<GenerateScheduleSlot> Slots { get; set; } = [];

        /// <summary>How many sessions should remain from now on. Defaults to however many
        /// not-yet-happened sessions the batch currently has, so a plain time/day tweak never
        /// has to also know or re-type the remaining count.</summary>
        [Range(1, 500)]
        public int? RemainingSessionCount { get; set; }
    }
}
