using System.Text.Json;
using iucs.readernest.application.Common;
using iucs.readernest.application.Common.Exceptions;
using iucs.readernest.application.Common.Interfaces;
using iucs.readernest.application.Dto.Billing;
using iucs.readernest.application.Dto.Common;
using iucs.readernest.application.Dto.Enrollment;
using iucs.readernest.domain.Entities.Academics;
using iucs.readernest.domain.Entities.Admission;
using iucs.readernest.domain.Entities.Billing;
using iucs.readernest.domain.Entities.Users;
using iucs.readernest.domain.Enums;
using iucs.readernest.domain.Repository;
using Microsoft.EntityFrameworkCore;

namespace iucs.readernest.application.Services
{
    public class EnrollmentService : IEnrollmentService
    {
        private readonly IUnitOfWork _unitOfWork;
        private readonly IAuditLogService _auditLog;
        private readonly IBillingService _billingService;
        private readonly IBatchService _batchService;
        private readonly IBulkFileReader _bulkFileReader;

        public EnrollmentService(
            IUnitOfWork unitOfWork,
            IAuditLogService auditLog,
            IBillingService billingService,
            IBatchService batchService,
            IBulkFileReader bulkFileReader)
        {
            _unitOfWork = unitOfWork;
            _auditLog = auditLog;
            _billingService = billingService;
            _batchService = batchService;
            _bulkFileReader = bulkFileReader;
        }

        /// <summary>
        /// Answer keys the admin review screen is actually built around (docs/ENROLLMENT_FORM_FIELDS.md
        /// marks all four required) — display name, age and course all come straight from these.
        /// The wizard already enforces this client-side, but that's only ever a UX nicety: nothing
        /// stopped a direct API call from posting "{}" and landing a form the review queue can't
        /// meaningfully show (blank name, "Age 0", no course) or even approve into a real child
        /// record with any accuracy. Enforcing it here closes that off for every caller, not just
        /// the wizard.
        /// </summary>
        private static readonly string[] RequiredFormFields = ["childName", "dob", "grade", "courseInterest"];

