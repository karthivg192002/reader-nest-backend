using iucs.readernest.application.Dto.Users;

namespace iucs.readernest.application.Services
{
    /// <summary>
    /// Hard-deletes a Parent account or a single Student (Child) record, along with every row
    /// in every other table that's actually theirs — not the soft delete <see cref="IUserService.DeleteAsync"/>
    /// / <see cref="IEnrollmentService.RemoveChildAsync"/> already do. Every removed row is
    /// snapshotted to <c>DataDeletionLog</c> first, so the data still exists there for future
    /// reference even though it's gone from its live table. Scoped to Parent/Student only — see
    /// UsersController for why Teacher/Admin/Sub Admin accounts don't get this option.
    /// </summary>
    public interface IUserDeletionService
    {
        /// <summary>Everything a hard-delete of this Student would remove, without removing
        /// anything — powers the confirmation popup.</summary>
        Task<DataDeletionPreviewDto> PreviewStudentDeletionAsync(Guid childId, CancellationToken cancellationToken = default);

        /// <summary>Everything a hard-delete of this Parent (and every one of their children)
        /// would remove, without removing anything — powers the confirmation popup.</summary>
        Task<DataDeletionPreviewDto> PreviewParentDeletionAsync(Guid parentUserId, CancellationToken cancellationToken = default);

        /// <summary>Permanently deletes this Student and every row that's actually theirs
        /// (enrollments, invoices/payments/refunds, attendance, engagement, progress reports,
        /// awards, ...). Never touches the parent account, siblings, batches or class sessions
        /// themselves — only this child's own rows in those join/history tables.</summary>
        Task DeleteStudentAsync(Guid childId, Guid deletedByUserId, CancellationToken cancellationToken = default);

        /// <summary>Permanently deletes this Parent account, every one of their children (same
        /// scope as <see cref="DeleteStudentAsync"/> per child), and every parent-level row
        /// (invoices not tied to a specific child, enrollment forms, fee suspensions, resource
        /// access grants, notifications, chat history, ...). Never touches batches or class
        /// sessions themselves.</summary>
        Task DeleteParentAsync(Guid parentUserId, Guid deletedByUserId, CancellationToken cancellationToken = default);
    }
}
