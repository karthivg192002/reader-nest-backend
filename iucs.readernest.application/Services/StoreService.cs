using iucs.readernest.application.Common.Exceptions;
using iucs.readernest.application.Dto.Admission;
using iucs.readernest.application.Helper;
using iucs.readernest.application.Mappings;
using iucs.readernest.domain.Entities.Admission;
using iucs.readernest.domain.Entities.Billing;
using iucs.readernest.domain.Entities.Sessions;
using iucs.readernest.domain.Entities.Users;
using iucs.readernest.domain.Enums;
using iucs.readernest.domain.Repository;
using Microsoft.EntityFrameworkCore;

namespace iucs.readernest.application.Services
{
    public class StoreService : IStoreService
    {
        // Demos booked publicly are always this length — the form only asks for a start
        // time, never a duration, so there is nothing client-controlled to trust here.
        private const int DemoDurationMinutes = 30;
        // Guardrails on an endpoint anyone on the internet can call: enough lead time for
        // a teacher to actually be auto-assigned and notified, and a window short enough
        // that the slot picker stays meaningful (not an open-ended spam surface).
        private static readonly TimeSpan MinLeadTime = TimeSpan.FromHours(2);
        private const int MaxLeadDays = 30;
        // The org runs on IST (DateTimeDisplay.DefaultTimeZoneId) with no configured business-hours
        // setting to read instead — 9am-7pm covers every teacher's actual class-day span today.
        private const int BusinessDayStartHour = 9;
        private const int BusinessDayEndHour = 19;

        private readonly IUnitOfWork _unitOfWork;
        private readonly IAuditLogService _auditLog;
        private readonly IDemoBookingService _demoBookingService;

        public StoreService(IUnitOfWork unitOfWork, IAuditLogService auditLog, IDemoBookingService demoBookingService)
        {
            _unitOfWork = unitOfWork;
            _auditLog = auditLog;
            _demoBookingService = demoBookingService;
        }

        public async Task<IReadOnlyList<StorePlanDto>> ListPublicPlansAsync(CancellationToken cancellationToken = default)
        {
            var plans = await _unitOfWork.Repository<PackagePlan>().Query()
                .Include(p => p.Course)
                .Where(p => p.IsActive)
                .OrderBy(p => p.Name)
                .ToListAsync(cancellationToken);
            return plans.Select(p => p.ToStoreDto()).ToList();
        }

