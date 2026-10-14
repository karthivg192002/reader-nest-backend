namespace iucs.readernest.application.Dto.Academics
{
    /// <summary>
    /// Month-end report of classes cancelled because of teacher leave -- the figure teacher
    /// pay deductions are based on. Dates are IST calendar dates (the org's timezone).
    /// </summary>
    public class LeaveCancellationReportDto
    {
        public int Year { get; set; }

        public int Month { get; set; }

        /// <summary>When the figures were computed (the stored snapshot's time, or now for a live preview).</summary>
        public DateTime GeneratedAtUtc { get; set; }

        /// <summary>True when this is the frozen month-end snapshot; false for a live preview of a month not yet generated.</summary>
        public bool IsSnapshot { get; set; }

        public int TotalCancelledClasses { get; set; }

        public IReadOnlyList<LeaveCancellationTeacherDto> Teachers { get; set; } = Array.Empty<LeaveCancellationTeacherDto>();
    }

    public class LeaveCancellationTeacherDto
    {
        public Guid TeacherProfileId { get; set; }

        public string TeacherName { get; set; } = null!;

        /// <summary>Total classes this teacher cancelled for leave during the month.</summary>
        public int TotalCancelledClasses { get; set; }

        public IReadOnlyList<LeaveCancellationDayDto> Days { get; set; } = Array.Empty<LeaveCancellationDayDto>();
    }

    public class LeaveCancellationDayDto
    {
        public DateOnly Date { get; set; }

        /// <summary>Classes cancelled on this date.</summary>
        public int CancelledClasses { get; set; }

        public IReadOnlyList<LeaveCancellationClassDto> Classes { get; set; } = Array.Empty<LeaveCancellationClassDto>();
    }

    public class LeaveCancellationClassDto
    {
        public string BatchName { get; set; } = null!;

        /// <summary>"Group" or "Individual" (the batch's course type); a dash for a class with no batch.</summary>
        public string ClassType { get; set; } = null!;

        /// <summary>Local start time of the cancelled class, e.g. "04:00 PM".</summary>
        public string StartTime { get; set; } = null!;
    }

    public class LeaveCancellationReportMonthDto
    {
        public int Year { get; set; }

        public int Month { get; set; }

        public DateTime GeneratedAtUtc { get; set; }

        public int TotalCancelledClasses { get; set; }
    }
}