        public async Task<EnrollmentFormDto> SubmitAsync(
            Guid parentUserId,
            SubmitEnrollmentFormRequest request,
            CancellationToken cancellationToken = default)
        {
            ValidateRequiredFields(request.FormDataJson);

            var parent = await GetParentAsync(parentUserId, cancellationToken);

            // A rejected form is resubmitted in place; otherwise every submission is a new child enrollment
            var form = await _unitOfWork.Repository<EnrollmentForm>().FirstOrDefaultAsync(
                f => f.ParentProfileId == parent.Id && f.Status == EnrollmentFormStatus.Rejected,
                cancellationToken);

            if (form is null)
            {
                form = new EnrollmentForm { ParentProfileId = parent.Id };

                // Best-effort traceability: if this parent booked a demo before an account
                // existed for them, link the two so approving this form can auto-mark that
                // lead Enrolled instead of leaving conversion tracking as a disconnected,
                // manually-set label with nothing behind it.
                var user = await _unitOfWork.Repository<User>().GetByIdAsync(parentUserId, cancellationToken);
                if (user is not null)
                {
                    // A correlated NOT EXISTS instead of pulling every ever-linked booking id
                    // into memory first — that list only grows, system-wide, across all-time
                    // enrollments, on every single submission.
                    var enrollmentForms = _unitOfWork.Repository<EnrollmentForm>().Query();
                    var matchedBooking = await _unitOfWork.Repository<DemoBooking>().Query()
                        .Where(b => b.ParentEmail == user.Email
                            && !enrollmentForms.Any(f => f.DemoBookingId == b.Id))
                        .OrderByDescending(b => b.CreatedAtUtc)
                        .FirstOrDefaultAsync(cancellationToken);
                    form.DemoBookingId = matchedBooking?.Id;
                }

                await _unitOfWork.Repository<EnrollmentForm>().AddAsync(form, cancellationToken);
            }

            form.FormDataJson = request.FormDataJson;
            form.Status = EnrollmentFormStatus.Submitted;
            form.SubmittedAtUtc = DateTime.UtcNow;

            await _auditLog.StageAsync(AuditAction.Create, nameof(EnrollmentForm), form.Id.ToString(), cancellationToken: cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return await GetAsync(form.Id, cancellationToken);
        }

        public async Task<IReadOnlyList<EnrollmentFormDto>> ListForParentUserAsync(
            Guid parentUserId,
            CancellationToken cancellationToken = default)
        {
            var parent = await GetParentAsync(parentUserId, cancellationToken);
            var forms = await BaseQuery()
                .Where(f => f.ParentProfileId == parent.Id)
                .OrderByDescending(f => f.CreatedAtUtc)
                .ToListAsync(cancellationToken);
            return forms.Select(ToDto).ToList();
        }

        public async Task<IReadOnlyList<EnrollmentFormDto>> ListAsync(
            EnrollmentFormStatus? status,
            CancellationToken cancellationToken = default)
        {
            IQueryable<EnrollmentForm> query = BaseQuery();
            if (status.HasValue)
            {
                query = query.Where(f => f.Status == status.Value);
            }

            var forms = await query.OrderByDescending(f => f.SubmittedAtUtc).ToListAsync(cancellationToken);
            return forms.Select(ToDto).ToList();
        }

        public async Task<EnrollmentFormDto> GetAsync(Guid id, CancellationToken cancellationToken = default)
        {
            var form = await BaseQuery().FirstOrDefaultAsync(f => f.Id == id, cancellationToken)
                ?? throw new NotFoundException(nameof(EnrollmentForm), id);
            return ToDto(form);
        }

        public async Task<EnrollmentFormDto> UpdateFormDataAsync(
            Guid id,
            SubmitEnrollmentFormRequest request,
            CancellationToken cancellationToken = default)
        {
            ValidateRequiredFields(request.FormDataJson);

            // Load tracked so the mutation persists (BaseQuery is AsNoTracking).
            var form = await _unitOfWork.Repository<EnrollmentForm>()
                .FirstOrDefaultAsync(f => f.Id == id, cancellationToken)
                ?? throw new NotFoundException(nameof(EnrollmentForm), id);

            if (form.Status == EnrollmentFormStatus.Approved)
            {
                throw new ConflictException("An approved enrollment form can no longer be edited.");
            }

            form.FormDataJson = request.FormDataJson;
            _unitOfWork.Repository<EnrollmentForm>().Update(form);

            await _auditLog.StageAsync(AuditAction.Update, nameof(EnrollmentForm), form.Id.ToString(), cancellationToken: cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return await GetAsync(form.Id, cancellationToken);
        }

        public async Task<EnrollmentFormDto> ReviewAsync(
            Guid id,
            ReviewEnrollmentFormRequest request,
            CancellationToken cancellationToken = default)
        {
            // Load TRACKED (not via BaseQuery's AsNoTracking) so the status change
            // and the parent's EnrollmentFormCompleted flag actually persist.
            var form = await _unitOfWork.Repository<EnrollmentForm>().FirstOrDefaultAsync(f => f.Id == id, cancellationToken)
                ?? throw new NotFoundException(nameof(EnrollmentForm), id);

            if (form.Status != EnrollmentFormStatus.Submitted)
            {
                throw new DomainValidationException($"Only a submitted form can be reviewed; this one is {form.Status}.");
            }

            Child? child = null;
            if (request.Approve)
            {
                // Load the profile tracked so the unlock flag saves; also needed for the
                // billing pre-checks (parents can be pinned to a specific payment account).
                var parentProfile = await _unitOfWork.Repository<ParentProfile>()
                    .GetByIdAsync(form.ParentProfileId, cancellationToken);

                // Billing plan chosen at approval: validate everything the subscription's
                // first invoice will need BEFORE mutating anything, so a bad pick fails the
                // review cleanly instead of leaving an approved form with broken billing.
                if (request.PackagePlanId.HasValue)
                {
                    await ValidatePlanForBillingAsync(request.PackagePlanId.Value, parentProfile, cancellationToken);
                }

                // Same fail-fast intent as the plan check above: a bad/full/inactive batch
                // should reject the review before anything is mutated, not after the Child
                // and form status are already committed.
                if (request.BatchId.HasValue)
                {
                    await ValidateBatchForAssignmentAsync(request.BatchId.Value, cancellationToken);
                }

                // Not [Required] on the DTO — that would also reject Approve=false requests,
                // which never touch this field. DateOfBirth stays optional on Child itself
                // (age display already handles null), but a genuinely missing value at
                // approval is worth catching explicitly rather than silently creating a
                // Child nobody can show an age for.
                if (request.ChildDateOfBirth is null)
                {
                    throw new DomainValidationException("Child's date of birth is required to approve this enrollment.");
                }
                if (request.ChildDateOfBirth > DateOnly.FromDateTime(DateTime.UtcNow))
                {
                    throw new DomainValidationException("Child's date of birth cannot be in the future.");
                }

                var (firstName, lastName) = ResolveChildName(form, request);
                child = new Child
                {
                    ParentProfileId = form.ParentProfileId,
                    FirstName = firstName,
                    LastName = lastName,
                    DateOfBirth = request.ChildDateOfBirth,
                };
                await _unitOfWork.Repository<Child>().AddAsync(child, cancellationToken);

                form.Child = child;
                form.Status = EnrollmentFormStatus.Approved;

                if (parentProfile is not null)
                {
                    parentProfile.EnrollmentFormCompleted = true;
                    _unitOfWork.Repository<ParentProfile>().Update(parentProfile);
                }

                // Close the admissions loop: this form's linked demo booking (if any)
                // just produced a real Child record, so mark it Enrolled with actual
                // evidence behind the label instead of requiring a separate manual flip.
                if (form.DemoBookingId.HasValue)
                {
                    var linkedBooking = await _unitOfWork.Repository<DemoBooking>()
                        .GetByIdAsync(form.DemoBookingId.Value, cancellationToken);
                    if (linkedBooking is not null && linkedBooking.ConversionStatus != ConversionStatus.Enrolled)
                    {
                        linkedBooking.ConversionStatus = ConversionStatus.Enrolled;
                        _unitOfWork.Repository<DemoBooking>().Update(linkedBooking);
                    }
                }
            }
            else
            {
                form.Status = EnrollmentFormStatus.Rejected;
            }

            form.ReviewedAtUtc = DateTime.UtcNow;
            _unitOfWork.Repository<EnrollmentForm>().Update(form);

            await _auditLog.StageAsync(AuditAction.Update, nameof(EnrollmentForm), form.Id.ToString(), cancellationToken: cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            // Auto billing (WBS p.29/32): starting the plan today issues the first invoice
            // immediately and schedules the recurring cycle, so the amount shows up on the
            // parent's Payments & Billing the moment the enrollment is approved.
            if (request.Approve && request.PackagePlanId.HasValue && child is not null)
            {
                await _billingService.CreateSubscriptionAsync(
                    new CreateSubscriptionRequest
                    {
                        ParentProfileId = form.ParentProfileId,
                        ChildId = child.Id,
                        PackagePlanId = request.PackagePlanId.Value,
                        StartDate = DateOnly.FromDateTime(DateTime.UtcNow),
                    },
                    cancellationToken);
            }

            // Places the new child straight onto the batch's roster — same "act immediately
            // on approval" pattern as billing above. Runs after the plan is validated but the
            // batch's own capacity/active check happens for real inside AssignStudentAsync
            // (it needs a fresh, serializable read; the pre-check above only rejects garbage
            // input early).
            if (request.Approve && request.BatchId.HasValue && child is not null)
            {
                await _batchService.AssignStudentAsync(request.BatchId.Value, child.Id, cancellationToken);
            }

            return await GetAsync(form.Id, cancellationToken);
        }

        /// <summary>
        /// Mirrors what CreateSubscription/CreateInvoice will require: an active plan and an
        /// active payment account to route the invoice to (the parent's pinned account or the
        /// plan department's default). Throws an actionable error while nothing is saved yet.
        /// </summary>
        private async Task ValidatePlanForBillingAsync(
            Guid packagePlanId,
            ParentProfile? parentProfile,
            CancellationToken cancellationToken)
        {
            var plan = await _unitOfWork.Repository<PackagePlan>().Query()
                .Include(p => p.Course)
                .FirstOrDefaultAsync(p => p.Id == packagePlanId, cancellationToken)
                ?? throw new NotFoundException(nameof(PackagePlan), packagePlanId);

            if (!plan.IsActive)
            {
                throw new DomainValidationException(
                    $"Package plan '{plan.Name}' is inactive — pick an active plan or approve without billing.");
            }

            var pinnedAccountActive = parentProfile?.PaymentAccountId is { } pinned
                && await _unitOfWork.Repository<PaymentAccount>().ExistsAsync(
                    a => a.Id == pinned && a.IsActive, cancellationToken);
            if (pinnedAccountActive)
            {
                return;
            }

            var departmentId = plan.Course?.DepartmentId ?? WellKnownDepartments.Phonics;
            var departmentAccountActive = await _unitOfWork.Repository<PaymentAccount>().ExistsAsync(
                a => a.DepartmentId == departmentId && a.IsActive, cancellationToken);
            if (!departmentAccountActive)
            {
                var departmentName = (await _unitOfWork.Repository<Department>().GetByIdAsync(departmentId, cancellationToken))?.Name
                    ?? "that";
                throw new DomainValidationException(
                    $"Cannot start billing: no active payment account is configured for the {departmentName} department. " +
                    "Set one up under Payment Gateway Mapping, or approve without a plan and assign it later.");
            }
        }

        /// <summary>
        /// Fail-fast check before anything is mutated: the real, race-safe capacity check
        /// still happens inside <see cref="IBatchService.AssignStudentAsync"/> once the child
        /// exists, but there's no reason to create a Child and mark the form Approved only to
        /// discover the chosen batch doesn't exist, is Archived/Dormant, or is already full.
        /// </summary>
        private async Task ValidateBatchForAssignmentAsync(Guid batchId, CancellationToken cancellationToken)
        {
            var batch = await _unitOfWork.Repository<Batch>().GetByIdAsync(batchId, cancellationToken)
                ?? throw new NotFoundException(nameof(Batch), batchId);

            if (batch.Status != BatchStatus.Active)
            {
                throw new DomainValidationException(
                    $"Batch '{batch.Name}' is {batch.Status} and cannot take new enrollments — pick another batch or approve without one.");
            }

            var activeCount = await _unitOfWork.Repository<BatchEnrollment>().Query()
                .CountAsync(e => e.BatchId == batchId && e.Status == EnrollmentStatus.Active, cancellationToken);
            if (activeCount >= batch.Capacity)
            {
                throw new DomainValidationException(
                    $"Batch '{batch.Name}' is at capacity ({batch.Capacity}/{batch.Capacity}). Choose another batch or approve without one.");
            }
        }

        public async Task<IReadOnlyList<ChildDto>> ListChildrenForParentUserAsync(
            Guid parentUserId,
            CancellationToken cancellationToken = default)
        {
            var parent = await GetParentAsync(parentUserId, cancellationToken);
            var children = await _unitOfWork.Repository<Child>().Query()
                .Where(c => c.ParentProfileId == parent.Id)
                .OrderBy(c => c.FirstName)
                .ToListAsync(cancellationToken);

            return children.Select(c => new ChildDto
            {
                Id = c.Id,
                ParentProfileId = c.ParentProfileId,
                FirstName = c.FirstName,
                LastName = c.LastName,
                DateOfBirth = c.DateOfBirth,
                AcademicLevel = c.AcademicLevel,
                IsActive = c.IsActive,
            }).ToList();
        }

        public async Task<IReadOnlyList<StudentDto>> ListAllStudentsAsync(CancellationToken cancellationToken = default)
        {
            var children = await _unitOfWork.Repository<Child>().Query()
                .Include(c => c.ParentProfile).ThenInclude(p => p.User)
                .OrderBy(c => c.FirstName).ThenBy(c => c.LastName)
                .ToListAsync(cancellationToken);

            if (children.Count == 0)
            {
                return [];
            }

            // Resolve each child's current course in one pass (child → batch enrollment → batch → course).
            var childIds = children.Select(c => c.Id).ToList();
            var enrollments = await _unitOfWork.Repository<BatchEnrollment>().Query()
                .Where(e => childIds.Contains(e.ChildId))
                .Include(e => e.Batch).ThenInclude(b => b.Course)
                .ToListAsync(cancellationToken);
            var courseByChild = enrollments
                .GroupBy(e => e.ChildId)
                .ToDictionary(g => g.Key, g => g.First().Batch.Course.Name);

            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            return children.Select(c => new StudentDto
            {
                Id = c.Id,
                ParentProfileId = c.ParentProfileId,
                FullName = $"{c.FirstName} {c.LastName}".Trim(),
                Age = c.DateOfBirth is { } dob ? Math.Max(0, (today.DayNumber - dob.DayNumber) / 365) : null,
                AcademicLevel = c.AcademicLevel,
                ParentName = c.ParentProfile?.User is { } u ? $"{u.FirstName} {u.LastName}".Trim() : "—",
                CourseName = courseByChild.TryGetValue(c.Id, out var name) ? name : null,
                RmNotes = c.RmNotes,
                IsActive = c.IsActive,
            }).ToList();
        }

        public async Task UpdateChildNotesAsync(Guid childId, string? notes, CancellationToken cancellationToken = default)
        {
            // Load tracked so the mutation persists (Query() is AsNoTracking).
            var child = await _unitOfWork.Repository<Child>().FirstOrDefaultAsync(c => c.Id == childId, cancellationToken)
                ?? throw new NotFoundException(nameof(Child), childId);

            child.RmNotes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
            await _auditLog.StageAsync(AuditAction.Update, nameof(Child), child.Id.ToString(),
                changesJson: "{\"rmNotes\":\"updated\"}", cancellationToken: cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }

        public async Task<BulkImportResult> BulkImportStudentsAsync(
            Stream file, string fileName, CancellationToken cancellationToken = default)
        {
            var rows = _bulkFileReader.ReadRows(file, fileName);
            var result = new BulkImportResult { TotalRows = rows.Count };

            for (var i = 0; i < rows.Count; i++)
            {
                var rowNumber = i + 2;
                try
                {
                    var row = rows[i];
                    var parentEmail = row.GetOrNull("ParentEmail")
                        ?? throw new DomainValidationException("ParentEmail is required.");
                    var studentName = row.GetOrNull("StudentFullName")
                        ?? throw new DomainValidationException("StudentFullName is required.");

                    var normalizedEmail = parentEmail.Trim().ToLowerInvariant();
                    var parentProfile = await _unitOfWork.Repository<ParentProfile>().Query()
                        .Include(p => p.User)
                        .FirstOrDefaultAsync(p => p.User.Email == normalizedEmail, cancellationToken)
                        ?? throw new NotFoundException(
                            $"No parent account found with email '{parentEmail}' — create the parent first, then import their students.");

                    DateOnly? dateOfBirth = null;
                    var dobText = row.GetOrNull("DateOfBirth");
                    if (dobText is not null)
                    {
                        if (!DateOnly.TryParse(dobText, out var parsedDob))
                        {
                            throw new DomainValidationException($"DateOfBirth '{dobText}' is not a valid date — use YYYY-MM-DD.");
                        }
                        dateOfBirth = parsedDob;
                    }

                    var nameParts = studentName.Trim().Split(' ', 2);
                    var child = new Child
                    {
                        ParentProfileId = parentProfile.Id,
                        FirstName = nameParts[0],
                        LastName = nameParts.Length > 1 ? nameParts[1] : string.Empty,
                        DateOfBirth = dateOfBirth,
                        AcademicLevel = row.GetOrNull("AcademicLevel"),
                        IsActive = true,
                    };
                    await _unitOfWork.Repository<Child>().AddAsync(child, cancellationToken);
                    await _auditLog.StageAsync(AuditAction.Create, nameof(Child), child.Id.ToString(), cancellationToken: cancellationToken);
                    await _unitOfWork.SaveChangesAsync(cancellationToken);

                    result.SucceededCount++;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    result.FailedCount++;
                    result.Errors.Add(new BulkImportRowError { RowNumber = rowNumber, Message = ex.Message });
                }
            }

            return result;
        }

        public async Task<string> ExportStudentsCsvAsync(CancellationToken cancellationToken = default)
        {
            var students = await ListAllStudentsAsync(cancellationToken);
            string[] headers = ["FullName", "ParentName", "Age", "AcademicLevel", "CourseName", "IsActive"];
            var rows = students.Select(s => new List<string?>
            {
                s.FullName, s.ParentName, s.Age?.ToString(), s.AcademicLevel, s.CourseName, s.IsActive ? "true" : "false",
            });
            return CsvWriter.BuildCsv(headers, rows);
        }

        /// <summary>
        /// Parses <paramref name="formDataJson"/> and requires every <see cref="RequiredFormFields"/>
        /// key to be present with a non-blank string value. Shared by Submit and the admin Edit
        /// dialog's save — both write the same document the review queue reads back.
        /// </summary>
        private static void ValidateRequiredFields(string formDataJson)
        {
            JsonDocument document;
            try
            {
                document = JsonDocument.Parse(formDataJson);
            }
            catch (JsonException)
            {
                throw new DomainValidationException("The submitted form data is not valid JSON.");
            }

            using (document)
            {
                var missing = RequiredFormFields.Where(field =>
                    !document.RootElement.TryGetProperty(field, out var value)
                    || value.ValueKind != JsonValueKind.String
                    || string.IsNullOrWhiteSpace(value.GetString())).ToList();

                if (missing.Count > 0)
                {
                    throw new DomainValidationException(
                        $"The enrollment form is missing required field(s): {string.Join(", ", missing)}.");
                }
            }
        }

        private static (string FirstName, string LastName) ResolveChildName(EnrollmentForm form, ReviewEnrollmentFormRequest request)
        {
            if (!string.IsNullOrWhiteSpace(request.ChildFirstName))
            {
                return (request.ChildFirstName.Trim(), request.ChildLastName?.Trim() ?? "");
            }

            // Fall back to the form's own answer (proposed field id: child full name)
            using var document = JsonDocument.Parse(form.FormDataJson);
            if (document.RootElement.TryGetProperty("childName", out var nameElement)
                && nameElement.ValueKind == JsonValueKind.String
                && !string.IsNullOrWhiteSpace(nameElement.GetString()))
            {
                var parts = nameElement.GetString()!.Trim().Split(' ', 2);
                return (parts[0], parts.Length > 1 ? parts[1] : "");
            }

            throw new DomainValidationException(
                "Provide the child's name for approval: the form has no 'childName' answer to fall back on.");
        }

        private async Task<ParentProfile> GetParentAsync(Guid parentUserId, CancellationToken cancellationToken)
        {
            return await _unitOfWork.Repository<ParentProfile>()
                .FirstOrDefaultAsync(p => p.UserId == parentUserId, cancellationToken)
                ?? throw new NotFoundException("No parent profile is linked to the current account.");
        }

        private IQueryable<EnrollmentForm> BaseQuery()
        {
            return _unitOfWork.Repository<EnrollmentForm>().Query()
                .Include(f => f.ParentProfile).ThenInclude(p => p.User);
        }

        private static EnrollmentFormDto ToDto(EnrollmentForm form)
        {
            var parentUser = form.ParentProfile.User;
            return new EnrollmentFormDto
            {
                Id = form.Id,
                ParentProfileId = form.ParentProfileId,
                ParentName = $"{parentUser.FirstName} {parentUser.LastName}",
                ParentEmail = parentUser.Email,
                ChildId = form.ChildId,
                FormDataJson = form.FormDataJson,
                Status = form.Status,
                SubmittedAtUtc = form.SubmittedAtUtc,
                ReviewedAtUtc = form.ReviewedAtUtc,
            };
        }
    }
}
