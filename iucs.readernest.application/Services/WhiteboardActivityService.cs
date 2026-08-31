using iucs.readernest.application.Common.Exceptions;
using iucs.readernest.application.Common.Interfaces;
using iucs.readernest.application.Dto.Activities;
using iucs.readernest.domain.Entities.Academics;
using iucs.readernest.domain.Entities.Activities;
using iucs.readernest.domain.Entities.Admission;
using iucs.readernest.domain.Entities.Sessions;
using iucs.readernest.domain.Enums;
using iucs.readernest.domain.Repository;
using Microsoft.EntityFrameworkCore;

namespace iucs.readernest.application.Services
{
    public class WhiteboardActivityService : IWhiteboardActivityService
    {
        private readonly IUnitOfWork _unitOfWork;
        private readonly IAuditLogService _auditLog;
        private readonly ISessionService _sessionService;

        public WhiteboardActivityService(IUnitOfWork unitOfWork, IAuditLogService auditLog, ISessionService sessionService)
        {
            _unitOfWork = unitOfWork;
            _auditLog = auditLog;
            _sessionService = sessionService;
        }

        public async Task<IReadOnlyList<WhiteboardActivityDto>> ListAsync(
            Guid? departmentId, Guid? courseId, CancellationToken cancellationToken = default)
        {
            var query = _unitOfWork.Repository<WhiteboardActivityTemplate>().Query()
                .Include(a => a.Department)
                .Include(a => a.Course)
                .Include(a => a.Items)
                .AsQueryable();

            if (departmentId.HasValue)
            {
                query = query.Where(a => a.DepartmentId == departmentId.Value);
            }

            if (courseId.HasValue)
            {
                query = query.Where(a => a.CourseId == courseId.Value);
            }

            var activities = await query
                .OrderBy(a => a.Department.Name)
                .ThenBy(a => a.CourseId == null)
                .ThenBy(a => a.DisplayOrder)
                .ToListAsync(cancellationToken);

            return activities.Select(ToDto).ToList();
        }

        public async Task<WhiteboardActivityDto> CreateAsync(SaveWhiteboardActivityRequest request, CancellationToken cancellationToken = default)
        {
            var (departmentId, course) = await ResolveScopeAsync(request, cancellationToken);
            ValidateItems(request.Mode, request.Items);

            var activity = new WhiteboardActivityTemplate
            {
                DepartmentId = departmentId,
                CourseId = course?.Id,
                Mode = request.Mode,
                Prompt = request.Prompt.Trim(),
                DisplayOrder = request.DisplayOrder,
                Items = request.Items
                    .Select((i, idx) => new WhiteboardActivityItem
                    {
                        Emoji = i.Emoji.Trim(),
                        Label = i.Label?.Trim(),
                        IsTarget = i.IsTarget,
                        DisplayOrder = idx,
                    })
                    .ToList(),
            };
            await _unitOfWork.Repository<WhiteboardActivityTemplate>().AddAsync(activity, cancellationToken);
            await _auditLog.StageAsync(AuditAction.Create, nameof(WhiteboardActivityTemplate), activity.Id.ToString(), cancellationToken: cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return await GetDtoAsync(activity.Id, cancellationToken);
        }

        public async Task<WhiteboardActivityDto> UpdateAsync(Guid id, SaveWhiteboardActivityRequest request, CancellationToken cancellationToken = default)
        {
            var activity = await _unitOfWork.Repository<WhiteboardActivityTemplate>().TrackedQuery()
                .Include(a => a.Items)
                .FirstOrDefaultAsync(a => a.Id == id, cancellationToken)
                ?? throw new NotFoundException(nameof(WhiteboardActivityTemplate), id);

            var (departmentId, course) = await ResolveScopeAsync(request, cancellationToken);
            ValidateItems(request.Mode, request.Items);

            activity.DepartmentId = departmentId;
            activity.CourseId = course?.Id;
            activity.Mode = request.Mode;
            activity.Prompt = request.Prompt.Trim();
            activity.DisplayOrder = request.DisplayOrder;

            // Replace the item set wholesale — same reasoning as QuizQuestionService's option
            // replacement: simpler and safer than diffing an admin form's free-edit list, and
            // items carry no history worth preserving individually.
            var itemRepo = _unitOfWork.Repository<WhiteboardActivityItem>();
            foreach (var existing in activity.Items.ToList())
            {
                itemRepo.Remove(existing);
            }
            activity.Items = request.Items
                .Select((i, idx) => new WhiteboardActivityItem
                {
                    WhiteboardActivityTemplateId = activity.Id,
                    Emoji = i.Emoji.Trim(),
                    Label = i.Label?.Trim(),
                    IsTarget = i.IsTarget,
                    DisplayOrder = idx,
                })
                .ToList();

            await _auditLog.StageAsync(AuditAction.Update, nameof(WhiteboardActivityTemplate), activity.Id.ToString(), cancellationToken: cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return await GetDtoAsync(activity.Id, cancellationToken);
        }

        public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
        {
            var repository = _unitOfWork.Repository<WhiteboardActivityTemplate>();
            var activity = await repository.TrackedQuery()
                .Include(a => a.Items)
                .FirstOrDefaultAsync(a => a.Id == id, cancellationToken)
                ?? throw new NotFoundException(nameof(WhiteboardActivityTemplate), id);

            var itemRepo = _unitOfWork.Repository<WhiteboardActivityItem>();
            foreach (var item in activity.Items)
            {
                itemRepo.Remove(item);
            }
            repository.Remove(activity);

            await _auditLog.StageAsync(AuditAction.Delete, nameof(WhiteboardActivityTemplate), id.ToString(), cancellationToken: cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }

        public async Task<IReadOnlyList<WhiteboardActivityDto>> GetForSessionAsync(
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
                return []; // nothing to resolve an activity bank against (e.g. a demo with no department set)
            }

            var activities = await _unitOfWork.Repository<WhiteboardActivityTemplate>().Query()
                .Include(a => a.Department)
                .Include(a => a.Course)
                .Include(a => a.Items)
                .Where(a => a.DepartmentId == departmentId.Value
                    && (a.CourseId == null || (courseId != null && a.CourseId == courseId)))
                .OrderBy(a => a.CourseId == null) // this course's own activities before the department-wide ones
                .ThenBy(a => a.DisplayOrder)
                .ToListAsync(cancellationToken);

            return activities.Select(ToDto).ToList();
        }

        private async Task<(Guid DepartmentId, Course? Course)> ResolveScopeAsync(
            SaveWhiteboardActivityRequest request, CancellationToken cancellationToken)
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
                throw new DomainValidationException("Set either a course or a department for this activity.");
            }

            if (!await _unitOfWork.Repository<Department>().ExistsAsync(d => d.Id == departmentId, cancellationToken))
            {
                throw new NotFoundException(nameof(Department), departmentId);
            }

            return (departmentId, null);
        }

