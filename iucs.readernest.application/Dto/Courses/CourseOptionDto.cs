using iucs.readernest.domain.Enums;

namespace iucs.readernest.application.Dto.Courses
{
    /// <summary>Minimal course reference for selection dropdowns — safe for any signed-in role, unlike the full CourseDto (pricing/enrollment data is admin-only).</summary>
    public class CourseOptionDto
    {
        public Guid Id { get; set; }

        public string Name { get; set; } = null!;

        /// <summary>
        /// Individual (1:1) courses always run with a single-seat batch (BatchService
        /// forces Capacity to 1 regardless of what's requested) — callers that let an
        /// admin type a batch capacity need this to explain that up front instead of
        /// silently overriding their input.
        /// </summary>
        public CourseType Type { get; set; }
    }
}
