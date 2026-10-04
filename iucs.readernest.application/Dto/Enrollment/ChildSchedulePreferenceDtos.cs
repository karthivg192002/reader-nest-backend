using System.ComponentModel.DataAnnotations;
using iucs.readernest.domain.Enums;

namespace iucs.readernest.application.Dto.Enrollment
{
    /// <summary>
    /// A child's details + preferred schedule as the parent entered them. For a child who hasn't
    /// filled it in yet, <see cref="HasSubmitted"/> is false and the basics (name, DOB, grade) are
    /// pre-filled from the child record so the form never asks for what the centre already has.
    /// </summary>
    public class ChildSchedulePreferenceDto
    {
        public Guid ChildId { get; set; }

        public string ChildName { get; set; } = null!;

        public DateOnly? DateOfBirth { get; set; }

        public Gender? Gender { get; set; }

        public string? Grade { get; set; }

        public Guid? CourseId { get; set; }

        public string? CourseName { get; set; }

        public DateOnly? PreferredStartDate { get; set; }

        public int? DaysPerWeek { get; set; }

        /// <summary>Weekday codes in week order (Mon..Sun).</summary>
        public List<string> PreferredDays { get; set; } = [];

        /// <summary>Preferred start time per day, "HH:mm" 24-hour.</summary>
        public Dictionary<string, string> DayTimes { get; set; } = [];

        /// <summary>IANA zone the day times are in (the parent's own); null = the academy's zone (Asia/Kolkata).</summary>
        public string? TimeZoneId { get; set; }

        public string? SchoolName { get; set; }

        public string? PriorExperience { get; set; }

        public string? Allergies { get; set; }

        public string? Notes { get; set; }

        public bool HasSubmitted { get; set; }

        public DateTime? SubmittedAtUtc { get; set; }
    }

    public class SaveChildSchedulePreferenceRequest
    {
        public Guid? CourseId { get; set; }

        public DateOnly? PreferredStartDate { get; set; }

        [Range(1, 7)]
        public int DaysPerWeek { get; set; }

        [Required]
        public List<string> PreferredDays { get; set; } = [];

        /// <summary>One "HH:mm" time for every chosen day.</summary>
        [Required]
        public Dictionary<string, string> DayTimes { get; set; } = [];

        /// <summary>IANA zone the parent picked the times in; defaults to the parent account's zone.</summary>
        [MaxLength(64)]
        public string? TimeZoneId { get; set; }

        [MaxLength(200)]
        public string? SchoolName { get; set; }

        [MaxLength(50)]
        public string? Grade { get; set; }

        public DateOnly? DateOfBirth { get; set; }

        public Gender? Gender { get; set; }

        [MaxLength(1000)]
        public string? PriorExperience { get; set; }

        [MaxLength(1000)]
        public string? Allergies { get; set; }

        [MaxLength(2000)]
        public string? Notes { get; set; }
    }
}
