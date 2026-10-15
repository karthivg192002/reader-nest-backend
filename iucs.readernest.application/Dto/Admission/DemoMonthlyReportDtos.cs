namespace iucs.readernest.application.Dto.Admission
{
    /// <summary>
    /// Month-end demo report (client request 2026-10-15): every demo held in the month, grouped by
    /// the admission counsellor who booked it. Downloaded as a PDF from Admission → Reports.
    /// </summary>
    public class DemoMonthlyReportDto
    {
        public int Year { get; set; }

        public int Month { get; set; }

        /// <summary>"October 2026".</summary>
        public string MonthLabel { get; set; } = null!;

        public DateTime GeneratedAtUtc { get; set; }

        public IReadOnlyList<DemoMonthlyReportCounselorDto> Counselors { get; set; } = [];
    }

    public class DemoMonthlyReportCounselorDto
    {
        /// <summary>Who booked the demos (DemoBooking.CreatedBy); "Booked online" when nobody signed in did.</summary>
        public string CounselorName { get; set; } = null!;

        public int DemoCount { get; set; }

        public int CompletedCount { get; set; }

        public int EnrolledCount { get; set; }

        public decimal TotalAmountPaid { get; set; }

        public IReadOnlyList<DemoMonthlyReportRowDto> Rows { get; set; } = [];
    }

    public class DemoMonthlyReportRowDto
    {
        public Guid BookingId { get; set; }

        public DateTime ScheduledStartAtUtc { get; set; }

        /// <summary>"08 Oct 2026, 4:00 PM" in IST.</summary>
        public string DateTimeLabel { get; set; } = null!;

        public string StudentName { get; set; } = null!;

        public string ParentName { get; set; } = null!;

        public string TeacherName { get; set; } = null!;

        /// <summary>What happened to the demo class: Completed, Teacher no-show, Cancelled, ...</summary>
        public string DemoStatus { get; set; } = null!;

        /// <summary>Where the lead stands now: Enrolled, Payment pending, Not interested, ...</summary>
        public string Outcome { get; set; } = null!;

        public bool Enrolled { get; set; }

        /// <summary>Paid so far against the lead's payment link; null when nothing has been paid.</summary>
        public decimal? AmountPaid { get; set; }
    }
}
