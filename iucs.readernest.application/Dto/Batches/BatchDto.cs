using iucs.readernest.domain.Enums;

namespace iucs.readernest.application.Dto.Batches
{
    public class BatchDto
    {
        public Guid Id { get; set; }

        public Guid CourseId { get; set; }

        public string CourseName { get; set; } = null!;

        public Guid TeacherProfileId { get; set; }

        public string TeacherName { get; set; } = null!;

        public string Name { get; set; } = null!;

        public int Capacity { get; set; }

        public int EnrolledCount { get; set; }

        public BatchStatus Status { get; set; }

        public DateOnly? StartDate { get; set; }

        public DateOnly? EndDate { get; set; }
    }
}
