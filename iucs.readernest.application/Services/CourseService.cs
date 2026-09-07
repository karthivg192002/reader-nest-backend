using iucs.readernest.application.Common;
using iucs.readernest.application.Common.Exceptions;
using iucs.readernest.application.Common.Interfaces;
using iucs.readernest.application.Dto.Common;
using iucs.readernest.application.Dto.Courses;
using iucs.readernest.application.Mappings;
using iucs.readernest.domain.Entities.Academics;
using iucs.readernest.domain.Entities.Billing;
using iucs.readernest.domain.Entities.Sessions;
using iucs.readernest.domain.Enums;
using iucs.readernest.domain.Repository;
using Microsoft.EntityFrameworkCore;

namespace iucs.readernest.application.Services
{
    public class CourseService : ICourseService
    {
        private readonly IUnitOfWork _unitOfWork;
        private readonly IAuditLogService _auditLog;
        private readonly IBulkFileReader _bulkFileReader;

        public CourseService(IUnitOfWork unitOfWork, IAuditLogService auditLog, IBulkFileReader bulkFileReader)
        {
            _unitOfWork = unitOfWork;
            _auditLog = auditLog;
            _bulkFileReader = bulkFileReader;
        }

        public async Task<IReadOnlyList<CourseCategoryDto>> ListCategoriesAsync(CancellationToken cancellationToken = default)
        {
            var categories = await _unitOfWork.Repository<CourseCategory>().Query()
                .Include(c => c.Department)
                .OrderBy(c => c.Name)
                .ToListAsync(cancellationToken);

            return categories.Select(c => c.ToDto()).ToList();
        }

