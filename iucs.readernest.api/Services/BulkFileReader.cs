using System.Text;
using ClosedXML.Excel;
using iucs.readernest.application.Common.Exceptions;
using iucs.readernest.application.Common.Interfaces;

namespace iucs.readernest.api.Services
{
    /// <summary>See IBulkFileReader. ClosedXML (pure .NET, no Excel install needed) handles
    /// .xlsx; .csv is hand-rolled since no CSV library is referenced anywhere in the solution
    /// and the format is simple enough not to need one.</summary>
    public class BulkFileReader : IBulkFileReader
    {
        public List<Dictionary<string, string>> ReadRows(Stream content, string fileName)
        {
            var extension = Path.GetExtension(fileName).ToLowerInvariant();
            return extension switch
            {
                ".xlsx" => ReadXlsx(content),
                ".csv" => ReadCsv(content),
                _ => throw new DomainValidationException("Only .csv and .xlsx files are supported."),
            };
        }

        private static List<Dictionary<string, string>> ReadXlsx(Stream content)
        {
            IXLWorksheet sheet;
            try
            {
                using var workbook = new XLWorkbook(content);
                sheet = workbook.Worksheets.FirstOrDefault()
                    ?? throw new DomainValidationException("The workbook has no sheets.");
                return ReadSheet(sheet);
            }
            catch (Exception ex) when (ex is not AppException)
            {
                throw new DomainValidationException($"Could not read the Excel file: {ex.Message}");
            }
        }

        private static List<Dictionary<string, string>> ReadSheet(IXLWorksheet sheet)
        {
            var usedRange = sheet.RangeUsed();
            var rows = usedRange?.RowsUsed().ToList() ?? [];
            if (rows.Count == 0)
            {
                return [];
            }

            var headerRow = rows[0];
            var columnCount = headerRow.CellsUsed().Count();
            var headers = headerRow.Cells(1, columnCount).Select(c => c.GetString().Trim()).ToList();

            var result = new List<Dictionary<string, string>>();
            for (var i = 1; i < rows.Count; i++)
            {
                var cells = rows[i].Cells(1, headers.Count).ToList();
                if (cells.All(c => string.IsNullOrWhiteSpace(c.GetString())))
                {
                    continue; // wholly blank row (trailing spreadsheet padding, etc.)
                }

                var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                for (var col = 0; col < headers.Count; col++)
                {
                    if (string.IsNullOrEmpty(headers[col]))
                    {
                        continue;
                    }

                    dict[headers[col]] = col < cells.Count ? cells[col].GetString().Trim() : string.Empty;
                }

                result.Add(dict);
            }

            return result;
        }

        private static List<Dictionary<string, string>> ReadCsv(Stream content)
        {
            using var reader = new StreamReader(content, Encoding.UTF8);
            var lines = ReadLogicalLines(reader);
            if (lines.Count == 0)
            {
                return [];
            }

            var headers = SplitCsvLine(lines[0]).Select(h => h.Trim()).ToList();
            var result = new List<Dictionary<string, string>>();
            for (var i = 1; i < lines.Count; i++)
            {
                if (string.IsNullOrWhiteSpace(lines[i]))
                {
                    continue;
                }

                var cells = SplitCsvLine(lines[i]);
                var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                for (var col = 0; col < headers.Count; col++)
                {
                    if (string.IsNullOrEmpty(headers[col]))
                    {
                        continue;
                    }

                    dict[headers[col]] = col < cells.Count ? cells[col].Trim() : string.Empty;
                }

                result.Add(dict);
            }

            return result;
        }

        /// <summary>Re-joins physical lines that were split in the middle of a quoted field
        /// (a cell containing an embedded newline) before splitting into cells — a naive
        /// line-by-line read would otherwise treat that one logical row as two broken ones.</summary>
        private static List<string> ReadLogicalLines(StreamReader reader)
        {
            var lines = new List<string>();
            var buffer = new StringBuilder();
            string? raw;
            while ((raw = reader.ReadLine()) is not null)
            {
                if (buffer.Length > 0)
                {
                    buffer.Append('\n');
                }

                buffer.Append(raw);
                if (buffer.ToString().Count(c => c == '"') % 2 != 0)
                {
                    continue; // inside a quoted field that spans this line break
                }

                lines.Add(buffer.ToString());
                buffer.Clear();
            }

            if (buffer.Length > 0)
            {
                lines.Add(buffer.ToString());
            }

            return lines;
        }

        /// <summary>RFC4180-ish split: quoted fields, "" as an escaped quote, commas inside quotes.</summary>
        private static List<string> SplitCsvLine(string line)
        {
            var fields = new List<string>();
            var field = new StringBuilder();
            var inQuotes = false;
            for (var i = 0; i < line.Length; i++)
            {
                var c = line[i];
                if (inQuotes)
                {
                    if (c == '"')
                    {
                        if (i + 1 < line.Length && line[i + 1] == '"')
                        {
                            field.Append('"');
                            i++;
                        }
                        else
                        {
                            inQuotes = false;
                        }
                    }
                    else
                    {
                        field.Append(c);
                    }
                }
                else if (c == '"')
                {
                    inQuotes = true;
                }
                else if (c == ',')
                {
                    fields.Add(field.ToString());
                    field.Clear();
                }
                else if (c != '\r')
                {
                    field.Append(c);
                }
            }

            fields.Add(field.ToString());
            return fields;
        }
    }
}
