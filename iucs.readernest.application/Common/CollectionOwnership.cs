using iucs.readernest.domain.Entities.Admission;
using iucs.readernest.domain.Entities.Billing;
using iucs.readernest.domain.Enums;

namespace iucs.readernest.application.Common
{
    /// <summary>
    /// Whose collection a payment is (client requirement: each counselor's own collection,
    /// never one combined figure for the whole team). An invoice that came out of a demo belongs
    /// to the counselor who scheduled that demo -- the lead's owner, same rule as
    /// <see cref="DemoOwnershipScope"/> -- even when the parent paid online and the invoice was
    /// created by the system or by someone else. Any other invoice belongs to whoever created it.
    /// Used by the counselor's own dashboard figure, the counselor's Payment Tracking list,
    /// collection-percentage salary and the Admin/Founder monthly summary, so they always agree.
    /// </summary>
    public static class CollectionOwnership
    {
        public static IQueryable<Invoice> OwnedBy(IQueryable<Invoice> invoices, IQueryable<DemoBooking> bookings, Guid userId) =>
            invoices.Where(i => bookings.Any(b => b.InvoiceId == i.Id && b.CreatedBy == userId)
                                || (!bookings.Any(b => b.InvoiceId == i.Id) && i.CreatedBy == userId));

        /// <summary>
        /// The payments that count as this person's own collection. On top of the invoice rule
        /// above, only the money collected while enrolling the child counts (client requirement,
        /// it drives counselor salary): once the counselor has verified the payment and enrolled
        /// the child, every later installment on that invoice is "Others", not theirs. A lead
        /// enrolled without a verification time (the older manual path), and an invoice that
        /// didn't come from a demo, credit the first successful payment only.
        /// </summary>
        public static IQueryable<PaymentTransaction> OwnedBy(IQueryable<PaymentTransaction> payments, IQueryable<DemoBooking> bookings, Guid userId) =>
            payments.Where(t =>
                bookings.Any(b => b.InvoiceId == t.InvoiceId && b.CreatedBy == userId
                    && (b.PaymentVerifiedAtUtc != null
                        ? t.PaidAtUtc <= b.PaymentVerifiedAtUtc
                        : (b.ConversionStatus != ConversionStatus.Enrolled && b.ConversionStatus != ConversionStatus.ReadyForEnrollment)
                            || !payments.Any(o => o.InvoiceId == t.InvoiceId && o.Status == TransactionStatus.Success && o.PaidAtUtc < t.PaidAtUtc)))
                || (!bookings.Any(b => b.InvoiceId == t.InvoiceId) && t.Invoice.CreatedBy == userId
                    && !payments.Any(o => o.InvoiceId == t.InvoiceId && o.Status == TransactionStatus.Success && o.PaidAtUtc < t.PaidAtUtc)));
    }
}
