using iucs.readernest.domain.Enums;

namespace iucs.readernest.application.Dto.Portal
{
    public class ParentChildSummaryDto
    {
        public Guid ChildId { get; set; }

        public string Name { get; set; } = null!;

        public string? AcademicLevel { get; set; }

        public int ClassesCompleted { get; set; }

        public int ClassesRemaining { get; set; }

        public double AttendancePercent { get; set; }

        /// <summary>paid | due | overdue | suspended</summary>
        public string FeeStatus { get; set; } = "paid";
    }

    public class ParentDashboardDto
    {
        public Guid ParentProfileId { get; set; }

        public bool EnrollmentFormCompleted { get; set; }

        /// <summary>Active fee suspension blocks session/content access and triggers the Pay Now popup.</summary>
        public bool IsSuspended { get; set; }

        public Guid? SuspendedInvoiceId { get; set; }

        public IReadOnlyList<ParentChildSummaryDto> Children { get; set; } = [];
    }
}

namespace iucs.readernest.application.Dto.Reports
{
    public class DashboardSummaryDto
    {
        public int TotalStudents { get; set; }

        public int ActiveStudents { get; set; }

        public decimal RevenueCollected { get; set; }

        public decimal RevenuePending { get; set; }

        public int TotalEnrollments { get; set; }

        public int ActiveBatches { get; set; }

        public int DormantBatches { get; set; }

        /// <summary>Enrolled demo bookings / all demo bookings.</summary>
        public double ConversionRatePercent { get; set; }

        /// <summary>Refunded amount / collected amount.</summary>
        public double RefundRatePercent { get; set; }

        /// <summary>Children re-enrolled into another batch after a prior batch completed / children with a completed batch.</summary>
        public double RenewalRatePercent { get; set; }

        /// <summary>Average enrolled/capacity across active batches.</summary>
        public double BatchOccupancyPercent { get; set; }

        /// <summary>Completed sessions per active teacher in the last 30 days.</summary>
        public double TeacherUtilizationSessionsPerTeacher { get; set; }

        public IReadOnlyList<CourseRevenueDto> RevenueByDepartment { get; set; } = [];

        /// <summary>Cash collected per month for the last 6 calendar months (oldest first).</summary>
        public IReadOnlyList<RevenuePointDto> RevenueTrend { get; set; } = [];

        /// <summary>Admission funnel counts: demo booked → completed → follow-up → enrolled.</summary>
        public IReadOnlyList<FunnelStageDto> EnrollmentFunnel { get; set; } = [];

        /// <summary>Student attendance % per week for the last 6 weeks (oldest first).</summary>
        public IReadOnlyList<AttendanceWeekDto> WeeklyAttendanceTrend { get; set; } = [];

        /// <summary>Active-batch fill rate per course (highest first).</summary>
        public IReadOnlyList<CourseOccupancyDto> BatchOccupancyByCourse { get; set; } = [];

        /// <summary>Demo→enrolled conversion % per booking-month for the last 6 calendar months (oldest first).</summary>
        public IReadOnlyList<ConversionPointDto> ConversionRateTrend { get; set; } = [];
    }

    public class AttendanceWeekDto
    {
        /// <summary>Week label — the Monday the week starts on (e.g. "23 Jun").</summary>
        public string Week { get; set; } = null!;

        public double Attendance { get; set; }
    }

    public class CourseOccupancyDto
    {
        public string Course { get; set; } = null!;

        public double Occupancy { get; set; }
    }

    public class ConversionPointDto
    {
        public string Month { get; set; } = null!;

        public double Rate { get; set; }
    }

    public class RevenuePointDto
    {
        public string Month { get; set; } = null!;

        public decimal Revenue { get; set; }
    }

    public class FunnelStageDto
    {
        public string Stage { get; set; } = null!;

        public int Value { get; set; }
    }

    public class CourseRevenueDto
    {
        public string Name { get; set; } = null!;

        public decimal Revenue { get; set; }
    }

    /// <summary>Per-teacher delivery stats for the performance view.</summary>
    public class TeacherPerformanceDto
    {
        public Guid TeacherProfileId { get; set; }

        public string TeacherName { get; set; } = null!;

        public string? Department { get; set; }

        public int SessionsCompleted { get; set; }

        public int TeacherNoShows { get; set; }

        public int UpcomingSessions { get; set; }

        public double StudentAttendancePercent { get; set; }

        public int SummariesWritten { get; set; }
    }

    /// <summary>Per-child analytics with generated progress insights.</summary>
    public class StudentAnalyticsDto
    {
        public Guid ChildId { get; set; }

        public string ChildName { get; set; } = null!;

        public double AttendancePercent { get; set; }

        public int SessionsAttended { get; set; }

        public int QuizAttempts { get; set; }

        public int QuizCorrect { get; set; }

        public int ActivityInteractions { get; set; }

        public int WhiteboardInteractions { get; set; }

        public int AverageEngagementScore { get; set; }

        /// <summary>Dominant-speaker seconds captured from the live classroom (talk-time analysis).</summary>
        public int TalkTimeSeconds { get; set; }

        /// <summary>Camera-on seconds captured from the live classroom (attentiveness signal).</summary>
        public int CameraOnSeconds { get; set; }

        /// <summary>Generated narrative progress insights derived from the signals above.</summary>
        public IReadOnlyList<string> Insights { get; set; } = [];
    }

    public class BulkEmailRequest
    {
        [System.ComponentModel.DataAnnotations.Required]
        [System.ComponentModel.DataAnnotations.MaxLength(200)]
        public string Subject { get; set; } = null!;

        [System.ComponentModel.DataAnnotations.Required]
        [System.ComponentModel.DataAnnotations.MaxLength(10000)]
        public string Body { get; set; } = null!;

        /// <summary>Limits recipients to parents of the batch's enrolled children; null = all active parents.</summary>
        public Guid? BatchId { get; set; }
    }

    public class BulkEmailResultDto
    {
        public int RecipientCount { get; set; }
    }
}
