namespace iucs.readernest.application.Dto.Reports
{
    /// <summary>The signed-in staff member's own collections (see CollectionOwnership), by IST calendar month.</summary>
    public class MyCollectionsDto
    {
        public decimal ThisMonth { get; set; }

        public int ThisMonthPayments { get; set; }

        public decimal LastMonth { get; set; }

        /// <summary>This month's payments, newest first.</summary>
        public List<MyCollectionPaymentDto> Payments { get; set; } = [];
    }

    public class MyCollectionPaymentDto
    {
        public DateTime PaidAtUtc { get; set; }

        public decimal Amount { get; set; }

        public string InvoiceNumber { get; set; } = null!;

        public string ParentName { get; set; } = null!;

        public string? ChildName { get; set; }

        public string? Method { get; set; }
    }
}
