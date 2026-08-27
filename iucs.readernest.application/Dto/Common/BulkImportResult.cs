namespace iucs.readernest.application.Dto.Common
{
    public class BulkImportRowError
    {
        /// <summary>1-based, counting the header row as row 1 — matches what the admin sees
        /// when they open the same file in a spreadsheet.</summary>
        public int RowNumber { get; set; }

        public string Message { get; set; } = null!;
    }

    /// <summary>
    /// Outcome of a bulk-import upload. Rows are processed independently (one bad row never
    /// aborts the rest — same continue-on-error spirit as the admin's existing bulk
    /// "Resend credentials" action on the Users screen), so this always comes back 200 with a
    /// per-row breakdown rather than throwing on partial failure.
    /// </summary>
    public class BulkImportResult
    {
        public int TotalRows { get; set; }

        public int SucceededCount { get; set; }

        public int FailedCount { get; set; }

        public List<BulkImportRowError> Errors { get; set; } = [];
    }
}
