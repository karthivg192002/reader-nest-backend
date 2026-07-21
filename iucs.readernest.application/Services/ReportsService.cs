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

        public async Task<IReadOnlyList<TeacherPerformanceDto>> GetTeacherPerformanceAsync(CancellationToken cancellationToken = default)
        {
            var teachers = await _unitOfWork.Repository<TeacherProfile>().Query()
                .Include(t => t.User)
                .Where(t => t.User.Status == UserStatus.Active)
                .ToListAsync(cancellationToken);

            var now = DateTime.UtcNow;
            var result = new List<TeacherPerformanceDto>(teachers.Count);
            foreach (var teacher in teachers)
            {
                var sessions = await _unitOfWork.Repository<ClassSession>().Query()
                    .Where(s => s.TeacherProfileId == teacher.Id)
                    .Select(s => new { s.Id, s.Status, s.ScheduledStartAtUtc, HasSummary = s.Summary != null })
                    .ToListAsync(cancellationToken);
                var completedIds = sessions.Where(s => s.Status == SessionStatus.Completed).Select(s => s.Id).ToList();

                var attendanceRows = await _unitOfWork.Repository<SessionAttendance>().Query()
                    .Where(a => completedIds.Contains(a.ClassSessionId) && a.ParticipantType == ParticipantType.Student)
                    .Select(a => a.Status)
                    .ToListAsync(cancellationToken);

                result.Add(new TeacherPerformanceDto
                {
                    TeacherProfileId = teacher.Id,
                    TeacherName = $"{teacher.User.FirstName} {teacher.User.LastName}",
                    Department = teacher.Department?.ToString(),
                    SessionsCompleted = completedIds.Count,
                    TeacherNoShows = sessions.Count(s => s.Status == SessionStatus.TeacherNoShow),
                    UpcomingSessions = sessions.Count(s => s.Status == SessionStatus.Scheduled && s.ScheduledStartAtUtc > now),
                    StudentAttendancePercent = attendanceRows.Count == 0
                        ? 100
                        : Math.Round(100.0 * attendanceRows.Count(a => a != AttendanceStatus.Absent) / attendanceRows.Count, 1),
                    SummariesWritten = sessions.Count(s => s.HasSummary),
                });
            }

            return result.OrderByDescending(t => t.SessionsCompleted).ToList();
        }

        public async Task<StudentAnalyticsDto> GetStudentAnalyticsAsync(Guid childId, CancellationToken cancellationToken = default)
        {
            var child = await _unitOfWork.Repository<Child>().GetByIdAsync(childId, cancellationToken)
                ?? throw new NotFoundException(nameof(Child), childId);

            var attendance = await _unitOfWork.Repository<SessionAttendance>().Query()
                .Where(a => a.ChildId == childId)
                .Select(a => a.Status)
                .ToListAsync(cancellationToken);
            var attended = attendance.Count(a => a != AttendanceStatus.Absent);
            var attendancePercent = attendance.Count == 0 ? 100 : Math.Round(100.0 * attended / attendance.Count, 1);

            var events = await _unitOfWork.Repository<EngagementEvent>().Query()
                .Where(e => e.ChildId == childId)
                .ToListAsync(cancellationToken);
            var quizAttempts = events.Where(e => e.Type is EngagementEventType.QuizAttempt or EngagementEventType.QuizCorrect).Sum(e => e.Value);
            var quizCorrect = events.Where(e => e.Type == EngagementEventType.QuizCorrect).Sum(e => e.Value);
            var activity = events.Where(e => e.Type is EngagementEventType.ActivityClick or EngagementEventType.ActivityCompleted).Sum(e => e.Value);
            var whiteboard = events.Where(e => e.Type == EngagementEventType.WhiteboardInteraction).Sum(e => e.Value);
            var talkSeconds = events.Where(e => e.Type == EngagementEventType.TalkTimeSeconds).Sum(e => e.Value);
            var cameraSeconds = events.Where(e => e.Type == EngagementEventType.CameraOnSeconds).Sum(e => e.Value);

            var sessionCount = Math.Max(1, events.Select(e => e.ClassSessionId).Distinct().Count());
            var avgScore = Math.Min(100,
                (Math.Min(quizCorrect * 2, 30) + Math.Min(quizAttempts, 20) + Math.Min(activity * 2, 20) + Math.Min(whiteboard, 15)) / sessionCount * 2);

            // Generated progress insights: rule-based narrative from the captured signals
            var name = child.FirstName;
            var insights = new List<string>();
            insights.Add(attendancePercent >= 90
                ? $"{name} attends consistently ({attendancePercent}%) — a strong routine is in place."
                : attendancePercent >= 75
                    ? $"{name}'s attendance is {attendancePercent}%; a steadier routine would compound progress."
                    : $"Attendance is {attendancePercent}% — missed classes are the biggest lever for {name} right now.");
            if (quizAttempts > 0)
            {
                var accuracy = Math.Round(100.0 * quizCorrect / Math.Max(1, quizAttempts));
                insights.Add(accuracy >= 70
                    ? $"Quiz accuracy is {accuracy}% across {quizAttempts} attempts — concepts are landing."
                    : $"Quiz accuracy is {accuracy}%; a recap of recent topics before new material would help.");
            }
            else
            {
                insights.Add($"{name} hasn't attempted in-class quizzes yet — gentle prompting will build confidence.");
            }
            insights.Add(activity + whiteboard >= 10
                ? $"{name} participates actively in board activities ({activity + whiteboard} interactions)."
                : $"Participation in board activities is light ({activity + whiteboard} interactions) — calling {name} up for one activity per class would lift engagement.");
            // Talk-time and camera attentiveness from the live-classroom media signals
            if (talkSeconds > 0)
            {
                var talkMinutes = Math.Round(talkSeconds / 60.0, 1);
                insights.Add(talkSeconds >= 120
                    ? $"{name} speaks up in class (~{talkMinutes} min of talk time captured) — great verbal participation."
                    : $"Talk time is low (~{talkMinutes} min) — reading-aloud turns would build {name}'s speaking confidence.");
            }
            if (cameraSeconds > 0 && talkSeconds + cameraSeconds > 0)
            {
                var camMinutes = Math.Round(cameraSeconds / 60.0);
                insights.Add($"Camera attentiveness: on screen for ~{camMinutes} min across tracked classes.");
            }

            return new StudentAnalyticsDto
            {
                ChildId = child.Id,
                ChildName = $"{child.FirstName} {child.LastName}",
                AttendancePercent = attendancePercent,
                SessionsAttended = attended,
                QuizAttempts = quizAttempts,
                QuizCorrect = quizCorrect,
                ActivityInteractions = activity,
                WhiteboardInteractions = whiteboard,
                AverageEngagementScore = avgScore,
                TalkTimeSeconds = talkSeconds,
                CameraOnSeconds = cameraSeconds,
                Insights = insights,
            };
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

            // Renewal rate: children whose batch completed (Dormant) who later hold an active
            // enrollment in a different batch, over all children whose batch completed.
            var completedEnrollmentsByChild = await _unitOfWork.Repository<BatchEnrollment>().Query()
                .Where(e => e.Batch.Status == BatchStatus.Dormant)
                .Select(e => new { e.ChildId, e.BatchId })
                .ToListAsync(cancellationToken);
            var completedChildIds = completedEnrollmentsByChild.Select(e => e.ChildId).Distinct().ToList();
            var renewedChildCount = 0;
            if (completedChildIds.Count > 0)
            {
                var completedBatchesByChild = completedEnrollmentsByChild
                    .GroupBy(e => e.ChildId)
                    .ToDictionary(g => g.Key, g => g.Select(e => e.BatchId).ToHashSet());
                var activeEnrollmentsByChild = await _unitOfWork.Repository<BatchEnrollment>().Query()
                    .Where(e => e.Status == EnrollmentStatus.Active && completedChildIds.Contains(e.ChildId))
                    .Select(e => new { e.ChildId, e.BatchId })
                    .ToListAsync(cancellationToken);
                renewedChildCount = activeEnrollmentsByChild
                    .GroupBy(e => e.ChildId)
                    .Count(g => g.Any(e => !completedBatchesByChild[g.Key].Contains(e.BatchId)));
            }
            var renewalRatePercent = completedChildIds.Count == 0
                ? 0
                : Math.Round(100.0 * renewedChildCount / completedChildIds.Count, 1);

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

            // Revenue trend: successful payments grouped into the last 6 calendar months (oldest first).
            var trendStart = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1, 0, 0, 0, DateTimeKind.Utc).AddMonths(-5);
            var successfulPayments = await _unitOfWork.Repository<PaymentTransaction>().Query()
                .Where(t => t.Status == TransactionStatus.Success && t.PaidAtUtc != null && t.PaidAtUtc >= trendStart)
                .Select(t => new { t.PaidAtUtc, t.Amount })
                .ToListAsync(cancellationToken);
            var revenueTrend = Enumerable.Range(0, 6)
                .Select(offset =>
                {
                    var month = trendStart.AddMonths(offset);
                    var revenue = successfulPayments
                        .Where(p => p.PaidAtUtc!.Value.Year == month.Year && p.PaidAtUtc.Value.Month == month.Month)
                        .Sum(p => p.Amount);
                    return new RevenuePointDto { Month = month.ToString("MMM"), Revenue = revenue };
                })
                .ToList();

            // Weekly attendance trend: student attendance % per week for the last 6 weeks (oldest first).
            var today = DateTime.UtcNow.Date;
            var mondayThisWeek = today.AddDays(-(((int)today.DayOfWeek + 6) % 7));
            var attendanceWindowStart = mondayThisWeek.AddDays(-7 * 5);
            var attendanceRows = await _unitOfWork.Repository<SessionAttendance>().Query()
                .Where(a => a.ParticipantType == ParticipantType.Student
                    && a.ClassSession.ScheduledStartAtUtc >= attendanceWindowStart)
                .Select(a => new { a.Status, a.ClassSession.ScheduledStartAtUtc })
                .ToListAsync(cancellationToken);
            var weeklyAttendanceTrend = Enumerable.Range(0, 6)
                .Select(offset =>
                {
                    var weekStart = attendanceWindowStart.AddDays(7 * offset);
                    var weekEnd = weekStart.AddDays(7);
                    var week = attendanceRows.Where(r => r.ScheduledStartAtUtc >= weekStart && r.ScheduledStartAtUtc < weekEnd).ToList();
                    return new AttendanceWeekDto
                    {
                        Week = weekStart.ToString("dd MMM"),
                        Attendance = week.Count == 0
                            ? 0
                            : Math.Round(100.0 * week.Count(r => r.Status != AttendanceStatus.Absent) / week.Count, 1),
                    };
                })
                .ToList();

            // Batch occupancy split by course (active batches only, highest fill first).
            var occupancyByCourseRows = await _unitOfWork.Repository<Batch>().Query()
                .Where(b => b.Status == BatchStatus.Active && b.Capacity > 0)
                .Select(b => new
                {
                    CourseName = b.Course.Name,
                    b.Capacity,
                    Enrolled = b.Enrollments.Count(e => e.Status == EnrollmentStatus.Active),
                })
                .ToListAsync(cancellationToken);
            var batchOccupancyByCourse = occupancyByCourseRows
                .GroupBy(x => x.CourseName)
                .Select(g => new CourseOccupancyDto
                {
                    Course = g.Key,
                    Occupancy = Math.Round(100.0 * g.Sum(x => x.Enrolled) / g.Sum(x => x.Capacity), 1),
                })
                .OrderByDescending(c => c.Occupancy)
                .ToList();

            // Conversion trend: demo→enrolled % per booking-month over the same 6-month window.
            var trendBookings = await _unitOfWork.Repository<DemoBooking>().Query()
                .Where(b => b.CreatedAtUtc >= trendStart)
                .Select(b => new { b.CreatedAtUtc, b.ConversionStatus })
                .ToListAsync(cancellationToken);
            var conversionRateTrend = Enumerable.Range(0, 6)
                .Select(offset =>
                {
                    var month = trendStart.AddMonths(offset);
                    var monthRows = trendBookings
                        .Where(b => b.CreatedAtUtc.Year == month.Year && b.CreatedAtUtc.Month == month.Month)
                        .ToList();
                    return new ConversionPointDto
                    {
                        Month = month.ToString("MMM"),
                        Rate = monthRows.Count == 0
                            ? 0
                            : Math.Round(100.0 * monthRows.Count(b => b.ConversionStatus == ConversionStatus.Enrolled) / monthRows.Count, 1),
                    };
                })
                .ToList();

            // Enrollment funnel: cumulative demo-booking stage counts.
            var demoByStatus = await _unitOfWork.Repository<DemoBooking>().Query()
                .GroupBy(b => b.ConversionStatus)
                .Select(g => new { Status = g.Key, Count = g.Count() })
                .ToListAsync(cancellationToken);
            int StageCount(params ConversionStatus[] statuses) =>
                demoByStatus.Where(x => statuses.Contains(x.Status)).Sum(x => x.Count);
            var enrollmentFunnel = new List<FunnelStageDto>
            {
                new() { Stage = "Demo Booked", Value = demoTotal },
                new() { Stage = "Demo Completed", Value = StageCount(ConversionStatus.DemoCompleted, ConversionStatus.FollowUpInProgress, ConversionStatus.Enrolled) },
                new() { Stage = "Follow-up", Value = StageCount(ConversionStatus.FollowUpInProgress, ConversionStatus.Enrolled) },
                new() { Stage = "Enrolled", Value = demoEnrolled },
            };

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
                RenewalRatePercent = renewalRatePercent,
                BatchOccupancyPercent = occupancyPercent,
                TeacherUtilizationSessionsPerTeacher = activeTeachers == 0
                    ? 0
                    : Math.Round((double)completedSessions / activeTeachers, 1),
                RevenueByDepartment = revenueByDepartment,
                RevenueTrend = revenueTrend,
                EnrollmentFunnel = enrollmentFunnel,
                WeeklyAttendanceTrend = weeklyAttendanceTrend,
                BatchOccupancyByCourse = batchOccupancyByCourse,
                ConversionRateTrend = conversionRateTrend,
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
            var recipients = await ResolveBulkEmailRecipientsAsync(request.BatchId, cancellationToken);

            foreach (var user in recipients)
            {
                await _notificationService.SendEmailAsync(
                    user.Id, user.Email, NotificationType.General, request.Subject, request.Body, cancellationToken);
            }

            return new BulkEmailResultDto { RecipientCount = recipients.Count };
        }

        public async Task<BulkEmailResultDto> PreviewBulkEmailAsync(Guid? batchId, CancellationToken cancellationToken = default)
        {
            var recipients = await ResolveBulkEmailRecipientsAsync(batchId, cancellationToken);
            return new BulkEmailResultDto { RecipientCount = recipients.Count };
        }

        /// <summary>
        /// One recipient rule for both the compose-screen preview and the actual send, so the
        /// count the admin sees is exactly who the email goes to: every active parent, or the
        /// parents of the batch's actively-enrolled students.
        /// </summary>
        private async Task<List<User>> ResolveBulkEmailRecipientsAsync(Guid? batchId, CancellationToken cancellationToken)
        {
            if (batchId.HasValue)
            {
                return await _unitOfWork.Repository<BatchEnrollment>().Query()
                    .Where(e => e.BatchId == batchId.Value && e.Status == EnrollmentStatus.Active)
                    .Select(e => e.Child.ParentProfile.User)
                    .Distinct()
                    .ToListAsync(cancellationToken);
            }

            return await _unitOfWork.Repository<User>().Query()
                .Where(u => u.Role == UserRole.Parent && u.Status == UserStatus.Active)
                .ToListAsync(cancellationToken);
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
