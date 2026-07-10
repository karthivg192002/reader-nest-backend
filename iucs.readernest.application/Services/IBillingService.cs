using iucs.readernest.application.Dto.Billing;
using iucs.readernest.domain.Enums;

namespace iucs.readernest.application.Services
{
    public interface IBillingService
    {
        Task<IReadOnlyList<PackagePlanDto>> ListPlansAsync(CancellationToken cancellationToken = default);

        Task<PackagePlanDto> CreatePlanAsync(SavePackagePlanRequest request, CancellationToken cancellationToken = default);

        Task<PackagePlanDto> UpdatePlanAsync(Guid id, SavePackagePlanRequest request, CancellationToken cancellationToken = default);

        Task<IReadOnlyList<InvoiceDto>> ListInvoicesAsync(
            InvoiceStatus? status,
            Guid? parentProfileId,
            CancellationToken cancellationToken = default);

        Task<InvoiceDto> CreateInvoiceAsync(CreateInvoiceRequest request, CancellationToken cancellationToken = default);

        /// <summary>
        /// Records a successful payment against an invoice (manual entry now;
        /// gateway webhooks call the same path once accounts are provisioned)
        /// and generates the receipt.
        /// </summary>
        Task<InvoiceDto> RecordPaymentAsync(Guid invoiceId, RecordPaymentRequest request, CancellationToken cancellationToken = default);
    }
}
