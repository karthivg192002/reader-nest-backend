using iucs.readernest.application.Dto.Common;
using iucs.readernest.application.Dto.Quizzes;

namespace iucs.readernest.application.Services
{
    public interface IQuizQuestionService
    {
        /// <summary>Admin content management listing — optionally filtered by department and/or course.</summary>
        Task<IReadOnlyList<QuizQuestionDto>> ListAsync(
            Guid? departmentId, Guid? courseId, CancellationToken cancellationToken = default);

        Task<QuizQuestionDto> CreateAsync(SaveQuizQuestionRequest request, CancellationToken cancellationToken = default);

        Task<QuizQuestionDto> UpdateAsync(Guid id, SaveQuizQuestionRequest request, CancellationToken cancellationToken = default);

        Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);

        /// <summary>
        /// Resolves the live quiz set for one class session — this course's own questions
        /// first, then the department's shared (no specific course) questions, which is also
        /// the only pool a Demo session (no course) draws from. Caller must genuinely belong
        /// to the session (same check as attendance/recordings/engagement).
        /// </summary>
        Task<IReadOnlyList<QuizQuestionDto>> GetForSessionAsync(
            Guid sessionId, Guid userId, CancellationToken cancellationToken = default);

        /// <summary>Row-by-row. Columns: DepartmentName (required unless CourseName is set),
        /// CourseName (optional — narrows to one course), Prompt, Option1..Option6 (2-6 filled),
        /// CorrectOptionNumber (1-based index into the filled options).</summary>
        Task<BulkImportResult> BulkImportAsync(Stream file, string fileName, CancellationToken cancellationToken = default);

        Task<string> ExportCsvAsync(Guid? departmentId, Guid? courseId, CancellationToken cancellationToken = default);
    }
}
