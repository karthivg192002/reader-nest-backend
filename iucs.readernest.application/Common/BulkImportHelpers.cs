namespace iucs.readernest.application.Common
{
    /// <summary>Small shared conveniences for the per-entity BulkImportAsync row loops.</summary>
    public static class BulkImportHelpers
    {
        /// <summary>Trimmed value, or null when missing/blank — every row dictionary from
        /// IBulkFileReader is already keyed case-insensitively, so callers can just name the
        /// column as it appears on the template (e.g. row.GetOrNull("Email")).</summary>
        public static string? GetOrNull(this Dictionary<string, string> row, string key)
        {
            return row.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value) ? value.Trim() : null;
        }

        /// <summary>Accepts the common spreadsheet spellings of "yes" (true/1/yes/y/active),
        /// case-insensitive; anything else — including a blank cell — falls back to
        /// <paramref name="defaultValue"/> rather than being treated as an error.</summary>
        public static bool GetBool(this Dictionary<string, string> row, string key, bool defaultValue = true)
        {
            var value = row.GetOrNull(key);
            if (value is null)
            {
                return defaultValue;
            }

            return value.Trim().ToLowerInvariant() switch
            {
                "true" or "1" or "yes" or "y" or "active" => true,
                "false" or "0" or "no" or "n" or "inactive" => false,
                _ => defaultValue,
            };
        }
    }
}
