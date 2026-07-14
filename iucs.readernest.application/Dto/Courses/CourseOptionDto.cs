namespace iucs.readernest.application.Dto.Courses
{
    /// <summary>Minimal course reference for selection dropdowns — safe for any signed-in role, unlike the full CourseDto (pricing/enrollment data is admin-only).</summary>
    public class CourseOptionDto
    {
        public Guid Id { get; set; }

        public string Name { get; set; } = null!;
    }
}
