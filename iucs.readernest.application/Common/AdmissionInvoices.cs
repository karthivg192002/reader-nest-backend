using iucs.readernest.domain.Entities.Admission;
using iucs.readernest.domain.Enums;
using iucs.readernest.domain.Repository;
using Microsoft.EntityFrameworkCore;

namespace iucs.readernest.application.Common
{
    /// <summary>
    /// Invoices issued through an admission payment link (AdmissionPaymentService) belong to a lead,
    /// not yet to an enrolled child: they are family-level (no ChildId) until the RM approves the
    /// parent's enrollment form and the invoice is attached to the child. Left alone, an unpaid one
    /// would age into Overdue and the fee-suspension sweep -- which treats a family-level invoice as
    /// blocking every child on the account -- could suspend a sibling's classes over a lead that never
    /// paid, and the parent would be sent payment reminders before ever being enrolled. These invoices
    /// are exempt from the overdue sweep, reminders and suspension until the lead is Enrolled.
    /// </summary>
    public static class AdmissionInvoices
    {
        public static async Task<List<Guid>> ExemptFromOverdueHandlingAsync(
            IUnitOfWork unitOfWork, CancellationToken cancellationToken = default)
        {
            return await unitOfWork.Repository<DemoBooking>().Query()
                .Where(b => b.PaymentToken != null
                            && b.InvoiceId != null
                            && b.ConversionStatus != ConversionStatus.Enrolled)
                .Select(b => b.InvoiceId!.Value)
                .ToListAsync(cancellationToken);
        }
    }
}
