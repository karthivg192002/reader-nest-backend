using iucs.readernest.application.Common.Exceptions;
using iucs.readernest.application.Dto.Batches;
using iucs.readernest.application.Mappings;
using iucs.readernest.domain.Entities.Academics;
using iucs.readernest.domain.Entities.Billing;
using iucs.readernest.domain.Entities.Sessions;
using iucs.readernest.domain.Entities.Users;
using iucs.readernest.domain.Enums;
using iucs.readernest.domain.Repository;
using Microsoft.EntityFrameworkCore;

namespace iucs.readernest.application.Services
{
    public class BatchService : IBatchService
    {
        private readonly IUnitOfWork _unitOfWork;
        private readonly IAuditLogService _auditLog;
        private readonly INotificationService _notificationService;

        public BatchService(IUnitOfWork unitOfWork, IAuditLogService auditLog, INotificationService notificationService)
        {
            _unitOfWork = unitOfWork;
            _auditLog = auditLog;
            _notificationService = notificationService;
        }

        public async Task<IReadOnlyList<BatchDto>> ListAsync(BatchStatus? status, CancellationToken cancellationToken = default)
        {
            var query = BaseQuery();
            if (status.HasValue)
            {
                query = query.Where(b => b.Status == status.Value);
            }

            var batches = await query.OrderBy(b => b.Name).ToListAsync(cancellationToken);
            return batches.Select(b => b.ToDto(ActiveEnrollmentCount(b))).ToList();
        }

        public async Task<BatchDto> GetAsync(Guid id, CancellationToken cancellationToken = default)
        {
            var batch = await BaseQuery().FirstOrDefaultAsync(b => b.Id == id, cancellationToken)
                ?? throw new NotFoundException(nameof(Batch), id);

            return batch.ToDto(ActiveEnrollmentCount(batch));
        }

        /// <summary>A withdrawn student must free their seat — only Active rows count against capacity.</summary>
        private static int ActiveEnrollmentCount(Batch batch) =>
            batch.Enrollments.Count(e => e.Status == EnrollmentStatus.Active);

