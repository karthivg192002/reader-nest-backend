using iucs.readernest.application.Common.Exceptions;
using iucs.readernest.application.Dto.Courses;
using iucs.readernest.application.Mappings;
using iucs.readernest.domain.Entities.Academics;
using iucs.readernest.domain.Enums;
using iucs.readernest.domain.Repository;
using Microsoft.EntityFrameworkCore;

namespace iucs.readernest.application.Services
{
    public class CourseService : ICourseService
    {
        private static readonly int[] AllowedDurations = [30, 45, 60];

        private readonly IUnitOfWork _unitOfWork;
        private readonly IAuditLogService _auditLog;

        public CourseService(IUnitOfWork unitOfWork, IAuditLogService auditLog)
        {
            _unitOfWork = unitOfWork;
            _auditLog = auditLog;
        }

        public async Task<IReadOnlyList<CourseCategoryDto>> ListCategoriesAsync(CancellationToken cancellationToken = default)
        {
            var categories = await _unitOfWork.Repository<CourseCategory>().Query()
                .OrderBy(c => c.Name)
                .ToListAsync(cancellationToken);

            return categories.Select(c => c.ToDto()).ToList();
        }

        public async Task<CourseCategoryDto> CreateCategoryAsync(
            CreateCourseCategoryRequest request,
            CancellationToken cancellationToken = default)
        {
            var name = request.Name.Trim();
            var repository = _unitOfWork.Repository<CourseCategory>();

            if (await repository.ExistsAsync(c => c.Name == name, cancellationToken))
            {
                throw new ConflictException($"A course category named '{name}' already exists.");
            }

            var category = new CourseCategory
            {
                Name = name,
                Description = request.Description,
                Department = request.Department,
            };
            await repository.AddAsync(category, cancellationToken);
            await _auditLog.StageAsync(AuditAction.Create, nameof(CourseCategory), category.Id.ToString(), cancellationToken: cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return category.ToDto();
        }

        public async Task<IReadOnlyList<CourseDto>> ListAsync(bool includeInactive = false, CancellationToken cancellationToken = default)
        {
            var query = _unitOfWork.Repository<Course>().Query().Include(c => c.CourseCategory).AsQueryable();
            if (!includeInactive)
            {
                query = query.Where(c => c.IsActive);
            }

            var courses = await query.OrderBy(c => c.Name).ToListAsync(cancellationToken);
            return courses.Select(c => c.ToDto()).ToList();
        }

        public async Task<CourseDto> GetAsync(Guid id, CancellationToken cancellationToken = default)
        {
            var course = await _unitOfWork.Repository<Course>().Query()
                .Include(c => c.CourseCategory)
                .FirstOrDefaultAsync(c => c.Id == id, cancellationToken)
                ?? throw new NotFoundException(nameof(Course), id);

            return course.ToDto();
        }

        public async Task<CourseDto> CreateAsync(SaveCourseRequest request, CancellationToken cancellationToken = default)
        {
            var category = await ValidateAsync(request, cancellationToken);

            var course = new Course
            {
                CourseCategoryId = request.CourseCategoryId,
                CourseCategory = category,
                Name = request.Name.Trim(),
                Description = request.Description,
                Type = request.Type,
                DurationMinutes = request.DurationMinutes,
                Price = request.Price,
                TotalSessions = request.TotalSessions,
                Department = request.Department,
                IsActive = request.IsActive,
            };
            await _unitOfWork.Repository<Course>().AddAsync(course, cancellationToken);
            await _auditLog.StageAsync(AuditAction.Create, nameof(Course), course.Id.ToString(), cancellationToken: cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return course.ToDto();
        }

        public async Task<CourseDto> UpdateAsync(Guid id, SaveCourseRequest request, CancellationToken cancellationToken = default)
        {
            var category = await ValidateAsync(request, cancellationToken);
            var course = await _unitOfWork.Repository<Course>().GetByIdAsync(id, cancellationToken)
                ?? throw new NotFoundException(nameof(Course), id);

            course.CourseCategoryId = request.CourseCategoryId;
            course.CourseCategory = category;
            course.Name = request.Name.Trim();
            course.Description = request.Description;
            course.Type = request.Type;
            course.DurationMinutes = request.DurationMinutes;
            course.Price = request.Price;
            course.TotalSessions = request.TotalSessions;
            course.Department = request.Department;
            course.IsActive = request.IsActive;

            await _auditLog.StageAsync(AuditAction.Update, nameof(Course), course.Id.ToString(), cancellationToken: cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return course.ToDto();
        }

        private async Task<CourseCategory> ValidateAsync(SaveCourseRequest request, CancellationToken cancellationToken)
        {
            if (!AllowedDurations.Contains(request.DurationMinutes))
            {
                throw new DomainValidationException("Class duration must be 30, 45 or 60 minutes.");
            }

            return await _unitOfWork.Repository<CourseCategory>().GetByIdAsync(request.CourseCategoryId, cancellationToken)
                ?? throw new NotFoundException(nameof(CourseCategory), request.CourseCategoryId);
        }
    }
}
