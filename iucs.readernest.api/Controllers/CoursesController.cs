using iucs.readernest.api.Auth;
using iucs.readernest.application.Dto.Courses;
using iucs.readernest.application.Services;
using iucs.readernest.domain.Enums;
using Microsoft.AspNetCore.Mvc;

namespace iucs.readernest.api.Controllers
{
    [ApiController]
    [Route("api/courses")]
    public class CoursesController : ControllerBase
    {
        private readonly ICourseService _courseService;

        public CoursesController(ICourseService courseService)
        {
            _courseService = courseService;
        }

        [HttpGet("categories")]
        [HasPermission(PermissionModule.CourseBatchManagement, PermissionAction.View)]
        public async Task<ActionResult<IReadOnlyList<CourseCategoryDto>>> ListCategories(CancellationToken cancellationToken)
        {
            return Ok(await _courseService.ListCategoriesAsync(cancellationToken));
        }

        [HttpPost("categories")]
        [HasPermission(PermissionModule.CourseBatchManagement, PermissionAction.Create)]
        public async Task<ActionResult<CourseCategoryDto>> CreateCategory(
            CreateCourseCategoryRequest request,
            CancellationToken cancellationToken)
        {
            var category = await _courseService.CreateCategoryAsync(request, cancellationToken);
            return CreatedAtAction(nameof(ListCategories), null, category);
        }

        [HttpGet]
        [HasPermission(PermissionModule.CourseBatchManagement, PermissionAction.View)]
        public async Task<ActionResult<IReadOnlyList<CourseDto>>> List(
            [FromQuery] bool includeInactive = false,
            CancellationToken cancellationToken = default)
        {
            return Ok(await _courseService.ListAsync(includeInactive, cancellationToken));
        }

        [HttpGet("{id:guid}")]
        [HasPermission(PermissionModule.CourseBatchManagement, PermissionAction.View)]
        public async Task<ActionResult<CourseDto>> Get(Guid id, CancellationToken cancellationToken)
        {
            return Ok(await _courseService.GetAsync(id, cancellationToken));
        }

        [HttpPost]
        [HasPermission(PermissionModule.CourseBatchManagement, PermissionAction.Create)]
        public async Task<ActionResult<CourseDto>> Create(SaveCourseRequest request, CancellationToken cancellationToken)
        {
            var course = await _courseService.CreateAsync(request, cancellationToken);
            return CreatedAtAction(nameof(Get), new { id = course.Id }, course);
        }

        [HttpPut("{id:guid}")]
        [HasPermission(PermissionModule.CourseBatchManagement, PermissionAction.Edit)]
        public async Task<ActionResult<CourseDto>> Update(Guid id, SaveCourseRequest request, CancellationToken cancellationToken)
        {
            return Ok(await _courseService.UpdateAsync(id, request, cancellationToken));
        }
    }
}
