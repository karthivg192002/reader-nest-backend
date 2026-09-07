using iucs.readernest.domain.Enums;

namespace iucs.readernest.application.Dto.Batches
{
    public class BatchDto
    {
        public Guid Id { get; set; }

        public Guid CourseId { get; set; }

        public string CourseName { get; set; } = null!;

        /// <summary>The course's own configured class length — unaffected by this batch's own override, if any.</summary>
        public int CourseDurationMinutes { get; set; }

        /// <summary>This batch's own class length if set (e.g. a shorter paired-batch session);
        /// null means it follows CourseDurationMinutes.</summary>
        public int? DurationMinutesOverride { get; set; }

        /// <summary>What every session in this batch is actually scheduled at: DurationMinutesOverride if set, else CourseDurationMinutes.</summary>
        public int EffectiveDurationMinutes { get; set; }

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
