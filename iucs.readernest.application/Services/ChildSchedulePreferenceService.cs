using System.Text.Json;
using System.Text.RegularExpressions;
using iucs.readernest.application.Common.Exceptions;
using iucs.readernest.application.Dto.Enrollment;
using iucs.readernest.domain.Entities.Academics;
using iucs.readernest.domain.Entities.Users;
using iucs.readernest.domain.Enums;
using iucs.readernest.domain.Repository;
using Microsoft.EntityFrameworkCore;

namespace iucs.readernest.application.Services
{
    public interface IChildSchedulePreferenceService
    {
        /// <summary>The child's saved details, or their basics pre-filled when nothing is saved yet.</summary>
        Task<ChildSchedulePreferenceDto> GetForParentAsync(Guid parentUserId, Guid childId, CancellationToken cancellationToken = default);

        /// <summary>Saves (creates or updates) the details for one of the caller's own children.</summary>
        Task<ChildSchedulePreferenceDto> SaveForParentAsync(
            Guid parentUserId, Guid childId, SaveChildSchedulePreferenceRequest request, CancellationToken cancellationToken = default);
    }

    public class ChildSchedulePreferenceService : IChildSchedulePreferenceService
    {
        public static readonly string[] WeekDays = ["Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun"];
        private static readonly Regex TimeRe = new(@"^([01]\d|2[0-3]):[0-5]\d$", RegexOptions.Compiled);

        private readonly IUnitOfWork _unitOfWork;
        private readonly IAuditLogService _auditLog;

        public ChildSchedulePreferenceService(IUnitOfWork unitOfWork, IAuditLogService auditLog)
        {
            _unitOfWork = unitOfWork;
            _auditLog = auditLog;
        }

        public async Task<ChildSchedulePreferenceDto> GetForParentAsync(
            Guid parentUserId, Guid childId, CancellationToken cancellationToken = default)
        {
            var child = await GetOwnedChildAsync(parentUserId, childId, tracked: false, cancellationToken);
            var pref = await _unitOfWork.Repository<ChildSchedulePreference>().Query()
                .Include(p => p.Course)
                .FirstOrDefaultAsync(p => p.ChildId == childId, cancellationToken);
            return ChildSchedulePreferenceMapper.ToDto(child, pref);
        }

        public async Task<ChildSchedulePreferenceDto> SaveForParentAsync(
            Guid parentUserId, Guid childId, SaveChildSchedulePreferenceRequest request, CancellationToken cancellationToken = default)
        {
            var (days, dayTimes) = ValidateSchedule(request);

            var today = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1)); // tolerate IST being ahead of UTC
            if (request.PreferredStartDate is { } start && start < today)
            {
                throw new DomainValidationException("The preferred start date can't be in the past.");
            }

            if (request.DateOfBirth is { } dob && dob > DateOnly.FromDateTime(DateTime.UtcNow))
            {
                throw new DomainValidationException("Date of birth can't be in the future.");
            }

            if (request.CourseId is { } courseId
                && !await _unitOfWork.Repository<Course>().ExistsAsync(c => c.Id == courseId, cancellationToken))
            {
                throw new NotFoundException(nameof(Course), courseId);
            }

            var child = await GetOwnedChildAsync(parentUserId, childId, tracked: true, cancellationToken);

            var pref = await _unitOfWork.Repository<ChildSchedulePreference>()
                .FirstOrDefaultAsync(p => p.ChildId == childId, cancellationToken);
            var isNew = pref is null;
            pref ??= new ChildSchedulePreference { ChildId = childId };

            pref.CourseId = request.CourseId;
            pref.PreferredStartDate = request.PreferredStartDate;
            pref.DaysPerWeek = request.DaysPerWeek;
            pref.PreferredDays = string.Join(',', days);
            pref.DayTimesJson = JsonSerializer.Serialize(dayTimes);
            // The times are the parent's local times: a family in Melbourne choosing "6:00 PM"
            // means 6 PM Melbourne (12:30 PM IST). Keep the zone so staff see both.
            pref.TimeZoneId = await ResolveTimeZoneAsync(parentUserId, request.TimeZoneId, cancellationToken);
            pref.SchoolName = Clean(request.SchoolName);
            pref.PriorExperience = Clean(request.PriorExperience);
            pref.Allergies = Clean(request.Allergies);
            pref.Notes = Clean(request.Notes);
            pref.SubmittedAtUtc = DateTime.UtcNow;

            // The child's own record carries the basics the parent just confirmed/filled in.
            if (request.DateOfBirth.HasValue) child.DateOfBirth = request.DateOfBirth;
            if (request.Gender.HasValue) child.Gender = request.Gender;
            if (!string.IsNullOrWhiteSpace(request.Grade)) child.AcademicLevel = request.Grade.Trim();

