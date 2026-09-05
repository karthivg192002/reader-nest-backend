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

        /// <summary>Null when this child has no attendance-marked sessions yet -- distinct from
        /// a real 0%. Render as "no data yet", not as a default percentage.</summary>
        public double? AttendancePercent { get; set; }

        /// <summary>paid | due | overdue | suspended -- genuinely this child's own status; a
        /// sibling's unrelated overdue invoice never marks another child suspended.</summary>
        public string FeeStatus { get; set; } = "paid";

        /// <summary>True when this specific child's access is blocked (their own suspension,
        /// or an account-wide one) -- see FeeSuspension's doc comment on the two scopes.</summary>
        public bool IsSuspended { get; set; }

        /// <summary>The invoice that must be paid to unlock this child specifically. Null when
        /// not suspended, or when blocked only by an account-wide suspension with no single
        /// invoice to point at (see ParentDashboardDto.SuspendedInvoiceId for that case).</summary>
        public Guid? SuspendedInvoiceId { get; set; }
    }

    public class ParentDashboardDto
    {
        public Guid ParentProfileId { get; set; }

        public bool EnrollmentFormCompleted { get; set; }

        /// <summary>True when at least one child is suspended -- see each child's own
        /// IsSuspended for which. Kept for callers that only need "is anything blocked".</summary>
        public bool IsSuspended { get; set; }

        /// <summary>True only when EVERY child on the account is currently suspended -- nothing
        /// on the account is reachable, so the whole portal can be replaced with a single block
        /// screen instead of a per-child one. False whenever at least one child still has full
        /// access, even if others don't.</summary>
        public bool AllChildrenSuspended { get; set; }

        /// <summary>The invoice to pay to fully unblock the account when AllChildrenSuspended is
        /// true and every affected child shares one common family-level (ChildId-null) invoice.
        /// Null otherwise -- with multiple children, resolve each one's own
        /// ParentChildSummaryDto.SuspendedInvoiceId instead of assuming a single invoice fixes
        /// everything.</summary>
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

        /// <summary>
        /// Collected revenue grouped by course, for invoices that carry a CourseId (mainly
        /// subscription-driven billing). Invoices with no course resolved (e.g. manual
        /// admin-created invoices) are rolled into a single "Unassigned" bucket rather than
        /// dropped, so this total always reconciles with RevenueCollected.
        /// </summary>
        public IReadOnlyList<CourseRevenueDto> RevenueByCourse { get; set; } = [];

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

        /// <summary>Null when the teacher has no completed, attendance-marked sessions yet —
        /// distinct from a real 0%. A vacuous "100" here used to make an idle teacher with zero
        /// sessions delivered look fully utilized on the Management "Teacher Utilization" chart,
        /// which reads this field as delivery-vs-capacity. Consumers should render null as
        /// "No data" rather than defaulting it to any percentage.</summary>
        public double? StudentAttendancePercent { get; set; }

        public int SummariesWritten { get; set; }

        /// <summary>
        /// Status + total only — deliberately not the itemized breakdown GET /api/payouts
        /// returns (line items, notes, review flags, bank/rate details), which stays
        /// Admin-only ("payout/salary details are Super-Admin only"). This is the narrow
        /// "visibility, not administration" slice the Management report is meant to show:
        /// this teacher's most recent payout period, whatever its status — including a
        /// still-Pending one, so an amount actively accruing this month isn't invisible
        /// just because it hasn't been finalized/paid yet. Null when the teacher has no
        /// payout on record at all. Year/month (not a pre-formatted string) to match
        /// ApiPayout's own shape, so the frontend can reuse the same month-name formatting.
        /// </summary>
        public int? LatestPayoutPeriodYear { get; set; }

        public int? LatestPayoutPeriodMonth { get; set; }

        public PayoutStatus? LatestPayoutStatus { get; set; }

        public decimal? LatestPayoutAmount { get; set; }
    }

    /// <summary>Per-child analytics with generated progress insights.</summary>
    public class StudentAnalyticsDto
    {
        public Guid ChildId { get; set; }

        public string ChildName { get; set; } = null!;

        /// <summary>Null when this child has no attendance-marked sessions yet -- distinct
        /// from a real 0%.</summary>
        public double? AttendancePercent { get; set; }

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

    /// <summary>One past Bulk Email send, for the admin History list.</summary>
    public class BulkEmailHistoryItemDto
    {
        public Guid Id { get; set; }

        public string Subject { get; set; } = null!;

        public string SentByName { get; set; } = null!;

        public BulkEmailScope Scope { get; set; }

        public string? BatchName { get; set; }

        public DateTime SentAtUtc { get; set; }

        public int TotalRecipients { get; set; }

        public int SuccessCount { get; set; }

        public int FailureCount { get; set; }
    }

    /// <summary>One blast's full recipient list with delivery status and any reply, for the
    /// admin History detail view.</summary>
    public class BulkEmailBlastDetailDto
    {
        public Guid Id { get; set; }

        public string Subject { get; set; } = null!;

        public string Body { get; set; } = null!;

        public string SentByName { get; set; } = null!;

        public BulkEmailScope Scope { get; set; }

        public string? BatchName { get; set; }

        public DateTime SentAtUtc { get; set; }

        public IReadOnlyList<BulkEmailRecipientDto> Recipients { get; set; } = [];
    }

    public class BulkEmailRecipientDto
    {
        public Guid Id { get; set; }

        public string RecipientName { get; set; } = null!;

        public string Email { get; set; } = null!;

        public NotificationStatus Status { get; set; }

        public string? ErrorMessage { get; set; }

        public DateTime? SentAtUtc { get; set; }

        public string? ReplyMessage { get; set; }

        public DateTime? ReplyAtUtc { get; set; }
    }

    public class ReplyToBulkEmailRequest
    {
        [System.ComponentModel.DataAnnotations.Required]
        [System.ComponentModel.DataAnnotations.MaxLength(4000)]
        public string Message { get; set; } = null!;
    }
}
