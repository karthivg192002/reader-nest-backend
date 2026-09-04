using iucs.readernest.application.Common;
using iucs.readernest.application.Common.Exceptions;
using iucs.readernest.application.Dto.Billing;
using iucs.readernest.application.Dto.Portal;
using iucs.readernest.application.Dto.Resources;
using iucs.readernest.application.Dto.Sessions;
using iucs.readernest.application.Mappings;
using iucs.readernest.domain.Entities.Academics;
using iucs.readernest.domain.Entities.Admission;
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

            // Loaded once for the whole sibling group and matched per child in memory below --
            // same reasoning as the batch/session/attendance queries further down. A suspension
            // with ChildId null applies to every child; one with ChildId set only to that child.
            var activeSuspensions = await _unitOfWork.Repository<FeeSuspension>().Query()
                .Where(s => s.ParentProfileId == parent.Id && s.Status == SuspensionStatus.Active)
                .ToListAsync(cancellationToken);
            var accountWideSuspension = activeSuspensions.FirstOrDefault(s => s.ChildId is null);

            // Same reasoning: per-child invoice status (a sibling's overdue invoice must never
            // read as *this* child's own status), plus family-level (ChildId null) invoices,
            // which affect every child the same way an account-wide suspension does.
            var invoiceStatusByChild = await _unitOfWork.Repository<Invoice>().Query()
                .Where(i => i.ParentProfileId == parent.Id)
                .Select(i => new { i.ChildId, i.Status })
                .ToListAsync(cancellationToken);
            var familyLevelHasOverdue = invoiceStatusByChild.Any(i => i.ChildId is null && i.Status == InvoiceStatus.Overdue);
            var familyLevelHasDue = invoiceStatusByChild.Any(i => i.ChildId is null
                && (i.Status == InvoiceStatus.Pending || i.Status == InvoiceStatus.PartiallyPaid));

            var children = await _unitOfWork.Repository<Child>().Query()
                .Where(c => c.ParentProfileId == parent.Id)
                .OrderBy(c => c.FirstName)
                .ToListAsync(cancellationToken);

            // Three set-based queries for the whole sibling group, instead of four per child.
            // The per-child version issued 4N round trips (enrollments, completed count,
            // upcoming count, attendance rows) and pulled every attendance row a child has
            // ever had into memory just to average it — the counting now happens in SQL.
            var childIds = children.Select(c => c.Id).ToList();

            // EnrolledAtUtc (BatchEnrollment.CreatedAtUtc) scopes which of the batch's sessions
            // actually count for THIS child below — a batch that's been running for weeks
            // before a new child transfers in must not credit them with classes that happened
            // before they joined. (Known, accepted limitation: re-activating a previously
            // withdrawn enrollment updates the same row in place — see AssignStudentAsync — so
            // CreatedAtUtc reflects the child's first-ever join date, not a later
            // re-activation. Re-enrollment timing is a separate concern from the bug this fixes.)
            var enrollments = await _unitOfWork.Repository<BatchEnrollment>().Query()
                .Where(e => childIds.Contains(e.ChildId) && e.Status == EnrollmentStatus.Active)
                .Select(e => new { e.ChildId, e.BatchId, EnrolledAtUtc = e.CreatedAtUtc })
                .ToListAsync(cancellationToken);
            var batchIdsByChild = enrollments
                .GroupBy(e => e.ChildId)
                .ToDictionary(g => g.Key, g => g.Select(e => (e.BatchId, e.EnrolledAtUtc)).ToList());
            var allBatchIds = enrollments.Select(e => e.BatchId).Distinct().ToList();

            // Raw session rows per batch (status + start time only) instead of a pre-aggregated
            // per-batch count: whether a given session counts for a given child now depends on
            // that child's OWN EnrolledAtUtc, which can differ between two children in the same
            // batch, so the count can no longer be computed once and shared across every child
            // enrolled in it. Still one query for the whole sibling group, not one per child —
            // the per-child filtering below runs in memory over an already-fetched, bounded list.
            var sessionsByBatch = (await _unitOfWork.Repository<ClassSession>().Query()
                    .Where(s => s.BatchId != null && allBatchIds.Contains(s.BatchId.Value)
                                && (s.Status == SessionStatus.Completed
                                    || s.Status == SessionStatus.Scheduled
                                    || s.Status == SessionStatus.CarriedForward))
                    .Select(s => new { BatchId = s.BatchId!.Value, s.Status, s.ScheduledStartAtUtc })
                    .ToListAsync(cancellationToken))
                .GroupBy(s => s.BatchId)
                .ToDictionary(g => g.Key, g => g.ToList());

            var attendanceByChild = await _unitOfWork.Repository<SessionAttendance>().Query()
                .Where(a => a.ChildId != null && childIds.Contains(a.ChildId.Value))
                .GroupBy(a => a.ChildId!.Value)
                .Select(g => new
                {
                    ChildId = g.Key,
                    Total = g.Count(),
                    Present = g.Count(a => a.Status != AttendanceStatus.Absent),
                })
                .ToDictionaryAsync(x => x.ChildId, cancellationToken);

            var summaries = new List<ParentChildSummaryDto>(children.Count);
            foreach (var child in children)
            {
                var completed = 0;
                var upcoming = 0;
                if (batchIdsByChild.TryGetValue(child.Id, out var childBatches))
                {
                    foreach (var (batchId, enrolledAtUtc) in childBatches)
                    {
                        if (!sessionsByBatch.TryGetValue(batchId, out var sessions))
                        {
                            continue;
                        }

                        foreach (var s in sessions)
                        {
                            if (s.ScheduledStartAtUtc < enrolledAtUtc)
                            {
                                continue; // happened before this child actually joined the batch
                            }

                            if (s.Status == SessionStatus.Completed) completed++;
                            else upcoming++; // Scheduled or CarriedForward
                        }
                    }
                }

                attendanceByChild.TryGetValue(child.Id, out var attendance);
                // Null (not a vacuous 100%) when there's no attendance data yet -- a child
                // with zero completed sessions hasn't earned a perfect score, they just
                // haven't been measured. Same fix already applied to teacher utilization
                // (ReportsService's TeacherPerformanceDto) -- see that DTO's own doc comment.
                double? attendancePercent = attendance is null || attendance.Total == 0
                    ? null
                    : Math.Round(100.0 * attendance.Present / attendance.Total, 1);

                var childSuspension = accountWideSuspension ?? activeSuspensions.FirstOrDefault(s => s.ChildId == child.Id);
                var childHasOverdue = familyLevelHasOverdue || invoiceStatusByChild.Any(i => i.ChildId == child.Id && i.Status == InvoiceStatus.Overdue);
                var childHasDue = familyLevelHasDue || invoiceStatusByChild.Any(i => i.ChildId == child.Id
                    && (i.Status == InvoiceStatus.Pending || i.Status == InvoiceStatus.PartiallyPaid));
                var childFeeStatus = childSuspension is not null ? "suspended" : childHasOverdue ? "overdue" : childHasDue ? "due" : "paid";

                summaries.Add(new ParentChildSummaryDto
                {
                    ChildId = child.Id,
                    Name = $"{child.FirstName} {child.LastName}".Trim(),
                    AcademicLevel = child.AcademicLevel,
                    ClassesCompleted = completed,
                    ClassesRemaining = upcoming,
                    AttendancePercent = attendancePercent,
                    FeeStatus = childFeeStatus,
                    IsSuspended = childSuspension is not null,
                    SuspendedInvoiceId = childSuspension?.InvoiceId,
                });
            }

            var allSuspended = children.Count > 0 && summaries.All(c => c.IsSuspended);
            return new ParentDashboardDto
            {
                ParentProfileId = parent.Id,
                EnrollmentFormCompleted = parent.EnrollmentFormCompleted,
                IsSuspended = summaries.Any(c => c.IsSuspended),
                AllChildrenSuspended = allSuspended,
                SuspendedInvoiceId = allSuspended ? accountWideSuspension?.InvoiceId : null,
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

            var enrollments = await _unitOfWork.Repository<BatchEnrollment>().Query()
                .Where(e => e.Child.ParentProfileId == parent.Id && e.Status == EnrollmentStatus.Active)
                .Select(e => new { e.BatchId, e.ChildId })
                .ToListAsync(cancellationToken);
            var batchIds = enrollments.Select(e => e.BatchId).Distinct().ToList();
            // A batch can hold more than one of this parent's children (siblings sharing a
            // batch), so each batch maps to a list of child ids, not a single one.
            var childIdsByBatch = enrollments
                .GroupBy(e => e.BatchId)
                .ToDictionary(g => g.Key, g => g.Select(e => e.ChildId).ToList());

            var parentEmail = await _unitOfWork.Repository<User>().Query()
                .Where(u => u.Id == parentUserId)
                .Select(u => u.Email)
                .FirstOrDefaultAsync(cancellationToken);

            // Demo bookings track the lead by email (the parent may not have had an
            // enrolled child/batch yet when the demo was scheduled), so their session
            // is not reachable via BatchEnrollment and must be unioned in separately.
            // DemoBooking has no Child FK -- only this free-text name -- so it's carried
            // through to the DTO too, letting the portal at least label whose demo this is
            // when a family has more than one child (ChildIds itself always stays empty for
            // these, same as before).
            var demoChildNameBySession = parentEmail is null
                ? new Dictionary<Guid, string>()
                : await _unitOfWork.Repository<DemoBooking>().Query()
                    .Where(d => d.ClassSessionId != null && d.ParentEmail == parentEmail)
                    .Select(d => new { SessionId = d.ClassSessionId!.Value, d.ChildName })
                    .ToDictionaryAsync(d => d.SessionId, d => d.ChildName, cancellationToken);
            var demoSessionIds = demoChildNameBySession.Keys.ToList();

            var sessions = await _unitOfWork.Repository<ClassSession>().Query()
                .Include(s => s.Batch)
                .Include(s => s.TeacherProfile).ThenInclude(t => t.User)
                .Where(s => (s.BatchId != null && batchIds.Contains(s.BatchId.Value) || demoSessionIds.Contains(s.Id))
                            && s.ScheduledStartAtUtc < toUtc && s.ScheduledEndAtUtc > fromUtc)
                .OrderBy(s => s.ScheduledStartAtUtc)
                .ToListAsync(cancellationToken);

            var activeRecordings = await SessionRecordingLookup.ActiveRecordingsBySessionAsync(
                _unitOfWork, sessions.Select(s => s.Id), cancellationToken);

            return sessions
                .Select(s =>
                {
                    var dto = s.ToDto(
                        activeRecordings.GetValueOrDefault(s.Id),
                        activeRecordings.ContainsKey(s.Id),
                        demoChildNameBySession.GetValueOrDefault(s.Id));
                    if (s.BatchId is { } batchId && childIdsByBatch.TryGetValue(batchId, out var childIds))
                    {
                        dto.ChildIds = childIds;
                    }
                    return dto;
                })
                .ToList();
        }

        public async Task<IReadOnlyList<ResourceDto>> GetResourcesAsync(
            Guid parentUserId,
            CancellationToken cancellationToken = default)
        {
            var parent = await GetParentAsync(parentUserId, cancellationToken);

            // Account-wide suspension (family-level invoice overdue) blocks every child's
            // content outright. A per-child suspension only blocks resources reachable through
            // THAT child's own batches -- filtered below rather than thrown here, since a
            // sibling's fees being current must still see their own resources.
            if (await SuspensionCheck.IsAccountBlockedAsync(_unitOfWork, parent.Id, cancellationToken))
            {
                throw new DomainValidationException("Content access is suspended until the pending fee is settled.");
            }

            var granted = await _unitOfWork.Repository<ResourceAccess>().Query()
                .Include(a => a.Resource)
                .Where(a => a.ParentProfileId == parent.Id && a.VisibleOnDashboard)
                .Select(a => a.Resource)
                .ToListAsync(cancellationToken);

            // Multi-batch visibility: resources the teacher made visible to a batch reach every
            // parent with an actively enrolled, non-suspended child in that batch (no grant
            // needed). ResourceAccess (granted, above) has no ChildId to filter by, so a direct
            // grant stays visible regardless of which child triggered a per-child suspension --
            // a known, narrow gap versus the batch-derived list below, which resolves cleanly.
            var suspendedChildIds = (await _unitOfWork.Repository<FeeSuspension>().Query()
                    .Where(s => s.ParentProfileId == parent.Id && s.Status == SuspensionStatus.Active && s.ChildId != null)
                    .Select(s => s.ChildId!.Value)
                    .ToListAsync(cancellationToken))
                .ToHashSet();
            var enrolledBatchIds = _unitOfWork.Repository<BatchEnrollment>().Query()
                .Where(e => e.Status == EnrollmentStatus.Active
                    && e.Child.ParentProfileId == parent.Id
                    && !suspendedChildIds.Contains(e.ChildId))
                .Select(e => e.BatchId);
            var batchVisible = await _unitOfWork.Repository<ResourceBatchVisibility>().Query()
                .Where(v => enrolledBatchIds.Contains(v.BatchId))
                .Select(v => v.Resource)
                .ToListAsync(cancellationToken);

            return granted.Concat(batchVisible)
                .GroupBy(r => r.Id)
                .Select(g => g.First().ToDto())
                .ToList();
        }

        public async Task<IReadOnlyList<InvoiceDto>> GetInvoicesAsync(
            Guid parentUserId,
            CancellationToken cancellationToken = default)
        {
            var parent = await GetParentAsync(parentUserId, cancellationToken);
            var invoices = await _unitOfWork.Repository<Invoice>().Query()
                .Where(i => i.ParentProfileId == parent.Id)
                .Include(i => i.Child)
                .Include(i => i.Subscription).ThenInclude(s => s!.PackagePlan).ThenInclude(p => p.Course)
                .OrderByDescending(i => i.IssuedAtUtc)
                .ToListAsync(cancellationToken);
            return invoices.Select(i => i.ToDto()).ToList();
        }

        public async Task<ResourceDto> GetResourceForDownloadAsync(
            Guid parentUserId,
            Guid resourceId,
            CancellationToken cancellationToken = default)
        {
            var parent = await GetParentAsync(parentUserId, cancellationToken);

            if (await SuspensionCheck.IsAccountBlockedAsync(_unitOfWork, parent.Id, cancellationToken))
            {
                throw new DomainValidationException("Content access is suspended until the pending fee is settled.");
            }

            // Same two paths GetResourcesAsync lists from: an explicit per-parent grant, or
            // batch-wide visibility reaching every parent with an actively enrolled child in
            // that batch. Checking only ResourceAccess here (as this used to) meant a resource
            // shared the primary way — via batch visibility, no per-parent grant — could never
            // actually be downloaded despite showing up in the parent's resource list.
            var direct = await _unitOfWork.Repository<ResourceAccess>().Query()
                .Include(a => a.Resource)
                .FirstOrDefaultAsync(a => a.ParentProfileId == parent.Id && a.ResourceId == resourceId, cancellationToken);

            var resource = direct?.Resource;
            if (resource is null)
            {
                // A direct grant (ResourceAccess) has no ChildId to check per-child, so only
                // this batch-visibility path can resolve which child grants access -- and
                // therefore whether it's blocked by THAT child's own suspension specifically.
                var enrolledBatches = await _unitOfWork.Repository<BatchEnrollment>().Query()
                    .Where(e => e.Status == EnrollmentStatus.Active && e.Child.ParentProfileId == parent.Id)
                    .Select(e => new { e.BatchId, e.ChildId })
                    .ToListAsync(cancellationToken);
                var enrolledBatchIds = enrolledBatches.Select(e => e.BatchId).ToList();
                var candidate = await _unitOfWork.Repository<ResourceBatchVisibility>().Query()
                    .Include(v => v.Resource)
                    .Where(v => v.ResourceId == resourceId && enrolledBatchIds.Contains(v.BatchId))
                    .FirstOrDefaultAsync(cancellationToken);
                if (candidate is not null)
                {
                    var grantingChildId = enrolledBatches.First(e => e.BatchId == candidate.BatchId).ChildId;
                    if (await SuspensionCheck.IsChildBlockedAsync(_unitOfWork, parent.Id, grantingChildId, cancellationToken))
                    {
                        throw new DomainValidationException("Content access is suspended until the pending fee is settled.");
                    }
                }
                resource = candidate?.Resource;
            }

            if (resource is null)
            {
                throw new NotFoundException("This resource has not been shared with your account.");
            }

            if (!resource.IsDownloadable)
            {
                throw new DomainValidationException("This resource is view-only and cannot be downloaded.");
            }

            return resource.ToDto();
        }

        public async Task<IReadOnlyList<SessionRecordingDto>> GetRecordingsAsync(
            Guid parentUserId, Guid sessionId, CancellationToken cancellationToken = default)
        {
            var parent = await GetParentAsync(parentUserId, cancellationToken);

            var session = await _unitOfWork.Repository<ClassSession>().GetByIdAsync(sessionId, cancellationToken)
                ?? throw new NotFoundException(nameof(ClassSession), sessionId);

            var childIdsInBatch = session.BatchId.HasValue
                ? await _unitOfWork.Repository<BatchEnrollment>().Query()
                    .Where(e => e.BatchId == session.BatchId.Value
                        && e.Status == EnrollmentStatus.Active
                        && e.Child.ParentProfileId == parent.Id)
                    .Select(e => e.ChildId)
                    .ToListAsync(cancellationToken)
                : [];
            if (childIdsInBatch.Count == 0)
            {
                throw new NotFoundException("This session's recordings have not been shared with your account.");
            }

            // A batch can hold more than one of this parent's children (siblings sharing a
            // batch) -- blocked only if EVERY one of them is currently suspended; one sibling's
            // fees being current is enough to keep this recording reachable.
            var anyChildAllowed = false;
            foreach (var childId in childIdsInBatch)
            {
                if (!await SuspensionCheck.IsChildBlockedAsync(_unitOfWork, parent.Id, childId, cancellationToken))
                {
                    anyChildAllowed = true;
                    break;
                }
            }
            if (!anyChildAllowed)
            {
                throw new DomainValidationException("Content access is suspended until the pending fee is settled.");
            }

            var now = DateTime.UtcNow;
            var recordings = await _unitOfWork.Repository<SessionRecording>().Query()
                .Where(r => r.ClassSessionId == sessionId && (r.ExpiresAtUtc == null || r.ExpiresAtUtc > now))
                .OrderByDescending(r => r.CreatedAtUtc)
                .ToListAsync(cancellationToken);

            return recordings.Select(r => new SessionRecordingDto
            {
                Id = r.Id,
                ClassSessionId = r.ClassSessionId,
                StorageUrl = r.StorageUrl,
                DurationSeconds = r.DurationSeconds,
                ExpiresAtUtc = r.ExpiresAtUtc,
                CreatedAtUtc = r.CreatedAtUtc,
            }).ToList();
        }

        private async Task<ParentProfile> GetParentAsync(Guid parentUserId, CancellationToken cancellationToken)
        {
            return await _unitOfWork.Repository<ParentProfile>()
                .FirstOrDefaultAsync(p => p.UserId == parentUserId, cancellationToken)
                ?? throw new NotFoundException("No parent profile is linked to the current account.");
        }
    }
}
