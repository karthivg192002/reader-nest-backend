using iucs.readernest.application.Dto.Enrollment;
using iucs.readernest.domain.Enums;

namespace iucs.readernest.application.Services
{
    public interface IEnrollmentService
    {
        /// <summary>Parent submits (or resubmits after rejection) the mandatory first-login form.</summary>
        Task<EnrollmentFormDto> SubmitAsync(Guid parentUserId, SubmitEnrollmentFormRequest request, CancellationToken cancellationToken = default);

        Task<IReadOnlyList<EnrollmentFormDto>> ListForParentUserAsync(Guid parentUserId, CancellationToken cancellationToken = default);

        Task<IReadOnlyList<EnrollmentFormDto>> ListAsync(EnrollmentFormStatus? status, CancellationToken cancellationToken = default);

        Task<EnrollmentFormDto> GetAsync(Guid id, CancellationToken cancellationToken = default);

        /// <summary>Admin edits the submitted answers before approval (approved forms are immutable).</summary>
        Task<EnrollmentFormDto> UpdateFormDataAsync(Guid id, SubmitEnrollmentFormRequest request, CancellationToken cancellationToken = default);

        /// <summary>
        /// Admin review: approval creates the Child record, links it to the form and
        /// unlocks the parent dashboard (EnrollmentFormCompleted).
        /// </summary>
        Task<EnrollmentFormDto> ReviewAsync(Guid id, ReviewEnrollmentFormRequest request, CancellationToken cancellationToken = default);

        Task<IReadOnlyList<ChildDto>> ListChildrenForParentUserAsync(Guid parentUserId, CancellationToken cancellationToken = default);

        /// <summary>Admin students directory: every enrolled child with its parent and current course resolved.</summary>
        Task<IReadOnlyList<StudentDto>> ListAllStudentsAsync(CancellationToken cancellationToken = default);

        /// <summary>Relationship Manager's special enrolment notes on a child's profile.</summary>
        Task UpdateChildNotesAsync(Guid childId, string? notes, CancellationToken cancellationToken = default);
    }
}
