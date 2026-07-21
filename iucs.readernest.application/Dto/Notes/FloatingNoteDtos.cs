namespace iucs.readernest.application.Dto.Notes
{
    public class FloatingNoteDto
    {
        public Guid Id { get; set; }

        public string Content { get; set; } = string.Empty;

        public string? Color { get; set; }

        public int SortOrder { get; set; }

        public DateTime CreatedAtUtc { get; set; }

        public DateTime? UpdatedAtUtc { get; set; }
    }

    public class SaveFloatingNoteRequest
    {
        public string Content { get; set; } = string.Empty;

        public string? Color { get; set; }

        public int SortOrder { get; set; }
    }
}
