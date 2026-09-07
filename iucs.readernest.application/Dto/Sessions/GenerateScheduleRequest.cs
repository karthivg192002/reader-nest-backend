using System.ComponentModel.DataAnnotations;

namespace iucs.readernest.application.Dto.Sessions
{
    /// <summary>
    /// Automated scheduling: generates every session of a batch's course on the
    /// selected weekdays at a fixed time, skipping holidays, until the course's
    /// TotalSessions count is reached.
    /// </summary>
    public class GenerateScheduleRequest
    {
        [Required]
        public DateOnly StartDate { get; set; }

        [Required]
        [MinLength(1)]
        public List<DayOfWeek> DaysOfWeek { get; set; } = [];

        [Required]
        public TimeOnly StartTimeUtc { get; set; }

        /// <summary>How many sessions to generate. Defaults to the course's own TotalSessions
        /// (the only behaviour before this field existed) when omitted — set explicitly for a
        /// batch continuing mid-course under this portal (e.g. migrated from another system
        /// with some sessions already delivered there), so only the sessions actually still
        /// owed get scheduled instead of the course's full count.</summary>
        [Range(1, 500)]
        public int? SessionCount { get; set; }
    }
}
