using iucs.readernest.application.Common;
using iucs.readernest.application.Common.Exceptions;
using iucs.readernest.application.Common.Interfaces;
using iucs.readernest.application.Dto.Common;
using iucs.readernest.application.Dto.Quizzes;
using iucs.readernest.domain.Entities.Academics;
using iucs.readernest.domain.Entities.Admission;
using iucs.readernest.domain.Entities.Quizzes;
using iucs.readernest.domain.Entities.Sessions;
using iucs.readernest.domain.Enums;
using iucs.readernest.domain.Repository;
using Microsoft.EntityFrameworkCore;

namespace iucs.readernest.application.Services
{
    public class QuizQuestionService : IQuizQuestionService
    {
        private readonly IUnitOfWork _unitOfWork;
        private readonly IAuditLogService _auditLog;
        private readonly ISessionService _sessionService;
        private readonly IBulkFileReader _bulkFileReader;

        public QuizQuestionService(
            IUnitOfWork unitOfWork, IAuditLogService auditLog, ISessionService sessionService, IBulkFileReader bulkFileReader)
        {
            _unitOfWork = unitOfWork;
            _auditLog = auditLog;
            _sessionService = sessionService;
            _bulkFileReader = bulkFileReader;
        }

        public async Task<IReadOnlyList<QuizQuestionDto>> ListAsync(
            Guid? departmentId, Guid? courseId, CancellationToken cancellationToken = default)
        {
            var query = _unitOfWork.Repository<QuizQuestion>().Query()
                .Include(q => q.Department)
                .Include(q => q.Course)
                .Include(q => q.Options)
                .AsQueryable();

            if (departmentId.HasValue)
            {
                query = query.Where(q => q.DepartmentId == departmentId.Value);
            }

            if (courseId.HasValue)
            {
                query = query.Where(q => q.CourseId == courseId.Value);
            }

            var questions = await query
                .OrderBy(q => q.Department.Name)
                .ThenBy(q => q.CourseId == null)
                .ThenBy(q => q.DisplayOrder)
                .ToListAsync(cancellationToken);

            return questions.Select(ToDto).ToList();
        }

