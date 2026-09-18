using System.ComponentModel.DataAnnotations;

namespace iucs.readernest.application.Dto.Sessions
{
    /// <summary>One weekday this batch meets on, and the time it meets at. A batch doesn't have
    /// to run at the same time every day it meets — e.g. Monday/Wednesday at 5 PM but Friday at
    /// noon — so each weekday carries its own time instead of one time applying to all of them.</summary>
    public class GenerateScheduleSlot
    {
        [Required]
        public DayOfWeek DayOfWeek { get; set; }

        [Required]
        public TimeOnly StartTimeUtc { get; set; }
    }

    /// <summary>
    /// Automated scheduling: generates every session of a batch's course on the
    /// selected weekdays (each at its own time), skipping holidays, until the
    /// target session count is reached.
    /// </summary>
    public class GenerateScheduleRequest
    {
        [Required]
        public DateOnly StartDate { get; set; }

        /// <summary>One entry per weekday this batch meets on. A given DayOfWeek must appear
        /// at most once — pick whichever single time that day actually runs at.</summary>
        [Required]
        [MinLength(1)]
        public List<GenerateScheduleSlot> Slots { get; set; } = [];

        /// <summary>How many sessions to generate. Defaults to the course's own TotalSessions
        /// (the only behaviour before this field existed) when omitted — set explicitly for a
        /// batch continuing mid-course under this portal (e.g. migrated from another system
        /// with some sessions already delivered there), so only the sessions actually still
        /// owed get scheduled instead of the course's full count.</summary>
        [Range(1, 500)]
        public int? SessionCount { get; set; }
    }
}