        private static void ValidateItems(WhiteboardActivityMode mode, List<WhiteboardActivityItemInput> items)
        {
            if (items.Count < 2 || items.Count > 8)
            {
                throw new DomainValidationException("An activity needs between 2 and 8 items.");
            }

            if (items.Any(i => string.IsNullOrWhiteSpace(i.Emoji)))
            {
                throw new DomainValidationException("Every item needs an emoji.");
            }

            if (mode == WhiteboardActivityMode.Hotspot)
            {
                if (!items.Any(i => i.IsTarget) || items.All(i => i.IsTarget))
                {
                    throw new DomainValidationException("A hotspot activity needs at least one target item and at least one distractor.");
                }
            }
            else
            {
                if (items.Any(i => string.IsNullOrWhiteSpace(i.Label)))
                {
                    throw new DomainValidationException("Every item needs a match label for this mode.");
                }

                var labels = items.Select(i => i.Label!.Trim()).ToList();
                if (labels.Distinct(StringComparer.OrdinalIgnoreCase).Count() != labels.Count)
                {
                    throw new DomainValidationException("Match labels must be unique within an activity.");
                }
            }
        }

        private async Task<WhiteboardActivityDto> GetDtoAsync(Guid id, CancellationToken cancellationToken)
        {
            var activity = await _unitOfWork.Repository<WhiteboardActivityTemplate>().Query()
                .Include(a => a.Department)
                .Include(a => a.Course)
                .Include(a => a.Items)
                .FirstAsync(a => a.Id == id, cancellationToken);
            return ToDto(activity);
        }

        private static WhiteboardActivityDto ToDto(WhiteboardActivityTemplate activity) => new()
        {
            Id = activity.Id,
            DepartmentId = activity.DepartmentId,
            DepartmentName = activity.Department.Name,
            CourseId = activity.CourseId,
            CourseName = activity.Course?.Name,
            Mode = activity.Mode,
            Prompt = activity.Prompt,
            DisplayOrder = activity.DisplayOrder,
            Items = activity.Items
                .OrderBy(i => i.DisplayOrder)
                .Select(i => new WhiteboardActivityItemDto { Id = i.Id, Emoji = i.Emoji, Label = i.Label, IsTarget = i.IsTarget, DisplayOrder = i.DisplayOrder })
                .ToList(),
        };
    }
}