        public async Task<CourseCategoryDto> CreateCategoryAsync(
            CreateCourseCategoryRequest request,
            CancellationToken cancellationToken = default)
        {
            var name = request.Name.Trim();
            var repository = _unitOfWork.Repository<CourseCategory>();

            // Scoped to the department, not global: two departments legitimately reuse the same
            // category name (e.g. a "Level 1" under both Hindi and Maths), and the frontend's
            // own ensureCategory() already assumes this -- it only treats an existing category as
            // a match when its DepartmentId matches too, otherwise it tries to create a new one
            // scoped to the target department. A global check here rejected that second create
            // outright, even though nothing actually collided.
            if (await repository.ExistsAsync(c => c.Name == name && c.DepartmentId == request.DepartmentId, cancellationToken))
            {
                throw new ConflictException($"A course category named '{name}' already exists in this department.");
            }

            // Fetched (not just checked for existence) so the navigation is set below —
            // ToDto() reads category.Department.Name directly, and without it the create
            // response comes back with an empty departmentName even though the same row
            // shows the real name a moment later from ListCategoriesAsync (which does
            // Include(c => c.Department)). Two endpoints for the same resource returning a
            // different shape for the same field is exactly the kind of thing a client can't
            // work around.
            var department = await _unitOfWork.Repository<Department>().GetByIdAsync(request.DepartmentId, cancellationToken)
                ?? throw new NotFoundException(nameof(Department), request.DepartmentId);

            var category = new CourseCategory
            {
                Name = name,
                Description = request.Description,
                DepartmentId = request.DepartmentId,
                Department = department,
            };
            await repository.AddAsync(category, cancellationToken);
            await _auditLog.StageAsync(AuditAction.Create, nameof(CourseCategory), category.Id.ToString(), cancellationToken: cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return category.ToDto();
        }

        public async Task<IReadOnlyList<CourseDto>> ListAsync(bool includeInactive = false, CancellationToken cancellationToken = default)
        {
            var query = _unitOfWork.Repository<Course>().Query()
                .Include(c => c.CourseCategory)
                .Include(c => c.Department)
                .AsQueryable();
            if (!includeInactive)
            {
                query = query.Where(c => c.IsActive);
            }

            var courses = await query.OrderBy(c => c.Name).ToListAsync(cancellationToken);
            var dtos = courses.Select(c => c.ToDto()).ToList();
            await ApplyStatsAsync(dtos, cancellationToken);
            return dtos;
        }

        /// <summary>
        /// Fills ActiveBatches/TotalEnrolled/Revenue — deliberately not part of Course.ToDto()
        /// since they're cross-table aggregates (Batch/BatchEnrollment/Invoice), not fields on
        /// the Course entity itself. Revenue is AmountPaid summed over invoices tied to the
        /// course (Invoice.CourseId), so it reflects money actually collected, not billed.
        /// </summary>
        private async Task ApplyStatsAsync(IReadOnlyList<CourseDto> dtos, CancellationToken cancellationToken)
        {
            if (dtos.Count == 0) return;

            var courseIds = dtos.Select(d => d.Id).ToList();

            var activeBatchCounts = await _unitOfWork.Repository<Batch>().Query()
                .Where(b => courseIds.Contains(b.CourseId) && b.Status == BatchStatus.Active)
                .GroupBy(b => b.CourseId)
                .Select(g => new { CourseId = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.CourseId, x => x.Count, cancellationToken);

            var enrolledCounts = await _unitOfWork.Repository<BatchEnrollment>().Query()
                .Where(e => e.Status == EnrollmentStatus.Active && courseIds.Contains(e.Batch.CourseId))
                .GroupBy(e => e.Batch.CourseId)
                .Select(g => new { CourseId = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.CourseId, x => x.Count, cancellationToken);

            var revenueByCourse = await _unitOfWork.Repository<Invoice>().Query()
                .Where(i => i.CourseId != null && courseIds.Contains(i.CourseId.Value))
                .GroupBy(i => i.CourseId!.Value)
                .Select(g => new { CourseId = g.Key, Amount = g.Sum(i => i.AmountPaid) })
                .ToDictionaryAsync(x => x.CourseId, x => x.Amount, cancellationToken);

            foreach (var dto in dtos)
            {
                dto.ActiveBatches = activeBatchCounts.GetValueOrDefault(dto.Id);
                dto.TotalEnrolled = enrolledCounts.GetValueOrDefault(dto.Id);
                dto.Revenue = revenueByCourse.GetValueOrDefault(dto.Id);
            }
        }

        public async Task<IReadOnlyList<CourseOptionDto>> ListOptionsAsync(CancellationToken cancellationToken = default)
        {
            return await _unitOfWork.Repository<Course>().Query()
                .Where(c => c.IsActive)
                .OrderBy(c => c.Name)
                .Select(c => new CourseOptionDto { Id = c.Id, Name = c.Name, Type = c.Type })
                .ToListAsync(cancellationToken);
        }

        public async Task<CourseDto> GetAsync(Guid id, CancellationToken cancellationToken = default)
        {
            var course = await _unitOfWork.Repository<Course>().Query()
                .Include(c => c.CourseCategory)
                .Include(c => c.Department)
                .FirstOrDefaultAsync(c => c.Id == id, cancellationToken)
                ?? throw new NotFoundException(nameof(Course), id);

            var dto = course.ToDto();
            await ApplyStatsAsync([dto], cancellationToken);
            return dto;
        }

        public async Task<CourseDto> CreateAsync(SaveCourseRequest request, CancellationToken cancellationToken = default)
        {
            var (category, department) = await ValidateAsync(request, cancellationToken);
            await EnsureNameNotDuplicateAsync(request.Name, request.DepartmentId, excludingId: null, cancellationToken);

            var course = new Course
            {
                CourseCategoryId = request.CourseCategoryId,
                CourseCategory = category,
                Name = request.Name.Trim(),
                Description = request.Description,
                Type = request.Type,
                DurationMinutes = request.DurationMinutes,
                Price = request.Price,
                TotalSessions = request.TotalSessions,
                DepartmentId = request.DepartmentId,
                Department = department,
                IsActive = request.IsActive,
            };
            await _unitOfWork.Repository<Course>().AddAsync(course, cancellationToken);
            await _auditLog.StageAsync(AuditAction.Create, nameof(Course), course.Id.ToString(), cancellationToken: cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return course.ToDto();
        }

        public async Task<CourseDto> UpdateAsync(Guid id, SaveCourseRequest request, CancellationToken cancellationToken = default)
        {
            var (category, department) = await ValidateAsync(request, cancellationToken);
            await EnsureNameNotDuplicateAsync(request.Name, request.DepartmentId, excludingId: id, cancellationToken);
            var course = await _unitOfWork.Repository<Course>().GetByIdAsync(id, cancellationToken)
                ?? throw new NotFoundException(nameof(Course), id);

            // Switching to Individual bypasses BatchService's own "one student per
            // Individual batch" guard entirely, since that guard only fires when a BATCH
            // is edited — not when the course backing it changes type out from under it.
            if (request.Type == CourseType.Individual && course.Type != CourseType.Individual)
            {
                var hasMultiStudentBatch = await _unitOfWork.Repository<BatchEnrollment>().Query()
                    .Where(e => e.Status == EnrollmentStatus.Active && e.Batch.CourseId == id)
                    .GroupBy(e => e.BatchId)
                    .AnyAsync(g => g.Count() > 1, cancellationToken);
                if (hasMultiStudentBatch)
                {
                    throw new DomainValidationException(
                        "This course has a batch with more than one active student; it can't switch to Individual " +
                        "until students are moved to separate batches.");
                }
            }

            // TotalSessions/DurationMinutes are baked into any schedule already generated
            // from them (SessionService.GenerateScheduleAsync). Changing either afterward
            // desyncs MoveBatchToDormantIfCourseCompletedAsync's completedSessions vs.
            // TotalSessions check against a schedule sized for the old values.
            if ((request.TotalSessions != course.TotalSessions || request.DurationMinutes != course.DurationMinutes))
            {
                var batchIds = await _unitOfWork.Repository<Batch>().Query()
                    .Where(b => b.CourseId == id)
                    .Select(b => b.Id)
                    .ToListAsync(cancellationToken);
                var hasGeneratedSchedule = batchIds.Count > 0 && await _unitOfWork.Repository<ClassSession>()
                    .ExistsAsync(s => s.BatchId != null && batchIds.Contains(s.BatchId.Value), cancellationToken);
                if (hasGeneratedSchedule)
                {
                    throw new DomainValidationException(
                        "This course already has a batch with a generated schedule; TotalSessions and " +
                        "DurationMinutes can no longer change without desyncing it.");
                }
            }

            course.CourseCategoryId = request.CourseCategoryId;
            course.CourseCategory = category;
            course.Name = request.Name.Trim();
            course.Description = request.Description;
            course.Type = request.Type;
            course.DurationMinutes = request.DurationMinutes;
            course.Price = request.Price;
            course.TotalSessions = request.TotalSessions;
            course.DepartmentId = request.DepartmentId;
            course.Department = department;
            course.IsActive = request.IsActive;

            await _auditLog.StageAsync(AuditAction.Update, nameof(Course), course.Id.ToString(), cancellationToken: cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return course.ToDto();
        }

        public async Task<BulkImportResult> BulkImportAsync(Stream file, string fileName, CancellationToken cancellationToken = default)
        {
            var rows = _bulkFileReader.ReadRows(file, fileName);
            var result = new BulkImportResult { TotalRows = rows.Count };

            for (var i = 0; i < rows.Count; i++)
            {
                var rowNumber = i + 2;
                try
                {
                    var row = rows[i];
                    var name = row.GetOrNull("Name") ?? throw new DomainValidationException("Name is required.");
                    var departmentName = row.GetOrNull("DepartmentName")
                        ?? throw new DomainValidationException("DepartmentName is required.");
                    var categoryName = row.GetOrNull("CategoryName")
                        ?? throw new DomainValidationException("CategoryName is required.");
                    var typeText = row.GetOrNull("Type") ?? "Group";
                    if (!Enum.TryParse<CourseType>(typeText, true, out var type))
                    {
                        throw new DomainValidationException($"Type '{typeText}' is not valid — use Individual or Group.");
                    }

                    var durationText = row.GetOrNull("DurationMinutes")
                        ?? throw new DomainValidationException("DurationMinutes is required.");
                    if (!int.TryParse(durationText, out var duration))
                    {
                        throw new DomainValidationException($"DurationMinutes '{durationText}' is not a whole number.");
                    }

                    var priceText = row.GetOrNull("Price") ?? throw new DomainValidationException("Price is required.");
                    if (!decimal.TryParse(priceText, out var price))
                    {
                        throw new DomainValidationException($"Price '{priceText}' is not a number.");
                    }

                    var sessionsText = row.GetOrNull("TotalSessions")
                        ?? throw new DomainValidationException("TotalSessions is required.");
                    if (!int.TryParse(sessionsText, out var totalSessions))
                    {
                        throw new DomainValidationException($"TotalSessions '{sessionsText}' is not a whole number.");
                    }

                    var department = await _unitOfWork.Repository<Department>()
                        .FirstOrDefaultAsync(d => d.Name == departmentName, cancellationToken)
                        ?? throw new NotFoundException($"No department named '{departmentName}' — create it first (or via the Departments bulk import).");

                    var category = await _unitOfWork.Repository<CourseCategory>()
                        .FirstOrDefaultAsync(c => c.Name == categoryName && c.DepartmentId == department.Id, cancellationToken);
                    if (category is null)
                    {
                        var created = await CreateCategoryAsync(
                            new CreateCourseCategoryRequest { Name = categoryName, DepartmentId = department.Id },
                            cancellationToken);
                        category = await _unitOfWork.Repository<CourseCategory>().GetByIdAsync(created.Id, cancellationToken);
                    }

                    await CreateAsync(
                        new SaveCourseRequest
                        {
                            CourseCategoryId = category!.Id,
                            Name = name,
                            Description = row.GetOrNull("Description"),
                            Type = type,
                            DurationMinutes = duration,
                            Price = price,
                            TotalSessions = totalSessions,
                            DepartmentId = department.Id,
                            IsActive = row.GetBool("IsActive"),
                        },
                        cancellationToken);
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

        public async Task<string> ExportCsvAsync(bool includeInactive, CancellationToken cancellationToken = default)
        {
            var courses = await ListAsync(includeInactive, cancellationToken);
            string[] headers = ["DepartmentName", "CategoryName", "Name", "Description", "Type", "DurationMinutes", "Price", "TotalSessions", "IsActive"];
            var rows = courses.Select(c => new List<string?>
            {
                c.DepartmentName, c.CategoryName, c.Name, c.Description, c.Type.ToString(),
                c.DurationMinutes.ToString(), c.Price.ToString(System.Globalization.CultureInfo.InvariantCulture),
                c.TotalSessions.ToString(), c.IsActive ? "true" : "false",
            });
            return CsvWriter.BuildCsv(headers, rows);
        }

        /// <summary>
        /// Case-insensitive uniqueness within a department — nothing previously stopped the
        /// exact same course name being created twice (manually, or by re-running the same
        /// bulk-import CSV), which is how the catalogue ended up with parallel entries like
        /// "Abacus A1" and "Abacus Level A1 (4-8 Years)" for the same subject. Scoped to
        /// department rather than global, matching CreateCategoryAsync's own precedent above —
        /// two departments can legitimately reuse a name (e.g. a "Level 1" under both Hindi and
        /// Maths). This only catches exact (trimmed, case-insensitive) repeats, not genuinely
        /// different names for the same subject — that needs a manual catalogue cleanup, not a
        /// validation rule.
        /// </summary>
        private async Task EnsureNameNotDuplicateAsync(
            string name, Guid departmentId, Guid? excludingId, CancellationToken cancellationToken)
        {
            var trimmed = name.Trim();
            var repository = _unitOfWork.Repository<Course>();
            var duplicate = excludingId is { } id
                ? await repository.ExistsAsync(
                    c => c.DepartmentId == departmentId && c.Name.ToLower() == trimmed.ToLower() && c.Id != id,
                    cancellationToken)
                : await repository.ExistsAsync(
                    c => c.DepartmentId == departmentId && c.Name.ToLower() == trimmed.ToLower(),
                    cancellationToken);
            if (duplicate)
            {
                throw new ConflictException($"A course named '{trimmed}' already exists in this department.");
            }
        }

        private async Task<(CourseCategory Category, Department Department)> ValidateAsync(
            SaveCourseRequest request, CancellationToken cancellationToken)
        {
            if (request.DurationMinutes <= 0)
            {
                throw new DomainValidationException("Class duration must be a positive number of minutes.");
            }

            var department = await _unitOfWork.Repository<Department>().GetByIdAsync(request.DepartmentId, cancellationToken)
                ?? throw new NotFoundException(nameof(Department), request.DepartmentId);

            var category = await _unitOfWork.Repository<CourseCategory>().GetByIdAsync(request.CourseCategoryId, cancellationToken)
                ?? throw new NotFoundException(nameof(CourseCategory), request.CourseCategoryId);

            return (category, department);
        }
    }
}
