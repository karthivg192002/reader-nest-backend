namespace iucs.readernest.application.Dto.Users
{
    /// <summary>Lightweight teacher entry for assignment dropdowns (batches, sessions, demos).</summary>
    public class TeacherOptionDto
    {
        public Guid TeacherProfileId { get; set; }

        public Guid UserId { get; set; }

        public string FullName { get; set; } = null!;

        public Guid? DepartmentId { get; set; }

        public string? DepartmentName { get; set; }
    }
}
