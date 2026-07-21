using iucs.readernest.application.Dto.Communication;

namespace iucs.readernest.application.Services
{
    public interface IEmailTemplateService
    {
        Task<IReadOnlyList<EmailTemplateDto>> ListAsync(CancellationToken cancellationToken = default);

        Task<EmailTemplateDto> GetAsync(Guid id, CancellationToken cancellationToken = default);

        /// <summary>Admin edits Subject/HtmlBody/IsActive only — Key/Category/Placeholders are fixed by the sender.</summary>
        Task<EmailTemplateDto> UpdateAsync(Guid id, SaveEmailTemplateRequest request, CancellationToken cancellationToken = default);

        /// <summary>Renders with the admin's saved draft (not the persisted row) for the live preview pane.</summary>
        Task<EmailTemplatePreviewDto> PreviewAsync(Guid id, PreviewEmailTemplateRequest request, CancellationToken cancellationToken = default);

        /// <summary>
        /// Resolves the active template for <paramref name="key"/> and substitutes {{Token}} placeholders.
        /// Falls back to a minimal generic message if the template is missing/inactive so a send never hard-fails.
        /// </summary>
        Task<(string Subject, string HtmlBody)> RenderAsync(
            string key,
            IReadOnlyDictionary<string, string> tokens,
            CancellationToken cancellationToken = default);
    }
}