        public async Task<StoreInquiryDto> CreateInquiryAsync(
            CreateStoreInquiryRequest request,
            CancellationToken cancellationToken = default)
        {
            var plan = await _unitOfWork.Repository<PackagePlan>()
                .FirstOrDefaultAsync(p => p.Id == request.PackagePlanId && p.IsActive, cancellationToken)
                ?? throw new NotFoundException("That plan is no longer available.");

            var inquiry = new StoreInquiry
            {
                PackagePlanId = plan.Id,
                ParentName = request.ParentName.Trim(),
                ParentEmail = request.ParentEmail.Trim().ToLowerInvariant(),
                ParentPhone = request.ParentPhone.Trim(),
                ChildName = request.ChildName.Trim(),
                ChildAge = request.ChildAge,
                Notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim(),
            };
            await _unitOfWork.Repository<StoreInquiry>().AddAsync(inquiry, cancellationToken);
            await _auditLog.StageAsync(AuditAction.Create, nameof(StoreInquiry), inquiry.Id.ToString(), cancellationToken: cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            inquiry.PackagePlan = plan;
            return inquiry.ToDto();
        }

        public async Task<StoreDemoBookingConfirmationDto> BookDemoAsync(
            CreateStoreDemoBookingRequest request,
            CancellationToken cancellationToken = default)
        {
            var now = DateTime.UtcNow;
            if (request.PreferredStartAtUtc < now + MinLeadTime)
            {
                throw new DomainValidationException(
                    $"Please pick a time at least {MinLeadTime.TotalHours:0} hours from now, so we can line up a teacher.");
            }

            if (request.PreferredStartAtUtc > now.AddDays(MaxLeadDays))
            {
                throw new DomainValidationException($"Please pick a time within the next {MaxLeadDays} days.");
            }

            // Delegates to the same booking logic the admission team's own scheduler uses
            // (auto-assign, session creation, confirmation email) — a public visitor never
            // gets to name a specific teacher or invite extra participants, unlike that flow.
            var booking = await _demoBookingService.CreateAsync(
                new CreateDemoBookingRequest
                {
                    ParentName = request.ParentName.Trim(),
                    ParentEmail = request.ParentEmail.Trim(),
                    ParentPhone = request.ParentPhone.Trim(),
                    ChildName = request.ChildName.Trim(),
                    ChildAge = request.ChildAge,
                    DepartmentId = request.DepartmentId,
                    TeacherProfileId = null,
                    ScheduledStartAtUtc = request.PreferredStartAtUtc,
                    ScheduledEndAtUtc = request.PreferredStartAtUtc.AddMinutes(DemoDurationMinutes),
                    Participants = [],
                },
                cancellationToken);

            return new StoreDemoBookingConfirmationDto
            {
                Id = booking.Id,
                ScheduledStartAtUtc = request.PreferredStartAtUtc,
                ScheduledEndAtUtc = request.PreferredStartAtUtc.AddMinutes(DemoDurationMinutes),
            };
        }

        public async Task<IReadOnlyList<AvailableDemoSlotDto>> ListAvailableDemoSlotsAsync(
            DateOnly date,
            Guid? departmentId,
            CancellationToken cancellationToken = default)
        {
            var zone = TimeZoneInfo.FindSystemTimeZoneById(DateTimeDisplay.DefaultTimeZoneId);
            var dayStartLocal = new DateTime(date.Year, date.Month, date.Day, BusinessDayStartHour, 0, 0, DateTimeKind.Unspecified);
            var dayEndLocal = new DateTime(date.Year, date.Month, date.Day, BusinessDayEndHour, 0, 0, DateTimeKind.Unspecified);
            var dayStartUtc = TimeZoneInfo.ConvertTimeToUtc(dayStartLocal, zone);
            var dayEndUtc = TimeZoneInfo.ConvertTimeToUtc(dayEndLocal, zone);

            var now = DateTime.UtcNow;
            var earliest = now + MinLeadTime;
            var latest = now.AddDays(MaxLeadDays);
            if (dayStartUtc > latest || dayEndUtc < earliest)
            {
                return [];
            }

            IQueryable<TeacherProfile> teachers = _unitOfWork.Repository<TeacherProfile>().Query()
                .Where(t => t.User.Status == UserStatus.Active);
            if (departmentId.HasValue)
            {
                teachers = teachers.Where(t => t.DepartmentId == departmentId.Value);
            }
            var teacherIds = await teachers.Select(t => t.Id).ToListAsync(cancellationToken);
            if (teacherIds.Count == 0)
            {
                return [];
            }

            // Every one of this day's sessions across the matching teachers, fetched once —
            // an in-memory overlap check per candidate slot is far cheaper than one query per
            // slot (up to 20 slots/day), same reasoning as AutoAssignTeacherAsync's single read.
            var busyWindows = await _unitOfWork.Repository<ClassSession>().Query()
                .Where(s => teacherIds.Contains(s.TeacherProfileId)
                    && (s.Status == SessionStatus.Scheduled || s.Status == SessionStatus.CarriedForward)
                    && s.ScheduledStartAtUtc < dayEndUtc
                    && s.ScheduledEndAtUtc > dayStartUtc)
                .Select(s => new { s.TeacherProfileId, s.ScheduledStartAtUtc, s.ScheduledEndAtUtc })
                .ToListAsync(cancellationToken);

            var slots = new List<AvailableDemoSlotDto>();
            for (var slotStart = dayStartUtc; slotStart.AddMinutes(DemoDurationMinutes) <= dayEndUtc; slotStart = slotStart.AddMinutes(DemoDurationMinutes))
            {
                if (slotStart < earliest || slotStart > latest)
                {
                    continue;
                }

                var slotEnd = slotStart.AddMinutes(DemoDurationMinutes);
                var hasFreeTeacher = teacherIds.Any(teacherId =>
                    !busyWindows.Any(w => w.TeacherProfileId == teacherId && w.ScheduledStartAtUtc < slotEnd && w.ScheduledEndAtUtc > slotStart));
                if (hasFreeTeacher)
                {
                    slots.Add(new AvailableDemoSlotDto { StartAtUtc = slotStart, EndAtUtc = slotEnd });
                }
            }

            return slots;
        }

        public async Task<IReadOnlyList<StoreInquiryDto>> ListInquiriesAsync(
            StoreInquiryStatus? status,
            CancellationToken cancellationToken = default)
        {
            IQueryable<StoreInquiry> query = _unitOfWork.Repository<StoreInquiry>().Query().Include(i => i.PackagePlan);
            if (status.HasValue)
            {
                query = query.Where(i => i.Status == status.Value);
            }

            var inquiries = await query.OrderByDescending(i => i.CreatedAtUtc).ToListAsync(cancellationToken);
            return inquiries.Select(i => i.ToDto()).ToList();
        }

        public async Task<StoreInquiryDto> UpdateInquiryStatusAsync(
            Guid id,
            UpdateStoreInquiryStatusRequest request,
            CancellationToken cancellationToken = default)
        {
            // Load tracked (Query()/BaseQuery is AsNoTracking; mutating that never persists).
            var inquiry = await _unitOfWork.Repository<StoreInquiry>().FirstOrDefaultAsync(i => i.Id == id, cancellationToken)
                ?? throw new NotFoundException(nameof(StoreInquiry), id);

            inquiry.Status = request.Status;
            await _auditLog.StageAsync(AuditAction.Update, nameof(StoreInquiry), inquiry.Id.ToString(), cancellationToken: cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            var withPlan = await _unitOfWork.Repository<StoreInquiry>().Query()
                .Include(i => i.PackagePlan)
                .FirstAsync(i => i.Id == id, cancellationToken);
            return withPlan.ToDto();
        }
    }
}
