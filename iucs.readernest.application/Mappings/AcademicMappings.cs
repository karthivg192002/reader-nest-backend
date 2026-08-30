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
                DepartmentId = category.DepartmentId,
                DepartmentName = category.Department?.Name ?? string.Empty,
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
                DepartmentId = course.DepartmentId,
                DepartmentName = course.Department?.Name ?? string.Empty,
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
                CourseDurationMinutes = batch.Course?.DurationMinutes ?? 0,
                TeacherProfileId = batch.TeacherProfileId,
                TeacherName = batch.TeacherProfile?.User is { } u ? $"{u.FirstName} {u.LastName}".Trim() : string.Empty,
                Name = batch.Name,
                Capacity = batch.Capacity,
                EnrolledCount = enrolledCount,
                Status = batch.Status,
                StartDate = batch.StartDate,
                EndDate = batch.EndDate,
            };
        }

        public static BatchStudentDto ToDto(this BatchEnrollment enrollment)
        {
            return new BatchStudentDto
            {
                EnrollmentId = enrollment.Id,
                ChildId = enrollment.ChildId,
                ChildName = $"{enrollment.Child.FirstName} {enrollment.Child.LastName}".Trim(),
                AcademicLevel = enrollment.Child.AcademicLevel,
                Status = enrollment.Status,
                EnrolledAtUtc = enrollment.CreatedAtUtc,
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
                TeacherName = session.TeacherProfile?.User is { } u ? $"{u.FirstName} {u.LastName}".Trim() : string.Empty,
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
