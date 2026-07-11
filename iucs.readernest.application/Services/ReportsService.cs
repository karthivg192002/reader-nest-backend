using System.Text;
using iucs.readernest.application.Common.Exceptions;
using iucs.readernest.application.Dto.Reports;
using iucs.readernest.domain.Entities.Academics;
using iucs.readernest.domain.Entities.Admission;
using iucs.readernest.domain.Entities.Billing;
using iucs.readernest.domain.Entities.Payouts;
using iucs.readernest.domain.Entities.Sessions;
using iucs.readernest.domain.Entities.Users;
using iucs.readernest.domain.Enums;
using iucs.readernest.domain.Repository;
using Microsoft.EntityFrameworkCore;

namespace iucs.readernest.application.Services
{
    public class ReportsService : IReportsService
    {
        private readonly IUnitOfWork _unitOfWork;
        private readonly INotificationService _notificationService;

        public ReportsService(IUnitOfWork unitOfWork, INotificationService notificationService)
        {
            _unitOfWork = unitOfWork;
            _notificationService = notificationService;
        }

        public async Task<DashboardSummaryDto> GetDashboardSummaryAsync(CancellationToken cancellationToken = default)
        {
            var totalStudents = await _unitOfWork.Repository<Child>().Query().CountAsync(cancellationToken);
            var activeStudents = await _unitOfWork.Repository<Child>().Query().CountAsync(c => c.IsActive, cancellationToken);
            var totalEnrollments = await _unitOfWork.Repository<BatchEnrollment>().Query()
                .CountAsync(e => e.Status == EnrollmentStatus.Active, cancellationToken);

            var invoices = await _unitOfWork.Repository<Invoice>().Query()
                .Select(i => new { i.Department, i.Amount, i.AmountPaid, i.Status })
                .ToListAsync(cancellationToken);
            var revenueCollected = invoices.Sum(i => i.AmountPaid);
            var revenuePending = invoices
                .Where(i => i.Status is InvoiceStatus.Pending or InvoiceStatus.PartiallyPaid or InvoiceStatus.Overdue)
                .Sum(i => i.Amount - i.AmountPaid);

            var refunded = await _unitOfWork.Repository<Refund>().Query()
                .Where(r => r.Status == RefundStatus.Processed)
                .SumAsync(r => (decimal?)r.Amount, cancellationToken) ?? 0m;

            var activeBatches = await _unitOfWork.Repository<Batch>().Query()
                .CountAsync(b => b.Status == BatchStatus.Active, cancellationToken);
            var dormantBatches = await _unitOfWork.Repository<Batch>().Query()
                .CountAsync(b => b.Status == BatchStatus.Dormant, cancellationToken);

            var occupancy = await _unitOfWork.Repository<Batch>().Query()
                .Where(b => b.Status == BatchStatus.Active && b.Capacity > 0)
                .Select(b => new { b.Capacity, Enrolled = b.Enrollments.Count(e => e.Status == EnrollmentStatus.Active) })
                .ToListAsync(cancellationToken);
            var occupancyPercent = occupancy.Count == 0
                ? 0
                : Math.Round(100.0 * occupancy.Sum(o => o.Enrolled) / occupancy.Sum(o => o.Capacity), 1);

            var demoTotal = await _unitOfWork.Repository<DemoBooking>().Query().CountAsync(cancellationToken);
            var demoEnrolled = await _unitOfWork.Repository<DemoBooking>().Query()
                .CountAsync(b => b.ConversionStatus == ConversionStatus.Enrolled, cancellationToken);

            var since = DateTime.UtcNow.AddDays(-30);
            var completedSessions = await _unitOfWork.Repository<ClassSession>().Query()
                .CountAsync(s => s.Status == SessionStatus.Completed && s.ScheduledStartAtUtc >= since, cancellationToken);
            var activeTeachers = await _unitOfWork.Repository<TeacherProfile>().Query()
                .CountAsync(t => t.User.Status == UserStatus.Active, cancellationToken);

            var revenueByDepartment = invoices
                .GroupBy(i => i.Department)
                .Select(g => new CourseRevenueDto { Name = g.Key.ToString(), Revenue = g.Sum(i => i.AmountPaid) })
                .OrderByDescending(r => r.Revenue)
                .ToList();

            return new DashboardSummaryDto
            {
                TotalStudents = totalStudents,
                ActiveStudents = activeStudents,
                RevenueCollected = revenueCollected,
                RevenuePending = revenuePending,
                TotalEnrollments = totalEnrollments,
                ActiveBatches = activeBatches,
                DormantBatches = dormantBatches,
                ConversionRatePercent = demoTotal == 0 ? 0 : Math.Round(100.0 * demoEnrolled / demoTotal, 1),
                RefundRatePercent = revenueCollected == 0 ? 0 : Math.Round((double)(100m * refunded / revenueCollected), 1),
                BatchOccupancyPercent = occupancyPercent,
                TeacherUtilizationSessionsPerTeacher = activeTeachers == 0
                    ? 0
                    : Math.Round((double)completedSessions / activeTeachers, 1),
                RevenueByDepartment = revenueByDepartment,
            };
        }

