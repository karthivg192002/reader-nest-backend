using iucs.readernest.application.Dto.Batches;
using iucs.readernest.application.Dto.Courses;
using iucs.readernest.application.Dto.Sessions;
using iucs.readernest.domain.Entities.Academics;
using iucs.readernest.domain.Entities.Sessions;

namespace iucs.readernest.application.Mappings
{
    public static class AcademicMappings
    {
        public static CourseCategoryDto ToDto(this CourseCategory category)
        {
            return new CourseCategoryDto
            {
                Id = category.Id,
                Name = category.Name,
                Description = category.Description,
                Department = category.Department,
            };
        }

        public static CourseDto ToDto(this Course course)
        {
            return new CourseDto
            {
                Id = course.Id,
                CourseCategoryId = course.CourseCategoryId,
                CategoryName = course.CourseCategory?.Name ?? string.Empty,
                Name = course.Name,
                Description = course.Description,
                Type = course.Type,
                DurationMinutes = course.DurationMinutes,
                Price = course.Price,
                TotalSessions = course.TotalSessions,
                Department = course.Department,
                IsActive = course.IsActive,
            };
        }

        public static BatchDto ToDto(this Batch batch, int enrolledCount = 0)
        {
            return new BatchDto
            {
                Id = batch.Id,
                CourseId = batch.CourseId,
                CourseName = batch.Course?.Name ?? string.Empty,
                TeacherProfileId = batch.TeacherProfileId,
                TeacherName = batch.TeacherProfile?.User is { } u ? $"{u.FirstName} {u.LastName}" : string.Empty,
                Name = batch.Name,
                Capacity = batch.Capacity,
                EnrolledCount = enrolledCount,
                Status = batch.Status,
                StartDate = batch.StartDate,
                EndDate = batch.EndDate,
            };
        }

        public static ClassSessionDto ToDto(this ClassSession session)
        {
            return new ClassSessionDto
            {
                Id = session.Id,
                BatchId = session.BatchId,
                BatchName = session.Batch?.Name,
                TeacherProfileId = session.TeacherProfileId,
                TeacherName = session.TeacherProfile?.User is { } u ? $"{u.FirstName} {u.LastName}" : string.Empty,
                Type = session.Type,
                Status = session.Status,
                ScheduledStartAtUtc = session.ScheduledStartAtUtc,
                ScheduledEndAtUtc = session.ScheduledEndAtUtc,
                MeetingRoomId = session.MeetingRoomId,
                RescheduledFromSessionId = session.RescheduledFromSessionId,
                CancellationReason = session.CancellationReason,
                Summary = session.Summary,
            };
        }
    }
}