        public async Task<BatchDto> CreateAsync(SaveBatchRequest request, CancellationToken cancellationToken = default)
        {
            var course = await ValidateAsync(request, cancellationToken);

            var batch = new Batch
            {
                CourseId = request.CourseId,
                TeacherProfileId = request.TeacherProfileId,
                Name = request.Name.Trim(),
                // Individual courses always run one student per batch
                Capacity = course.Type == CourseType.Individual ? 1 : request.Capacity,
                StartDate = request.StartDate,
                EndDate = request.EndDate,
            };
            await _unitOfWork.Repository<Batch>().AddAsync(batch, cancellationToken);
            await _auditLog.StageAsync(AuditAction.Create, nameof(Batch), batch.Id.ToString(), cancellationToken: cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return await GetAsync(batch.Id, cancellationToken);
        }

        public async Task<BatchDto> UpdateAsync(Guid id, SaveBatchRequest request, CancellationToken cancellationToken = default)
        {
            var course = await ValidateAsync(request, cancellationToken);
            var batch = await _unitOfWork.Repository<Batch>().GetByIdAsync(id, cancellationToken)
                ?? throw new NotFoundException(nameof(Batch), id);

            var newCapacity = course.Type == CourseType.Individual ? 1 : request.Capacity;
            var activeCount = await _unitOfWork.Repository<BatchEnrollment>().Query()
                .CountAsync(e => e.BatchId == id && e.Status == EnrollmentStatus.Active, cancellationToken);
            if (newCapacity < activeCount)
            {
                throw new DomainValidationException(
                    $"Batch '{batch.Name}' has {activeCount} active student(s); capacity can't be set below that " +
                    $"(withdraw students first). Switching an Individual course batch to Group (or back) is " +
                    $"blocked the same way once more than one student is enrolled.");
            }

            batch.CourseId = request.CourseId;
            batch.TeacherProfileId = request.TeacherProfileId;
            batch.Name = request.Name.Trim();
            batch.Capacity = newCapacity;
            batch.StartDate = request.StartDate;
            batch.EndDate = request.EndDate;

            await _auditLog.StageAsync(AuditAction.Update, nameof(Batch), batch.Id.ToString(), cancellationToken: cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return await GetAsync(batch.Id, cancellationToken);
        }

        public async Task<BatchDto> SetStatusAsync(Guid id, BatchStatus status, CancellationToken cancellationToken = default)
        {
            var batch = await _unitOfWork.Repository<Batch>().GetByIdAsync(id, cancellationToken)
                ?? throw new NotFoundException(nameof(Batch), id);

            batch.Status = status;
            if (status == BatchStatus.Dormant && batch.CompletedAtUtc is null)
            {
                // Marks when this batch stopped running — informational only; each
                // SessionRecording carries its own independent 15-day ExpiresAtUtc set at
                // upload time (SessionService.AddRecordingAsync), so this field doesn't gate it.
                batch.CompletedAtUtc = DateTime.UtcNow;
            }

            // A batch that's Dormant/Archived isn't running classes anymore — a still-Scheduled
            // future session left on the calendar would keep blocking the teacher's other slots
            // and go out via reminders for a batch nobody's tracking. The natural
            // course-completion path (MoveBatchToDormantIfCourseCompletedAsync) never has any
            // future sessions left to cancel; this only bites the manual/early transition.
            if (status is BatchStatus.Dormant or BatchStatus.Archived)
            {
                var now = DateTime.UtcNow;
                var danglingSessions = await _unitOfWork.Repository<ClassSession>().TrackedQuery()
                    .Where(s => s.BatchId == id
                        && (s.Status == SessionStatus.Scheduled || s.Status == SessionStatus.CarriedForward)
                        && s.ScheduledStartAtUtc > now)
                    .ToListAsync(cancellationToken);

                foreach (var session in danglingSessions)
                {
                    session.Status = SessionStatus.Cancelled;
                    session.CancellationReason = $"Batch marked {status}.";
                }

                await ExpireSubscriptionsForCompletedBatchAsync(batch, cancellationToken);
            }

            await _auditLog.StageAsync(AuditAction.Update, nameof(Batch), batch.Id.ToString(), cancellationToken: cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return await GetAsync(batch.Id, cancellationToken);
        }

        /// <summary>
        /// A Subscription has no direct link to a Batch — only ChildId + PackagePlanId — so
        /// there's no FK this could join on directly. Instead: find the children who were
        /// actively enrolled in this (now finished) batch, then find their Active subscriptions
        /// whose plan is for the SAME course this batch just ran. Those subscriptions were
        /// paying for a course that has now finished, so leaving them Active would let
        /// BillingBackgroundService keep invoicing for it indefinitely.
        /// Edge case accepted: a child enrolled in two concurrent batches of the same course
        /// would have that subscription expired when either batch finishes first.
        /// </summary>
        private async Task ExpireSubscriptionsForCompletedBatchAsync(Batch batch, CancellationToken cancellationToken)
        {
            var childIds = await _unitOfWork.Repository<BatchEnrollment>().Query()
                .Where(e => e.BatchId == batch.Id && e.Status == EnrollmentStatus.Active)
                .Select(e => e.ChildId)
                .ToListAsync(cancellationToken);
            if (childIds.Count == 0)
            {
                return;
            }

            var subscriptions = await _unitOfWork.Repository<Subscription>().TrackedQuery()
                .Where(s => childIds.Contains(s.ChildId)
                    && s.Status == SubscriptionStatus.Active
                    && s.PackagePlan.CourseId == batch.CourseId)
                .ToListAsync(cancellationToken);

            foreach (var subscription in subscriptions)
            {
                subscription.Status = SubscriptionStatus.Expired;
                subscription.NextBillingAtUtc = null;
            }
        }

        public async Task<IReadOnlyList<BatchStudentDto>> ListEnrollmentsAsync(Guid batchId, CancellationToken cancellationToken = default)
        {
            var enrollments = await _unitOfWork.Repository<BatchEnrollment>().Query()
                .Where(e => e.BatchId == batchId && e.Status == EnrollmentStatus.Active)
                .Include(e => e.Child)
                .OrderBy(e => e.Child.FirstName).ThenBy(e => e.Child.LastName)
                .ToListAsync(cancellationToken);

            return enrollments.Select(e => e.ToDto()).ToList();
        }

        public async Task<IReadOnlyList<UnassignedChildDto>> ListUnassignedStudentsAsync(Guid batchId, CancellationToken cancellationToken = default)
        {
            var alreadyEnrolledChildIds = await _unitOfWork.Repository<BatchEnrollment>().Query()
                .Where(e => e.BatchId == batchId && e.Status == EnrollmentStatus.Active)
                .Select(e => e.ChildId)
                .ToListAsync(cancellationToken);

            var candidates = await _unitOfWork.Repository<Child>().Query()
                .Where(c => c.IsActive && !alreadyEnrolledChildIds.Contains(c.Id))
                .Include(c => c.ParentProfile).ThenInclude(p => p.User)
                .OrderBy(c => c.FirstName).ThenBy(c => c.LastName)
                .ToListAsync(cancellationToken);

            return candidates.Select(c => new UnassignedChildDto
            {
                ChildId = c.Id,
                ChildName = $"{c.FirstName} {c.LastName}".Trim(),
                ParentName = c.ParentProfile?.User is { } u ? $"{u.FirstName} {u.LastName}".Trim() : "—",
                AcademicLevel = c.AcademicLevel,
            }).ToList();
        }

        public async Task<BatchStudentDto> AssignStudentAsync(Guid batchId, Guid childId, CancellationToken cancellationToken = default)
        {
            // The capacity check (count active enrollments, compare to batch.Capacity) and the
            // enrollment insert below are a check-then-act pair with nothing atomic binding them
            // together — two admins/coordinators assigning two DIFFERENT children to the same
            // near-full batch within the same window can both read the same activeCount under
            // capacity, both pass the check, and both insert (the only unique index is
            // (BatchId, ChildId), which guards the same child twice, not the batch's headcount).
            // A 10-seat batch could silently fill to 11. SERIALIZABLE — with every read fresh
            // inside, not values fetched before this block started — makes Postgres abort one of
            // two truly concurrent attempts instead of letting both squeeze through.
            var (batch, child, enrollment) = await _unitOfWork.ExecuteInSerializableTransactionAsync(async ct =>
            {
                // Load tracked (Repository.Query() is AsNoTracking) — no Include, so nothing here
                // pulls the Enrollments navigation into the tracker just to mutate one row of it.
                var b = await _unitOfWork.Repository<Batch>().FirstOrDefaultAsync(x => x.Id == batchId, ct)
                    ?? throw new NotFoundException(nameof(Batch), batchId);

                if (b.Status != BatchStatus.Active)
                {
                    throw new DomainValidationException($"Batch '{b.Name}' is {b.Status} and cannot take new enrollments.");
                }

                var c = await _unitOfWork.Repository<Child>().Query()
                    .Include(x => x.ParentProfile).ThenInclude(p => p.User)
                    .FirstOrDefaultAsync(x => x.Id == childId, ct)
                    ?? throw new NotFoundException(nameof(Child), childId);

                // Tracked lookup for this exact pair — the only row we might mutate below. Loading
                // it via the Batch.Enrollments navigation instead would return a second, detached
                // copy whenever the row is already tracked elsewhere in this DbContext, and
                // Update()-ing that copy throws ("already tracked with the same key").
                var existing = await _unitOfWork.Repository<BatchEnrollment>()
                    .FirstOrDefaultAsync(x => x.BatchId == batchId && x.ChildId == childId, ct);
                if (existing?.Status == EnrollmentStatus.Active)
                {
                    throw new ConflictException($"{c.FirstName} is already enrolled in this batch.");
                }

                var activeCount = await _unitOfWork.Repository<BatchEnrollment>().Query()
                    .CountAsync(x => x.BatchId == batchId && x.Status == EnrollmentStatus.Active, ct);
                if (activeCount >= b.Capacity)
                {
                    throw new DomainValidationException(
                        $"Batch '{b.Name}' is at capacity ({b.Capacity}/{b.Capacity}). Increase capacity or choose another batch.");
                }

                BatchEnrollment e;
                if (existing is not null)
                {
                    // A unique (BatchId, ChildId) index means a previously withdrawn student is
                    // re-activated in place, never re-inserted.
                    existing.Status = EnrollmentStatus.Active;
                    _unitOfWork.Repository<BatchEnrollment>().Update(existing);
                    e = existing;
                }
                else
                {
                    e = new BatchEnrollment { BatchId = batchId, ChildId = childId, Status = EnrollmentStatus.Active };
                    await _unitOfWork.Repository<BatchEnrollment>().AddAsync(e, ct);
                }

                // AuditLog.EntityId is MaxLength(64) — a single Guid fits, "batchId:childId" (73
                // chars) doesn't. The enrollment's own id is enough to look the row up.
                await _auditLog.StageAsync(AuditAction.Create, nameof(BatchEnrollment), e.Id.ToString(), cancellationToken: ct);
                await _unitOfWork.SaveChangesAsync(ct);
                return (b, c, e);
            }, cancellationToken);

            // Outside the transaction: a retried attempt must not re-send this email.
            var parentUser = child.ParentProfile?.User;
            if (parentUser is not null)
            {
                await _notificationService.SendTemplatedEmailAsync(
                    parentUser.Id, parentUser.Email, NotificationType.General,
                    "batch-assignment",
                    new Dictionary<string, string>
                    {
                        ["ChildFirstName"] = child.FirstName,
                        ["ChildFullName"] = $"{child.FirstName} {child.LastName}",
                        ["BatchName"] = batch.Name,
                    },
                    cancellationToken);
            }

            // Build the DTO directly rather than assigning enrollment.Child = child: child
            // came from a no-tracking query, and pointing a TRACKED entity's navigation at a
            // detached instance makes EF try to attach that whole graph on the next
            // SaveChanges — corrupting the tracker if this Child is already tracked elsewhere
            // in the same DbContext (e.g. this same request/scope).
            return new BatchStudentDto
            {
                EnrollmentId = enrollment.Id,
                ChildId = child.Id,
                ChildName = $"{child.FirstName} {child.LastName}".Trim(),
                AcademicLevel = child.AcademicLevel,
                Status = enrollment.Status,
                EnrolledAtUtc = enrollment.CreatedAtUtc,
            };
        }

        public async Task RemoveStudentAsync(Guid batchId, Guid childId, CancellationToken cancellationToken = default)
        {
            var enrollment = await _unitOfWork.Repository<BatchEnrollment>().FirstOrDefaultAsync(
                e => e.BatchId == batchId && e.ChildId == childId && e.Status == EnrollmentStatus.Active,
                cancellationToken)
                ?? throw new NotFoundException("No active enrollment matches this batch and child.");

            enrollment.Status = EnrollmentStatus.Withdrawn;
            _unitOfWork.Repository<BatchEnrollment>().Update(enrollment);

            await _auditLog.StageAsync(AuditAction.Update, nameof(BatchEnrollment), enrollment.Id.ToString(),
                changesJson: "{\"status\":\"Withdrawn\"}", cancellationToken: cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }

        private IQueryable<Batch> BaseQuery()
        {
            return _unitOfWork.Repository<Batch>().Query()
                .Include(b => b.Course)
                .Include(b => b.TeacherProfile).ThenInclude(t => t.User)
                .Include(b => b.Enrollments);
        }

        private async Task<Course> ValidateAsync(SaveBatchRequest request, CancellationToken cancellationToken)
        {
            var course = await _unitOfWork.Repository<Course>().GetByIdAsync(request.CourseId, cancellationToken)
                ?? throw new NotFoundException(nameof(Course), request.CourseId);

            var teacherExists = await _unitOfWork.Repository<TeacherProfile>()
                .ExistsAsync(t => t.Id == request.TeacherProfileId, cancellationToken);
            if (!teacherExists)
            {
                throw new NotFoundException(nameof(TeacherProfile), request.TeacherProfileId);
            }

            return course;
        }
    }
}
