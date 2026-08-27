namespace iucs.readernest.application.Common.Interfaces
{
    /// <summary>
    /// Parses an uploaded bulk-import spreadsheet (.csv or .xlsx) into plain rows keyed by
    /// column header, in file order. The header row itself is never returned as data.
    /// Implemented in the api project (BulkFileReader) since only it references the ClosedXML
    /// package — mirrors the IFileStorage/LocalFileStorage split.
    /// </summary>
    public interface IBulkFileReader
    {
        /// <summary>
        /// Each row is a header→cell-value map with a case-insensitive key comparer, so a
        /// caller can look up <c>row["email"]</c> or <c>row["Email"]</c> interchangeably.
        /// Wholly blank rows (every cell empty) are skipped. Throws DomainValidationException
        /// for an unsupported extension or an unreadable/corrupt file.
        /// </summary>
        List<Dictionary<string, string>> ReadRows(Stream content, string fileName);
    }
}
