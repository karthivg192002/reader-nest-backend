using iucs.readernest.domain.Entities.Academics;
using iucs.readernest.domain.Entities.Admission;
using iucs.readernest.domain.Entities.Billing;
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
    ///
    /// Also exempt (client, 2026-10-16): any unpaid invoice of a family whose child has never been
    /// placed in a class -- e.g. a parent who only attended a demo, whose enrollment form was
    /// approved with a plan, so an invoice was raised before any class. Reported live as such
    /// parents getting "payment overdue" emails and a fee suspension. Once the child (or, for a
    /// family-level invoice, any child) is in a batch, the invoice is handled normally again.
    /// </summary>
    public static class AdmissionInvoices
    {
        public static async Task<List<Guid>> ExemptFromOverdueHandlingAsync(
            IUnitOfWork unitOfWork, CancellationToken cancellationToken = default)
        {
            var unenrolledLeads = await unitOfWork.Repository<DemoBooking>().Query()
                .Where(b => b.PaymentToken != null
                            && b.InvoiceId != null
                            && b.ConversionStatus != ConversionStatus.Enrolled)
                .Select(b => b.InvoiceId!.Value)
                .ToListAsync(cancellationToken);

            var enrollments = unitOfWork.Repository<BatchEnrollment>().Query();
            var neverInAClass = await unitOfWork.Repository<Invoice>().Query()
                .Where(i => (i.Status == InvoiceStatus.Pending || i.Status == InvoiceStatus.PartiallyPaid || i.Status == InvoiceStatus.Overdue)
                            && !enrollments.Any(e => e.Child.ParentProfileId == i.ParentProfileId
                                                     && (i.ChildId == null || e.ChildId == i.ChildId)))
                .Select(i => i.Id)
                .ToListAsync(cancellationToken);

            return unenrolledLeads.Union(neverInAClass).ToList();
        }
    }
}
