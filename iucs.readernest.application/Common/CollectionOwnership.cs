using iucs.readernest.domain.Entities.Admission;
using iucs.readernest.domain.Entities.Billing;

namespace iucs.readernest.application.Common
{
    /// <summary>
    /// Whose collection a payment is (client requirement: each counselor's own collection,
    /// never one combined figure for the whole team). An invoice that came out of a demo belongs
    /// to the counselor who scheduled that demo -- the lead's owner, same rule as
    /// <see cref="DemoOwnershipScope"/> -- even when the parent paid online and the invoice was
    /// created by the system or by someone else. Any other invoice belongs to whoever created it.
    /// Used by the counselor's own dashboard figure, the counselor's Payment Tracking list and
    /// collection-percentage salary, so all three always agree.
    /// </summary>
    public static class CollectionOwnership
    {
        public static IQueryable<Invoice> OwnedBy(IQueryable<Invoice> invoices, IQueryable<DemoBooking> bookings, Guid userId) =>
            invoices.Where(i => bookings.Any(b => b.InvoiceId == i.Id && b.CreatedBy == userId)
                                || (!bookings.Any(b => b.InvoiceId == i.Id) && i.CreatedBy == userId));

        public static IQueryable<PaymentTransaction> OwnedBy(IQueryable<PaymentTransaction> payments, IQueryable<DemoBooking> bookings, Guid userId) =>
            payments.Where(t => bookings.Any(b => b.InvoiceId == t.InvoiceId && b.CreatedBy == userId)
                                || (!bookings.Any(b => b.InvoiceId == t.InvoiceId) && t.Invoice.CreatedBy == userId));
    }
}
