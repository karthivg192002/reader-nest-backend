using iucs.readernest.domain.Enums;

namespace iucs.readernest.application.Dto.Communication
{
    public class EmailTemplateDto
    {
        public Guid Id { get; set; }

        public string Key { get; set; } = null!;

        public string Name { get; set; } = null!;

        public string? Description { get; set; }

        public NotificationType Category { get; set; }

        public string Subject { get; set; } = null!;

        public string HtmlBody { get; set; } = null!;

        /// <summary>Token names this template's sender supplies, e.g. ["FirstName","Email"] — insertable in the editor.</summary>
        public IReadOnlyList<string> Placeholders { get; set; } = [];

        public bool IsActive { get; set; }

        public bool IsSystem { get; set; }

        public DateTime? UpdatedAtUtc { get; set; }
    }

    /// <summary>Only content is admin-editable; Key/Category/Placeholders are fixed by the sending code path.</summary>
    public class SaveEmailTemplateRequest
    {
        public string Subject { get; set; } = null!;

        public string HtmlBody { get; set; } = null!;

        public bool IsActive { get; set; } = true;
    }

    public class PreviewEmailTemplateRequest
    {
        /// <summary>Sample values for this template's placeholders; missing tokens fall back to a "[Token]" placeholder.</summary>
        public Dictionary<string, string> SampleTokens { get; set; } = [];
    }

    public class EmailTemplatePreviewDto
    {
        public string Subject { get; set; } = null!;

        public string HtmlBody { get; set; } = null!;
    }
}
