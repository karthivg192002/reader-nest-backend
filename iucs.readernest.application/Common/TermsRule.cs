using iucs.readernest.domain.Entities.Billing;
using iucs.readernest.domain.Entities.Users;
using iucs.readernest.domain.Repository;
using Microsoft.EntityFrameworkCore;

namespace iucs.readernest.application.Common
{
    /// <summary>
    /// Who must accept the Terms &amp; Conditions (client, Sep 2026): only parents enrolling for the
    /// first time, before their first payment and so before dashboard access. Existing parents —
    /// anyone who has already paid the academy — are never asked, and are not recorded as having
    /// accepted either (they didn't); a new parent is asked once, and their acceptance is recorded.
    /// </summary>
    public static class TermsRule
    {
        public static async Task<bool> IsAcceptanceRequiredAsync(
            IUnitOfWork unitOfWork, ParentProfile? parent, CancellationToken cancellationToken = default)
        {
            if (parent is null)
            {
                return true; // brand-new parent whose account isn't made yet
            }

            if (parent.TermsAcceptedAtUtc is not null)
            {
                return false;
            }

            var hasPaidBefore = await unitOfWork.Repository<Invoice>().Query()
                .AnyAsync(i => i.ParentProfileId == parent.Id && i.AmountPaid > 0, cancellationToken);
            return !hasPaidBefore;
        }
    }
}
