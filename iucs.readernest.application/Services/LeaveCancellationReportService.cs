using System.Text.Json;
using iucs.readernest.application.Common.Exceptions;
using iucs.readernest.application.Dto.Academics;
using iucs.readernest.application.Helper;
using iucs.readernest.domain.Entities.Academics;
using iucs.readernest.domain.Entities.Sessions;
using iucs.readernest.domain.Enums;
using iucs.readernest.domain.Repository;
using Microsoft.EntityFrameworkCore;

namespace iucs.readernest.application.Services
{
    public class LeaveCancellationReportService : ILeaveCancellationReportService
    {
        // Both leave paths in AcademicOpsService stamp one of these into CancellationReason
        // ("Cancelled by X (Teacher): teacher's leave ..." / "...teacher's class-wise leave ...").
        // A class cancelled for any other reason (parent, admin, batch change) must not count
        // against the teacher's pay.
        // Cancellations made before the "who" prefix existed read "Teacher on approved leave (...)"
        // / "Teacher on approved class-wise leave: ..." (21 September-2026 classes on live), so
        // those wordings must count too or older months would under-report.
        private const string WindowLeaveMarker = "teacher's leave";
        private const string ClassWiseLeaveMarker = "teacher's class-wise leave";
        private const string LegacyWindowLeaveMarker = "Teacher on approved leave";
        private const string LegacyClassWiseLeaveMarker = "Teacher on approved class-wise leave";

        /// <summary>How many already-finished months the scheduled job back-fills if a snapshot is missing.</summary>
        private const int BackfillMonths = 3;

        private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

        private readonly IUnitOfWork _unitOfWork;

        public LeaveCancellationReportService(IUnitOfWork unitOfWork)
        {
            _unitOfWork = unitOfWork;
        }

        public async Task<LeaveCancellationReportDto> GetAsync(int year, int month, CancellationToken cancellationToken = default)
        {
            ValidateMonth(year, month);
            var stored = await _unitOfWork.Repository<LeaveCancellationReport>().Query()
                .FirstOrDefaultAsync(r => r.Year == year && r.Month == month, cancellationToken);
            if (stored is not null)
            {
                var dto = JsonSerializer.Deserialize<LeaveCancellationReportDto>(stored.PayloadJson, Json)!;
                dto.IsSnapshot = true;
                dto.GeneratedAtUtc = stored.GeneratedAtUtc;
                return dto;
            }

            var live = await BuildAsync(year, month, cancellationToken);
            live.IsSnapshot = false;
            live.GeneratedAtUtc = DateTime.UtcNow;
            return live;
        }

        public async Task<IReadOnlyList<LeaveCancellationReportMonthDto>> ListGeneratedAsync(CancellationToken cancellationToken = default)
        {
            var rows = await _unitOfWork.Repository<LeaveCancellationReport>().Query()
                .OrderByDescending(r => r.Year).ThenByDescending(r => r.Month)
                .ToListAsync(cancellationToken);
            return rows.Select(r => new LeaveCancellationReportMonthDto
            {
                Year = r.Year,
                Month = r.Month,
                GeneratedAtUtc = r.GeneratedAtUtc,
                TotalCancelledClasses = JsonSerializer.Deserialize<LeaveCancellationReportDto>(r.PayloadJson, Json)?.TotalCancelledClasses ?? 0,
            }).ToList();
        }

        public async Task<LeaveCancellationReportDto> GenerateAsync(int year, int month, Guid? generatedBy, CancellationToken cancellationToken = default)
        {
            ValidateMonth(year, month);
            var report = await BuildAsync(year, month, cancellationToken);
            var now = DateTime.UtcNow;
            report.GeneratedAtUtc = now;
            report.IsSnapshot = true;
            var payload = JsonSerializer.Serialize(report, Json);

            var existing = await _unitOfWork.Repository<LeaveCancellationReport>().TrackedQuery()
                .FirstOrDefaultAsync(r => r.Year == year && r.Month == month, cancellationToken);
            if (existing is null)
            {
                await _unitOfWork.Repository<LeaveCancellationReport>().AddAsync(new LeaveCancellationReport
                {
                    Year = year,
                    Month = month,
                    GeneratedAtUtc = now,
                    GeneratedBy = generatedBy,
                    PayloadJson = payload,
                }, cancellationToken);
            }
            else
            {
                existing.GeneratedAtUtc = now;
                existing.GeneratedBy = generatedBy;
                existing.PayloadJson = payload;
            }

            await _unitOfWork.SaveChangesAsync(cancellationToken);
            return report;
        }

