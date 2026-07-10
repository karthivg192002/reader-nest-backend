using iucs.readernest.application.Dto.Courses;

namespace iucs.readernest.application.Services
{
    public interface ICourseService
    {
        Task<IReadOnlyList<CourseCategoryDto>> ListCategoriesAsync(CancellationToken cancellationToken = default);

        Task<CourseCategoryDto> CreateCategoryAsync(CreateCourseCategoryRequest request, CancellationToken cancellationToken = default);

        Task<IReadOnlyList<CourseDto>> ListAsync(bool includeInactive = false, CancellationToken cancellationToken = default);

        Task<CourseDto> GetAsync(Guid id, CancellationToken cancellationToken = default);

        Task<CourseDto> CreateAsync(SaveCourseRequest request, CancellationToken cancellationToken = default);

        Task<CourseDto> UpdateAsync(Guid id, SaveCourseRequest request, CancellationToken cancellationToken = default);
    }
}
