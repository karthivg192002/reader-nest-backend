namespace iucs.readernest.application.Dto.Users
{
    /// <summary>One row in a hard-delete preview: one table, how many of its rows would be
    /// permanently removed.</summary>
    public class DataDeletionPreviewItemDto
    {
        /// <summary>The actual table/entity name (e.g. "Invoice") — matches DataDeletionLog.TableName,
        /// so a row here can be traced to its logged snapshots after the fact.</summary>
        public string Table { get; set; } = null!;

        /// <summary>Human-readable label for the confirmation popup (e.g. "Invoices").</summary>
        public string Label { get; set; } = null!;

        public int Count { get; set; }
    }

    /// <summary>Everything a Parent/Student hard-delete confirmation popup needs to show before
    /// the admin commits to it.</summary>
    public class DataDeletionPreviewDto
    {
        /// <summary>The account/record's display name, for the popup's heading.</summary>
        public string TargetName { get; set; } = null!;

        public IReadOnlyList<DataDeletionPreviewItemDto> Items { get; set; } = Array.Empty<DataDeletionPreviewItemDto>();

        /// <summary>Sum of every item's Count — the popup's headline number.</summary>
        public int TotalRecords => Items.Sum(i => i.Count);
    }
}