        public async Task<IReadOnlyList<(int Year, int Month)>> EnsureSnapshotsAsync(DateTime nowUtc, CancellationToken cancellationToken = default)
        {
            var zone = TimeZoneInfo.FindSystemTimeZoneById(DateTimeDisplay.DefaultTimeZoneId);
            var local = TimeZoneInfo.ConvertTimeFromUtc(nowUtc, zone);

            var due = new List<(int Year, int Month)>();
            var isLastDay = local.Day == DateTime.DaysInMonth(local.Year, local.Month);
            var currentIsDue = isLastDay && local.Hour == 23 && local.Minute >= 59;
            if (currentIsDue)
            {
                due.Add((local.Year, local.Month));
            }

            var previous = new DateTime(local.Year, local.Month, 1);
            for (var i = 0; i < BackfillMonths; i++)
            {
                previous = previous.AddMonths(-1);
                due.Add((previous.Year, previous.Month));
            }

            var have = (await _unitOfWork.Repository<LeaveCancellationReport>().Query()
                    .Select(r => new { r.Year, r.Month })
                    .ToListAsync(cancellationToken))
                .Select(r => (r.Year, r.Month))
                .ToHashSet();

            var generated = new List<(int Year, int Month)>();
            foreach (var (year, month) in due.Where(m => !have.Contains(m)))
            {
                // A back-filled past month with no leave cancellations needs no empty snapshot;
                // the month-end run always writes one, so "nothing cancelled" is on record.
                var isCurrent = year == local.Year && month == local.Month;
                if (!isCurrent && !await HasAnyCancellationAsync(year, month, cancellationToken))
                {
                    continue;
                }

                await GenerateAsync(year, month, null, cancellationToken);
                generated.Add((year, month));
            }

            return generated;
        }

        private async Task<bool> HasAnyCancellationAsync(int year, int month, CancellationToken cancellationToken)
        {
            var (startUtc, endUtc) = MonthBoundsUtc(year, month);
            return await _unitOfWork.Repository<ClassSession>().Query()
                .AnyAsync(s => s.Status == SessionStatus.Cancelled
                    && s.ScheduledStartAtUtc >= startUtc && s.ScheduledStartAtUtc < endUtc
                    && s.CancellationReason != null
                    && (s.CancellationReason.Contains(WindowLeaveMarker) || s.CancellationReason.Contains(ClassWiseLeaveMarker)
                        || s.CancellationReason.Contains(LegacyWindowLeaveMarker) || s.CancellationReason.Contains(LegacyClassWiseLeaveMarker)),
                    cancellationToken);
        }

        private async Task<LeaveCancellationReportDto> BuildAsync(int year, int month, CancellationToken cancellationToken)
        {
            var (startUtc, endUtc) = MonthBoundsUtc(year, month);
            var zone = TimeZoneInfo.FindSystemTimeZoneById(DateTimeDisplay.DefaultTimeZoneId);

            var sessions = await _unitOfWork.Repository<ClassSession>().Query()
                .Include(s => s.Batch).ThenInclude(b => b!.Course)
                .Include(s => s.TeacherProfile).ThenInclude(t => t.User)
                .Where(s => s.Status == SessionStatus.Cancelled
                    && s.ScheduledStartAtUtc >= startUtc && s.ScheduledStartAtUtc < endUtc
                    && s.CancellationReason != null
                    && (s.CancellationReason.Contains(WindowLeaveMarker) || s.CancellationReason.Contains(ClassWiseLeaveMarker)
                        || s.CancellationReason.Contains(LegacyWindowLeaveMarker) || s.CancellationReason.Contains(LegacyClassWiseLeaveMarker)))
                .ToListAsync(cancellationToken);

            var teachers = sessions
                .GroupBy(s => s.TeacherProfileId)
                .Select(g =>
                {
                    var user = g.First().TeacherProfile.User;
                    var days = g
                        .Select(s => new { Session = s, Local = TimeZoneInfo.ConvertTimeFromUtc(s.ScheduledStartAtUtc, zone) })
                        .GroupBy(x => DateOnly.FromDateTime(x.Local))
                        .OrderBy(d => d.Key)
                        .Select(d => new LeaveCancellationDayDto
                        {
                            Date = d.Key,
                            CancelledClasses = d.Count(),
                            Classes = d.OrderBy(x => x.Local).Select(x => new LeaveCancellationClassDto
                            {
                                BatchName = x.Session.Batch?.Name ?? "(No batch)",
                                ClassType = x.Session.Batch is null ? "-" : x.Session.Batch.Course.Type.ToString(),
                                StartTime = x.Local.ToString("hh:mm tt"),
                            }).ToList(),
                        })
                        .ToList();
                    return new LeaveCancellationTeacherDto
                    {
                        TeacherProfileId = g.Key,
                        TeacherName = $"{user.FirstName} {user.LastName}".Trim(),
                        TotalCancelledClasses = days.Sum(d => d.CancelledClasses),
                        Days = days,
                    };
                })
                .OrderBy(t => t.TeacherName)
                .ToList();

            return new LeaveCancellationReportDto
            {
                Year = year,
                Month = month,
                TotalCancelledClasses = teachers.Sum(t => t.TotalCancelledClasses),
                Teachers = teachers,
            };
        }

        /// <summary>[start, end) of an IST calendar month as UTC instants.</summary>
        private static (DateTime StartUtc, DateTime EndUtc) MonthBoundsUtc(int year, int month)
        {
            var zone = TimeZoneInfo.FindSystemTimeZoneById(DateTimeDisplay.DefaultTimeZoneId);
            var start = new DateTime(year, month, 1, 0, 0, 0, DateTimeKind.Unspecified);
            return (TimeZoneInfo.ConvertTimeToUtc(start, zone), TimeZoneInfo.ConvertTimeToUtc(start.AddMonths(1), zone));
        }

        private static void ValidateMonth(int year, int month)
        {
            if (month is < 1 or > 12 || year is < 2020 or > 2100)
            {
                throw new DomainValidationException("Pick a valid month.");
            }
        }
    }
}
