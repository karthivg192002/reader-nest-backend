using iucs.readernest.application.Dto.Billing;

namespace iucs.readernest.application.Common.Interfaces
{
    /// <summary>
    /// Renders one invoice as a "Bill of Supply" PDF, matching the org's existing manually-made
    /// invoice template (logo, bank/GST payment info, terms, founder signature). Implemented in
    /// the API layer (needs a PDF-rendering library the application layer deliberately doesn't
    /// reference) — see InvoicePdfGenerator.
    /// </summary>
    public interface IInvoicePdfGenerator
    {
        byte[] Generate(InvoicePdfData data);
    }
}
