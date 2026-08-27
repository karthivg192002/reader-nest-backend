using iucs.readernest.application.Dto.Common;
using iucs.readernest.application.Dto.Courses;

namespace iucs.readernest.application.Services
{
    public interface ICourseService
    {
        Task<IReadOnlyList<CourseCategoryDto>> ListCategoriesAsync(CancellationToken cancellationToken = default);

        Task<CourseCategoryDto> CreateCategoryAsync(CreateCourseCategoryRequest request, CancellationToken cancellationToken = default);

        Task<IReadOnlyList<CourseDto>> ListAsync(bool includeInactive = false, CancellationToken cancellationToken = default);

        /// <summary>Active courses as id/name pairs, for any authenticated role that just needs to pick one (e.g. a teacher recommending a course after a demo).</summary>
        Task<IReadOnlyList<CourseOptionDto>> ListOptionsAsync(CancellationToken cancellationToken = default);

        Task<CourseDto> GetAsync(Guid id, CancellationToken cancellationToken = default);

        Task<CourseDto> CreateAsync(SaveCourseRequest request, CancellationToken cancellationToken = default);

        Task<CourseDto> UpdateAsync(Guid id, SaveCourseRequest request, CancellationToken cancellationToken = default);

        /// <summary>Row-by-row. Columns: DepartmentName, CategoryName, Name, Type (Individual/Group),
        /// DurationMinutes, Price, TotalSessions, IsActive. DepartmentName must match an existing
        /// department; CategoryName is matched within that department or created if new.</summary>
        Task<BulkImportResult> BulkImportAsync(Stream file, string fileName, CancellationToken cancellationToken = default);

        Task<string> ExportCsvAsync(bool includeInactive, CancellationToken cancellationToken = default);
    }
}
