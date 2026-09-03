using iucs.readernest.domain.Entities.Billing;
using iucs.readernest.domain.Enums;
using iucs.readernest.domain.Repository;

namespace iucs.readernest.application.Common
{
    /// <summary>
    /// Shared "is this child's access currently blocked by an unpaid fee" check, used
    /// everywhere a suspension gates something a specific child does (join a live session,
    /// view a resource/recording). A FeeSuspension with ChildId set blocks only that child;
    /// one with ChildId null (the triggering invoice wasn't tied to a specific child) blocks
    /// every child on the parent account -- see FeeSuspension's own doc comment.
    /// </summary>
    public static class SuspensionCheck
    {
        public static Task<bool> IsChildBlockedAsync(
            IUnitOfWork unitOfWork, Guid parentProfileId, Guid childId, CancellationToken cancellationToken = default)
        {
            return unitOfWork.Repository<FeeSuspension>().ExistsAsync(
                s => s.ParentProfileId == parentProfileId
                    && s.Status == SuspensionStatus.Active
                    && (s.ChildId == null || s.ChildId == childId),
                cancellationToken);
        }

        /// <summary>For contexts with no specific child (a family-level check) -- true only for an account-wide suspension.</summary>
        public static Task<bool> IsAccountBlockedAsync(
            IUnitOfWork unitOfWork, Guid parentProfileId, CancellationToken cancellationToken = default)
        {
            return unitOfWork.Repository<FeeSuspension>().ExistsAsync(
                s => s.ParentProfileId == parentProfileId && s.Status == SuspensionStatus.Active && s.ChildId == null,
                cancellationToken);
        }
    }
}