        public async Task<QuizQuestionDto> CreateAsync(SaveQuizQuestionRequest request, CancellationToken cancellationToken = default)
        {
            var (departmentId, course) = await ResolveScopeAsync(request, cancellationToken);
            ValidateOptions(request.Options);

            var question = new QuizQuestion
            {
                DepartmentId = departmentId,
                CourseId = course?.Id,
                Prompt = request.Prompt.Trim(),
                DisplayOrder = request.DisplayOrder,
                Options = request.Options
                    .Select((o, i) => new QuizQuestionOption { Text = o.Text.Trim(), IsCorrect = o.IsCorrect, DisplayOrder = i })
                    .ToList(),
            };
            await _unitOfWork.Repository<QuizQuestion>().AddAsync(question, cancellationToken);
            await _auditLog.StageAsync(AuditAction.Create, nameof(QuizQuestion), question.Id.ToString(), cancellationToken: cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return await GetDtoAsync(question.Id, cancellationToken);
        }

        public async Task<QuizQuestionDto> UpdateAsync(Guid id, SaveQuizQuestionRequest request, CancellationToken cancellationToken = default)
        {
            var question = await _unitOfWork.Repository<QuizQuestion>().TrackedQuery()
                .Include(q => q.Options)
                .FirstOrDefaultAsync(q => q.Id == id, cancellationToken)
                ?? throw new NotFoundException(nameof(QuizQuestion), id);

            var (departmentId, course) = await ResolveScopeAsync(request, cancellationToken);
            ValidateOptions(request.Options);

            question.DepartmentId = departmentId;
            question.CourseId = course?.Id;
            question.Prompt = request.Prompt.Trim();
            question.DisplayOrder = request.DisplayOrder;

            // Replace the option set wholesale — simpler and safer than diffing an admin form's
            // free-edit list against existing rows, and options carry no history worth preserving
            // individually (StudentAward/engagement records reference the question via awards, not
            // via a specific option row).
            var optionRepo = _unitOfWork.Repository<QuizQuestionOption>();
            foreach (var existing in question.Options.ToList())
            {
                optionRepo.Remove(existing);
            }
            question.Options = request.Options
                .Select((o, i) => new QuizQuestionOption { QuizQuestionId = question.Id, Text = o.Text.Trim(), IsCorrect = o.IsCorrect, DisplayOrder = i })
                .ToList();

            await _auditLog.StageAsync(AuditAction.Update, nameof(QuizQuestion), question.Id.ToString(), cancellationToken: cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return await GetDtoAsync(question.Id, cancellationToken);
        }

        public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
        {
            var repository = _unitOfWork.Repository<QuizQuestion>();
            // Options must come along tracked — QuizQuestionOption.QuizQuestionId is a required
            // FK, so soft-deleting the question while its options are untracked/unloaded leaves
            // EF unable to tell whether they should follow it (InvalidOperationException:
            // "association... has been severed"). Explicit, same shape as UpdateAsync's option
            // replacement below.
            var question = await repository.TrackedQuery()
                .Include(q => q.Options)
                .FirstOrDefaultAsync(q => q.Id == id, cancellationToken)
                ?? throw new NotFoundException(nameof(QuizQuestion), id);

            var optionRepo = _unitOfWork.Repository<QuizQuestionOption>();
            foreach (var option in question.Options)
            {
                optionRepo.Remove(option);
            }
            repository.Remove(question);

            await _auditLog.StageAsync(AuditAction.Delete, nameof(QuizQuestion), id.ToString(), cancellationToken: cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }

        public async Task<IReadOnlyList<QuizQuestionDto>> GetForSessionAsync(
            Guid sessionId, Guid userId, CancellationToken cancellationToken = default)
        {
            var session = await _unitOfWork.Repository<ClassSession>().Query()
                .Include(s => s.Batch).ThenInclude(b => b!.Course)
                .FirstOrDefaultAsync(s => s.Id == sessionId, cancellationToken)
                ?? throw new NotFoundException(nameof(ClassSession), sessionId);

            if (!await _sessionService.IsSessionParticipantAsync(sessionId, userId, cancellationToken))
            {
                throw new ForbiddenException("You do not have access to this session.");
            }

            var courseId = session.Batch?.CourseId;
            var departmentId = session.Batch?.Course?.DepartmentId;
            if (departmentId is null)
            {
                // Demo session — no batch/course, resolve the department from the booking instead.
                departmentId = await _unitOfWork.Repository<DemoBooking>().Query()
                    .Where(b => b.ClassSessionId == sessionId)
                    .Select(b => b.DepartmentId)
                    .FirstOrDefaultAsync(cancellationToken);
            }

            if (departmentId is null)
            {
                return []; // nothing to resolve a quiz bank against (e.g. a demo with no department set)
            }

            var questions = await _unitOfWork.Repository<QuizQuestion>().Query()
                .Include(q => q.Department)
                .Include(q => q.Course)
                .Include(q => q.Options)
                .Where(q => q.DepartmentId == departmentId.Value
                    && (q.CourseId == null || (courseId != null && q.CourseId == courseId)))
                .OrderBy(q => q.CourseId == null) // this course's own questions before the department-wide ones
                .ThenBy(q => q.DisplayOrder)
                .ToListAsync(cancellationToken);

            return questions.Select(ToDto).ToList();
        }

        private const int MaxImportOptions = 6;

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
                    var prompt = row.GetOrNull("Prompt") ?? throw new DomainValidationException("Prompt is required.");

                    Guid? departmentId = null;
                    Guid? courseId = null;
                    var courseName = row.GetOrNull("CourseName");
                    if (courseName is not null)
                    {
                        var course = await _unitOfWork.Repository<Course>().FirstOrDefaultAsync(c => c.Name == courseName, cancellationToken)
                            ?? throw new NotFoundException($"No course named '{courseName}'.");
                        courseId = course.Id;
                    }
                    else
                    {
                        var departmentName = row.GetOrNull("DepartmentName")
                            ?? throw new DomainValidationException("Set either DepartmentName or CourseName for this question.");
                        var department = await _unitOfWork.Repository<Department>().FirstOrDefaultAsync(d => d.Name == departmentName, cancellationToken)
                            ?? throw new NotFoundException($"No department named '{departmentName}'.");
                        departmentId = department.Id;
                    }

                    var options = new List<QuizQuestionOptionInput>();
                    for (var opt = 1; opt <= MaxImportOptions; opt++)
                    {
                        var text = row.GetOrNull($"Option{opt}");
                        if (text is not null)
                        {
                            options.Add(new QuizQuestionOptionInput { Text = text, IsCorrect = false });
                        }
                    }

                    var correctText = row.GetOrNull("CorrectOptionNumber")
                        ?? throw new DomainValidationException("CorrectOptionNumber is required.");
                    if (!int.TryParse(correctText, out var correctNumber) || correctNumber < 1 || correctNumber > options.Count)
                    {
                        throw new DomainValidationException(
                            $"CorrectOptionNumber must be a number between 1 and {options.Count} (the number of filled-in options).");
                    }

                    options[correctNumber - 1].IsCorrect = true;

                    await CreateAsync(
                        new SaveQuizQuestionRequest
                        {
                            DepartmentId = departmentId,
                            CourseId = courseId,
                            Prompt = prompt,
                            Options = options,
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

        public async Task<string> ExportCsvAsync(Guid? departmentId, Guid? courseId, CancellationToken cancellationToken = default)
        {
            var questions = await ListAsync(departmentId, courseId, cancellationToken);
            var headers = new List<string> { "DepartmentName", "CourseName", "Prompt" };
            for (var opt = 1; opt <= MaxImportOptions; opt++)
            {
                headers.Add($"Option{opt}");
            }
            headers.Add("CorrectOptionNumber");

            var rows = questions.Select(q =>
            {
                var row = new List<string?> { q.DepartmentName, q.CourseName, q.Prompt };
                var ordered = q.Options.OrderBy(o => o.DisplayOrder).ToList();
                for (var opt = 0; opt < MaxImportOptions; opt++)
                {
                    row.Add(opt < ordered.Count ? ordered[opt].Text : null);
                }
                var correctIndex = ordered.FindIndex(o => o.IsCorrect);
                row.Add(correctIndex >= 0 ? (correctIndex + 1).ToString() : null);
                return row;
            });

            return CsvWriter.BuildCsv(headers, rows);
        }

        /// <summary>Shared validate+resolve for Create/Update: derives DepartmentId from the
        /// course when one is set (never trusts a mismatched value from the client), otherwise
        /// requires an explicit DepartmentId.</summary>
        private async Task<(Guid DepartmentId, Course? Course)> ResolveScopeAsync(
            SaveQuizQuestionRequest request, CancellationToken cancellationToken)
        {
            if (request.CourseId is Guid courseId)
            {
                var course = await _unitOfWork.Repository<Course>().Query()
                    .FirstOrDefaultAsync(c => c.Id == courseId, cancellationToken)
                    ?? throw new NotFoundException(nameof(Course), courseId);
                return (course.DepartmentId, course);
            }

            if (request.DepartmentId is not Guid departmentId)
            {
                throw new DomainValidationException("Set either a course or a department for this question.");
            }

            if (!await _unitOfWork.Repository<Department>().ExistsAsync(d => d.Id == departmentId, cancellationToken))
            {
                throw new NotFoundException(nameof(Department), departmentId);
            }

            return (departmentId, null);
        }

        private static void ValidateOptions(List<QuizQuestionOptionInput> options)
        {
            if (options.Count < 2 || options.Count > 6)
            {
                throw new DomainValidationException("A question needs between 2 and 6 options.");
            }

            if (options.Count(o => o.IsCorrect) != 1)
            {
                throw new DomainValidationException("Exactly one option must be marked correct.");
            }

            if (options.Any(o => string.IsNullOrWhiteSpace(o.Text)))
            {
                throw new DomainValidationException("Every option needs text.");
            }
        }

        private async Task<QuizQuestionDto> GetDtoAsync(Guid id, CancellationToken cancellationToken)
        {
            var question = await _unitOfWork.Repository<QuizQuestion>().Query()
                .Include(q => q.Department)
                .Include(q => q.Course)
                .Include(q => q.Options)
                .FirstAsync(q => q.Id == id, cancellationToken);
            return ToDto(question);
        }

        private static QuizQuestionDto ToDto(QuizQuestion question) => new()
        {
            Id = question.Id,
            DepartmentId = question.DepartmentId,
            DepartmentName = question.Department.Name,
            CourseId = question.CourseId,
            CourseName = question.Course?.Name,
            Prompt = question.Prompt,
            DisplayOrder = question.DisplayOrder,
            Options = question.Options
                .OrderBy(o => o.DisplayOrder)
                .Select(o => new QuizQuestionOptionDto { Id = o.Id, Text = o.Text, IsCorrect = o.IsCorrect, DisplayOrder = o.DisplayOrder })
                .ToList(),
        };
    }
}
