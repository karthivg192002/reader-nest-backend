using System.Text;

namespace iucs.readernest.application.Common
{
    /// <summary>
    /// Shared CSV-building helper for the bulk-export endpoints (Users/Students/Departments/
    /// Courses/Package Plans/Quiz Questions). Deliberately separate from ReportsService's own
    /// private Escape/StringBuilder pair rather than reusing it — same shape, but keeping the
    /// two independent avoids any risk of a shared-code change ever touching the already-working
    /// Reports screen's CSV exports.
    /// </summary>
    public static class CsvWriter
    {
        public static string BuildCsv(IReadOnlyList<string> headers, IEnumerable<IReadOnlyList<string?>> rows)
        {
            var csv = new StringBuilder();
            csv.AppendLine(string.Join(',', headers.Select(Escape)));
            foreach (var row in rows)
            {
                csv.AppendLine(string.Join(',', row.Select(Escape)));
            }

            return csv.ToString();
        }

        /// <summary>Quotes a cell when it contains a comma/quote/newline, and neutralizes
        /// spreadsheet formula injection (a leading =, +, -, or @) by prefixing an apostrophe —
        /// same guard as ReportsService's own Escape.</summary>
        public static string Escape(string? value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            var v = value;
            if ("=+-@".IndexOf(v[0]) >= 0)
            {
                v = "'" + v;
            }

            if (v.IndexOfAny([',', '"', '\n', '\r']) >= 0)
            {
                v = "\"" + v.Replace("\"", "\"\"") + "\"";
            }

            return v;
        }
    }
}