        public async Task<string> ExportCsvAsync(string report, CancellationToken cancellationToken = default)
        {
            return report.ToLowerInvariant() switch
            {
                "attendance" => await AttendanceCsvAsync(cancellationToken),
                "revenue" => await RevenueCsvAsync(cancellationToken),
                "payouts" => await PayoutsCsvAsync(cancellationToken),
                "conversion" => await ConversionCsvAsync(cancellationToken),
                _ => throw new DomainValidationException("Unknown report. Use: attendance, revenue, payouts or conversion."),
            };
        }

        public async Task<BulkEmailResultDto> SendBulkEmailAsync(BulkEmailRequest request, CancellationToken cancellationToken = default)
        {
            List<User> recipients;
            if (request.BatchId.HasValue)
            {
                recipients = await _unitOfWork.Repository<BatchEnrollment>().Query()
                    .Where(e => e.BatchId == request.BatchId.Value && e.Status == EnrollmentStatus.Active)
                    .Select(e => e.Child.ParentProfile.User)
                    .Distinct()
                    .ToListAsync(cancellationToken);
            }
            else
            {
                recipients = await _unitOfWork.Repository<User>().Query()
                    .Where(u => u.Role == UserRole.Parent && u.Status == UserStatus.Active)
                    .ToListAsync(cancellationToken);
            }

            foreach (var user in recipients)
            {
                await _notificationService.SendEmailAsync(
                    user.Id, user.Email, NotificationType.General, request.Subject, request.Body, cancellationToken);
            }

            return new BulkEmailResultDto { RecipientCount = recipients.Count };
        }

        private async Task<string> AttendanceCsvAsync(CancellationToken cancellationToken)
        {
            var rows = await _unitOfWork.Repository<SessionAttendance>().Query()
                .Include(a => a.ClassSession)
                .Include(a => a.Child)
                .OrderBy(a => a.ClassSession.ScheduledStartAtUtc)
                .ToListAsync(cancellationToken);

            var csv = new StringBuilder("SessionStartUtc,ParticipantType,Participant,Status,JoinedAtUtc,LeftAtUtc\n");
            foreach (var a in rows)
            {
                var name = a.Child is null ? a.TeacherProfileId?.ToString() : $"{a.Child.FirstName} {a.Child.LastName}";
                csv.AppendLine(string.Join(',',
                    Escape($"{a.ClassSession.ScheduledStartAtUtc:u}"), a.ParticipantType, Escape(name),
                    a.Status, Escape($"{a.JoinedAtUtc:u}"), Escape($"{a.LeftAtUtc:u}")));
            }

            return csv.ToString();
        }

        private async Task<string> RevenueCsvAsync(CancellationToken cancellationToken)
        {
            var invoices = await _unitOfWork.Repository<Invoice>().Query()
                .OrderBy(i => i.IssuedAtUtc)
                .ToListAsync(cancellationToken);

            var csv = new StringBuilder("InvoiceNumber,Department,Amount,AmountPaid,Status,IssuedAtUtc,DueDate,PaidAtUtc\n");
            foreach (var i in invoices)
            {
                csv.AppendLine(string.Join(',',
                    i.InvoiceNumber, i.Department, i.Amount, i.AmountPaid, i.Status,
                    Escape($"{i.IssuedAtUtc:u}"), i.DueDate.ToString("yyyy-MM-dd"), Escape($"{i.PaidAtUtc:u}")));
            }

            return csv.ToString();
        }

        private async Task<string> PayoutsCsvAsync(CancellationToken cancellationToken)
        {
            var payouts = await _unitOfWork.Repository<Payout>().Query()
                .Include(p => p.Items)
                .Include(p => p.TeacherProfile).ThenInclude(t => t.User)
                .OrderBy(p => p.PeriodYear).ThenBy(p => p.PeriodMonth)
                .ToListAsync(cancellationToken);

            var csv = new StringBuilder("Teacher,Period,Status,Sessions,Total\n");
            foreach (var p in payouts)
            {
                csv.AppendLine(string.Join(',',
                    Escape($"{p.TeacherProfile.User.FirstName} {p.TeacherProfile.User.LastName}"),
                    $"{p.PeriodYear}-{p.PeriodMonth:D2}", p.Status,
                    p.Items.Count(i => i.Type == PayoutItemType.SessionEarning), p.TotalAmount));
            }

            return csv.ToString();
        }

        private async Task<string> ConversionCsvAsync(CancellationToken cancellationToken)
        {
            var bookings = await _unitOfWork.Repository<DemoBooking>().Query()
                .OrderBy(b => b.CreatedAtUtc)
                .ToListAsync(cancellationToken);

            var csv = new StringBuilder("Child,Parent,ParentEmail,Department,ConversionStatus,CreatedAtUtc\n");
            foreach (var b in bookings)
            {
                csv.AppendLine(string.Join(',',
                    Escape(b.ChildName), Escape(b.ParentName), Escape(b.ParentEmail),
                    b.Department, b.ConversionStatus, Escape($"{b.CreatedAtUtc:u}")));
            }

            return csv.ToString();
        }

        private static string Escape(string? value)
        {
            if (string.IsNullOrEmpty(value)) return "";
            return value.Contains(',') || value.Contains('"')
                ? $"\"{value.Replace("\"", "\"\"")}\""
                : value;
        }
    }
}