            if (isNew)
            {
                await _unitOfWork.Repository<ChildSchedulePreference>().AddAsync(pref, cancellationToken);
            }

            await _auditLog.StageAsync(isNew ? AuditAction.Create : AuditAction.Update, nameof(ChildSchedulePreference),
                pref.Id.ToString(), cancellationToken: cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return await GetForParentAsync(parentUserId, childId, cancellationToken);
        }

        /// <summary>The zone the parent picked in if it's a real one, else their account's zone.</summary>
        private async Task<string?> ResolveTimeZoneAsync(Guid parentUserId, string? requested, CancellationToken cancellationToken)
        {
            if (!string.IsNullOrWhiteSpace(requested) && TimeZoneInfo.TryFindSystemTimeZoneById(requested.Trim(), out _))
            {
                return requested.Trim();
            }

            return await _unitOfWork.Repository<User>().Query()
                .Where(u => u.Id == parentUserId)
                .Select(u => u.TimeZoneId)
                .FirstOrDefaultAsync(cancellationToken);
        }

        /// <summary>One valid time per chosen day, exactly <c>DaysPerWeek</c> distinct weekdays, returned in week order.</summary>
        private static (List<string> Days, Dictionary<string, string> Times) ValidateSchedule(SaveChildSchedulePreferenceRequest request)
        {
            var days = request.PreferredDays.Select(d => d?.Trim() ?? string.Empty).ToList();
            if (days.Any(d => !WeekDays.Contains(d)))
            {
                throw new DomainValidationException("Preferred days must be Mon, Tue, Wed, Thu, Fri, Sat or Sun.");
            }

            if (days.Distinct().Count() != days.Count)
            {
                throw new DomainValidationException("Each preferred day can only be chosen once.");
            }

            if (days.Count != request.DaysPerWeek)
            {
                throw new DomainValidationException($"Pick exactly {request.DaysPerWeek} day{(request.DaysPerWeek == 1 ? "" : "s")} to match days per week.");
            }

            var ordered = WeekDays.Where(days.Contains).ToList();
            var times = new Dictionary<string, string>();
            foreach (var day in ordered)
            {
                if (!request.DayTimes.TryGetValue(day, out var time) || !TimeRe.IsMatch(time ?? string.Empty))
                {
                    throw new DomainValidationException($"Choose a preferred time for {day}.");
                }

                times[day] = time!;
            }

            return (ordered, times);
        }

        private async Task<Child> GetOwnedChildAsync(Guid parentUserId, Guid childId, bool tracked, CancellationToken cancellationToken)
        {
            var parent = await _unitOfWork.Repository<ParentProfile>().Query()
                .FirstOrDefaultAsync(p => p.UserId == parentUserId, cancellationToken)
                ?? throw new NotFoundException("No parent profile is linked to the current account.");

            // Another family's child looks exactly like a missing one — never confirm it exists.
            var child = tracked
                ? await _unitOfWork.Repository<Child>().FirstOrDefaultAsync(c => c.Id == childId && c.ParentProfileId == parent.Id, cancellationToken)
                : await _unitOfWork.Repository<Child>().Query().FirstOrDefaultAsync(c => c.Id == childId && c.ParentProfileId == parent.Id, cancellationToken);
            return child ?? throw new NotFoundException(nameof(Child), childId);
        }

        private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    /// <summary>Shared by the parent form (this service) and the staff students directory.</summary>
    public static class ChildSchedulePreferenceMapper
    {
        public static ChildSchedulePreferenceDto ToDto(Child child, ChildSchedulePreference? pref)
        {
            var dto = new ChildSchedulePreferenceDto
            {
                ChildId = child.Id,
                ChildName = $"{child.FirstName} {child.LastName}".Trim(),
                DateOfBirth = child.DateOfBirth,
                Gender = child.Gender,
                Grade = child.AcademicLevel,
            };

            if (pref is null)
            {
                return dto;
            }

            dto.HasSubmitted = true;
            dto.SubmittedAtUtc = pref.SubmittedAtUtc;
            dto.CourseId = pref.CourseId;
            dto.CourseName = pref.Course?.Name;
            dto.PreferredStartDate = pref.PreferredStartDate;
            dto.DaysPerWeek = pref.DaysPerWeek;
            dto.PreferredDays = pref.PreferredDays.Split(',', StringSplitOptions.RemoveEmptyEntries).ToList();
            dto.DayTimes = JsonSerializer.Deserialize<Dictionary<string, string>>(pref.DayTimesJson) ?? [];
            dto.TimeZoneId = pref.TimeZoneId;
            dto.SchoolName = pref.SchoolName;
            dto.PriorExperience = pref.PriorExperience;
            dto.Allergies = pref.Allergies;
            dto.Notes = pref.Notes;
            return dto;
        }
    }
}
