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

        /// <summary>Average enrolled/capacity across active batches.</summary>
        public double BatchOccupancyPercent { get; set; }

        /// <summary>Completed sessions per active teacher in the last 30 days.</summary>
        public double TeacherUtilizationSessionsPerTeacher { get; set; }

        public IReadOnlyList<CourseRevenueDto> RevenueByDepartment { get; set; } = [];
    }

    public class CourseRevenueDto
    {
        public string Name { get; set; } = null!;

        public decimal Revenue { get; set; }
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
