using iucs.readernest.application.Common.Exceptions;
using iucs.readernest.application.Dto.Billing;
using iucs.readernest.application.Dto.Portal;
using iucs.readernest.application.Dto.Resources;
using iucs.readernest.application.Dto.Sessions;
using iucs.readernest.application.Mappings;
using iucs.readernest.domain.Entities.Academics;
using iucs.readernest.domain.Entities.Billing;
using iucs.readernest.domain.Entities.Resources;
using iucs.readernest.domain.Entities.Sessions;
using iucs.readernest.domain.Entities.Users;
using iucs.readernest.domain.Enums;
using iucs.readernest.domain.Repository;
using Microsoft.EntityFrameworkCore;

namespace iucs.readernest.application.Services
{
    public class ParentPortalService : IParentPortalService
    {
        private readonly IUnitOfWork _unitOfWork;

        public ParentPortalService(IUnitOfWork unitOfWork)
        {
            _unitOfWork = unitOfWork;
        }

        public async Task<ParentDashboardDto> GetDashboardAsync(Guid parentUserId, CancellationToken cancellationToken = default)
        {
            var parent = await GetParentAsync(parentUserId, cancellationToken);

            var suspension = await _unitOfWork.Repository<FeeSuspension>().FirstOrDefaultAsync(
                s => s.ParentProfileId == parent.Id && s.Status == SuspensionStatus.Active, cancellationToken);

            var hasOverdue = await _unitOfWork.Repository<Invoice>().ExistsAsync(
                i => i.ParentProfileId == parent.Id && i.Status == InvoiceStatus.Overdue, cancellationToken);
            var hasDue = await _unitOfWork.Repository<Invoice>().ExistsAsync(
                i => i.ParentProfileId == parent.Id
                     && (i.Status == InvoiceStatus.Pending || i.Status == InvoiceStatus.PartiallyPaid),
                cancellationToken);
            var accountFeeStatus = suspension is not null ? "suspended" : hasOverdue ? "overdue" : hasDue ? "due" : "paid";

            var children = await _unitOfWork.Repository<Child>().Query()
                .Where(c => c.ParentProfileId == parent.Id)
                .OrderBy(c => c.FirstName)
                .ToListAsync(cancellationToken);

            var summaries = new List<ParentChildSummaryDto>(children.Count);
            foreach (var child in children)
            {
                var batchIds = await _unitOfWork.Repository<BatchEnrollment>().Query()
                    .Where(e => e.ChildId == child.Id && e.Status == EnrollmentStatus.Active)
                    .Select(e => e.BatchId)
                    .ToListAsync(cancellationToken);

                var completed = await _unitOfWork.Repository<ClassSession>().Query()
                    .CountAsync(s => s.BatchId != null && batchIds.Contains(s.BatchId.Value)
                                     && s.Status == SessionStatus.Completed, cancellationToken);
                var upcoming = await _unitOfWork.Repository<ClassSession>().Query()
                    .CountAsync(s => s.BatchId != null && batchIds.Contains(s.BatchId.Value)
                                     && (s.Status == SessionStatus.Scheduled || s.Status == SessionStatus.CarriedForward),
                        cancellationToken);

                var attendanceRows = await _unitOfWork.Repository<SessionAttendance>().Query()
                    .Where(a => a.ChildId == child.Id)
                    .Select(a => a.Status)
                    .ToListAsync(cancellationToken);
                var attendancePercent = attendanceRows.Count == 0
                    ? 100
                    : Math.Round(100.0 * attendanceRows.Count(s => s != AttendanceStatus.Absent) / attendanceRows.Count, 1);

                summaries.Add(new ParentChildSummaryDto
                {
                    ChildId = child.Id,
                    Name = $"{child.FirstName} {child.LastName}".Trim(),
                    AcademicLevel = child.AcademicLevel,
                    ClassesCompleted = completed,
                    ClassesRemaining = upcoming,
                    AttendancePercent = attendancePercent,
                    FeeStatus = accountFeeStatus,
                });
            }

            return new ParentDashboardDto
            {
                ParentProfileId = parent.Id,
                EnrollmentFormCompleted = parent.EnrollmentFormCompleted,
                IsSuspended = suspension is not null,
                SuspendedInvoiceId = suspension?.InvoiceId,
                Children = summaries,
            };
        }

        public async Task<IReadOnlyList<ClassSessionDto>> GetScheduleAsync(
            Guid parentUserId,
            DateTime fromUtc,
            DateTime toUtc,
            CancellationToken cancellationToken = default)
        {
            var parent = await GetParentAsync(parentUserId, cancellationToken);

            var batchIds = await _unitOfWork.Repository<BatchEnrollment>().Query()
                .Where(e => e.Child.ParentProfileId == parent.Id && e.Status == EnrollmentStatus.Active)
                .Select(e => e.BatchId)
                .Distinct()
                .ToListAsync(cancellationToken);

            var sessions = await _unitOfWork.Repository<ClassSession>().Query()
                .Include(s => s.Batch)
                .Include(s => s.TeacherProfile).ThenInclude(t => t.User)
                .Where(s => s.BatchId != null && batchIds.Contains(s.BatchId.Value)
                            && s.ScheduledStartAtUtc < toUtc && s.ScheduledEndAtUtc > fromUtc)
                .OrderBy(s => s.ScheduledStartAtUtc)
                .ToListAsync(cancellationToken);

            return sessions.Select(s => s.ToDto()).ToList();
        }

        public async Task<IReadOnlyList<ResourceDto>> GetResourcesAsync(
            Guid parentUserId,
            CancellationToken cancellationToken = default)
        {
            var parent = await GetParentAsync(parentUserId, cancellationToken);

            // Fee suspension blocks content access until payment or admin restoration
            var suspended = await _unitOfWork.Repository<FeeSuspension>().ExistsAsync(
                s => s.ParentProfileId == parent.Id && s.Status == SuspensionStatus.Active, cancellationToken);
            if (suspended)
            {
                throw new DomainValidationException("Content access is suspended until the pending fee is settled.");
            }

            var resources = await _unitOfWork.Repository<ResourceAccess>().Query()
                .Include(a => a.Resource)
                .Where(a => a.ParentProfileId == parent.Id && a.VisibleOnDashboard)
                .Select(a => a.Resource)
                .ToListAsync(cancellationToken);

            return resources.Select(r => r.ToDto()).ToList();
        }

        public async Task<IReadOnlyList<InvoiceDto>> GetInvoicesAsync(
            Guid parentUserId,
            CancellationToken cancellationToken = default)
        {
            var parent = await GetParentAsync(parentUserId, cancellationToken);
            var invoices = await _unitOfWork.Repository<Invoice>().Query()
                .Where(i => i.ParentProfileId == parent.Id)
                .OrderByDescending(i => i.IssuedAtUtc)
                .ToListAsync(cancellationToken);
            return invoices.Select(i => i.ToDto()).ToList();
        }

        private async Task<ParentProfile> GetParentAsync(Guid parentUserId, CancellationToken cancellationToken)
        {
            return await _unitOfWork.Repository<ParentProfile>()
                .FirstOrDefaultAsync(p => p.UserId == parentUserId, cancellationToken)
                ?? throw new NotFoundException("No parent profile is linked to the current account.");
        }
    }
}
