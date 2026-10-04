using System.ComponentModel.DataAnnotations;
using iucs.readernest.domain.Entities.Academics;
using iucs.readernest.domain.Entities.Common;
using Microsoft.EntityFrameworkCore;

namespace iucs.readernest.domain.Entities.Users
{
    /// <summary>
    /// What a parent tells the centre about an EXISTING child from the portal — preferred class
    /// days and times, when to start, school and other details — so a counsellor-created child
    /// doesn't need a WhatsApp round-trip (or a second child record) to get scheduled.
    /// One row per child; saving again updates it in place.
    /// </summary>
    [Index(nameof(ChildId), IsUnique = true)]
    public class ChildSchedulePreference : AuditEntity
    {
        public Guid ChildId { get; set; }

        public Child Child { get; set; } = null!;

        /// <summary>The course the child is enrolling for, as chosen by the parent.</summary>
        public Guid? CourseId { get; set; }

        public Course? Course { get; set; }

        /// <summary>Date the parent wants classes to begin from.</summary>
        public DateOnly? PreferredStartDate { get; set; }

        public int DaysPerWeek { get; set; }

        /// <summary>Comma-separated weekday codes in week order, e.g. "Mon,Thu".</summary>
        [MaxLength(40)]
        public string PreferredDays { get; set; } = string.Empty;

        /// <summary>Preferred start time per chosen day as JSON, e.g. {"Mon":"16:00","Thu":"18:00"}.</summary>
        public string DayTimesJson { get; set; } = "{}";

        /// <summary>
        /// IANA time zone the days/times above are in -- the parent's own zone when they chose them
        /// (e.g. "Australia/Melbourne"). Null on rows saved before this existed, which were picked
        /// in the academy's zone (Asia/Kolkata). Staff views convert to IST with it.
        /// </summary>
        [MaxLength(64)]
        public string? TimeZoneId { get; set; }

        [MaxLength(200)]
        public string? SchoolName { get; set; }

        [MaxLength(1000)]
        public string? PriorExperience { get; set; }

        [MaxLength(1000)]
        public string? Allergies { get; set; }

        [MaxLength(2000)]
        public string? Notes { get; set; }

        public DateTime SubmittedAtUtc { get; set; }
    }
}
