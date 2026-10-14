namespace iucs.readernest.application.Dto.Reports
{
    /// <summary>
    /// The Admin/Founder dashboard's plain-number view of one IST calendar month: demos,
    /// conversions and money collected, split per admission counselor with everything that
    /// isn't a counselor's own collection (see CollectionOwnership) under "Others".
    /// </summary>
    public class MonthlyFinanceSummaryDto
    {
        public int Year { get; set; }

        public int Month { get; set; }

        /// <summary>Demos held this month.</summary>
        public int DemosConducted { get; set; }

        /// <summary>Of this month's demos, how many have enrolled.</summary>
        public int DemosConverted { get; set; }

        public double ConversionRatePercent { get; set; }

        /// <summary>Sum of every counselor row's AmountCollected.</summary>
        public decimal CounselorCollected { get; set; }

        /// <summary>Later installments and any other collection that isn't a counselor's own.</summary>
        public decimal OthersCollected { get; set; }

        public decimal TotalCollected { get; set; }

        public List<CounselorMonthDto> Counselors { get; set; } = [];
    }

    public class CounselorMonthDto
    {
        public Guid UserId { get; set; }

        public string Name { get; set; } = null!;

        public int DemosConducted { get; set; }

        public int DemosConverted { get; set; }

        public double ConversionRatePercent { get; set; }

        public decimal AmountCollected { get; set; }
    }
}
