using iucs.readernest.application.Common.Exceptions;
using iucs.readernest.application.Dto.Admission;
using iucs.readernest.domain.Entities.Academics;
using iucs.readernest.domain.Entities.Admission;
using iucs.readernest.domain.Entities.Users;
using iucs.readernest.domain.Enums;
using iucs.readernest.domain.Repository;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace iucs.readernest.application.Services
{
    public class ParentFeedbackService : IParentFeedbackService
    {
        /// <summary>
        /// How far back a finished demo / course still prompts. Bounded so a parent whose demo
        /// or course ended before this feature existed (or long ago) isn't asked out of the blue.
        /// </summary>
        private static readonly TimeSpan PromptWindow = TimeSpan.FromDays(30);

        private readonly IUnitOfWork _unitOfWork;
        private readonly INotificationService _notificationService;
        private readonly ILogger<ParentFeedbackService> _logger;

        public ParentFeedbackService(
            IUnitOfWork unitOfWork,
            INotificationService notificationService,
            ILogger<ParentFeedbackService> logger)
        {
            _unitOfWork = unitOfWork;
            _notificationService = notificationService;
            _logger = logger;
        }

        public async Task<IReadOnlyList<PendingParentFeedbackDto>> GetPendingAsync(
            Guid parentUserId, CancellationToken cancellationToken = default)
        {
            var user = await _unitOfWork.Repository<User>().Query()
                .FirstOrDefaultAsync(u => u.Id == parentUserId, cancellationToken)
                ?? throw new NotFoundException("User not found.");

            var since = DateTime.UtcNow - PromptWindow;
            var pending = new List<PendingParentFeedbackDto>();

            // Demos: a booking has no User/Child row, so it's matched to the parent by email --
            // the same link ParentJoinedAtUtc's capture uses.
            var email = user.Email.ToLower();
            var demos = await _unitOfWork.Repository<DemoBooking>().Query()
                .Include(d => d.ClassSession)
                .Where(d => d.ParentEmail.ToLower() == email
                            && d.ClassSession != null
                            && d.ClassSession.Status == SessionStatus.Completed
                            && (d.ClassSession.ActualEndAtUtc ?? d.ClassSession.ScheduledEndAtUtc) >= since)
                .ToListAsync(cancellationToken);
            if (demos.Count > 0)
            {
                var demoIds = demos.Select(d => d.Id).ToList();
                var rated = await _unitOfWork.Repository<ParentFeedback>().Query()
                    .Where(f => f.DemoBookingId != null && demoIds.Contains(f.DemoBookingId.Value))
                    .Select(f => f.DemoBookingId!.Value)
                    .ToListAsync(cancellationToken);
                pending.AddRange(demos.Where(d => !rated.Contains(d.Id)).Select(d => new PendingParentFeedbackDto
                {
                    Kind = ParentFeedbackKind.Demo,
                    DemoBookingId = d.Id,
                    ChildName = d.ChildName,
                    EndedAtUtc = d.ClassSession!.ActualEndAtUtc ?? d.ClassSession.ScheduledEndAtUtc,
                }));
            }

            // Courses: a child's batch is complete once Batch.CompletedAtUtc is stamped.
            var enrollments = await _unitOfWork.Repository<BatchEnrollment>().Query()
                .Include(e => e.Child)
                .Include(e => e.Batch).ThenInclude(b => b.Course)
                .Where(e => e.Child.ParentProfile.UserId == parentUserId
                            && e.Status != EnrollmentStatus.Withdrawn
                            && e.Batch.CompletedAtUtc != null
                            && e.Batch.CompletedAtUtc >= since)
                .ToListAsync(cancellationToken);
            if (enrollments.Count > 0)
            {
                var batchIds = enrollments.Select(e => e.BatchId).ToList();
                var rated = await _unitOfWork.Repository<ParentFeedback>().Query()
                    .Where(f => f.BatchId != null && batchIds.Contains(f.BatchId.Value))
                    .Select(f => new { BatchId = f.BatchId!.Value, ChildId = f.ChildId!.Value })
                    .ToListAsync(cancellationToken);
                pending.AddRange(enrollments
                    .Where(e => !rated.Any(r => r.BatchId == e.BatchId && r.ChildId == e.ChildId))
                    .Select(e => new PendingParentFeedbackDto
                    {
                        Kind = ParentFeedbackKind.CourseCompletion,
                        BatchId = e.BatchId,
                        ChildId = e.ChildId,
                        ChildName = $"{e.Child.FirstName} {e.Child.LastName}".Trim(),
                        CourseName = e.Batch.Course.Name,
                        EndedAtUtc = e.Batch.CompletedAtUtc!.Value,
                    }));
            }

            return pending.OrderByDescending(p => p.EndedAtUtc).ToList();
        }

        public async Task SubmitAsync(
            Guid parentUserId, SubmitParentFeedbackRequest request, CancellationToken cancellationToken = default)
        {
            var user = await _unitOfWork.Repository<User>().Query()
                .FirstOrDefaultAsync(u => u.Id == parentUserId, cancellationToken)
                ?? throw new NotFoundException("User not found.");

            // Ownership + "is it actually over" are checked against the same rules that produce
            // the prompt, so a parent can only rate their own finished demo / completed course.
            var pending = await GetPendingAsync(parentUserId, cancellationToken);
            var match = request.Kind == ParentFeedbackKind.Demo
                ? pending.FirstOrDefault(p => p.Kind == ParentFeedbackKind.Demo && p.DemoBookingId == request.DemoBookingId)
                : pending.FirstOrDefault(p => p.Kind == ParentFeedbackKind.CourseCompletion
                                              && p.BatchId == request.BatchId && p.ChildId == request.ChildId);
            if (match is null)
            {
                throw new DomainValidationException("There is no pending feedback for this class, or it has already been submitted.");
            }

            var comment = string.IsNullOrWhiteSpace(request.Comment) ? null : request.Comment.Trim();
            await _unitOfWork.Repository<ParentFeedback>().AddAsync(new ParentFeedback
            {
                Kind = request.Kind,
                ParentUserId = parentUserId,
                Rating = request.Rating,
                Comment = comment,
                DemoBookingId = match.DemoBookingId,
                BatchId = match.BatchId,
                ChildId = match.ChildId,
                SubmittedAtUtc = DateTime.UtcNow,
            }, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            await NotifyTeamAsync(user, match, request.Rating, comment, cancellationToken);
        }

        public async Task<IReadOnlyList<ParentFeedbackDto>> ListAsync(
            ParentFeedbackKind? kind, CancellationToken cancellationToken = default)
        {
            var query = _unitOfWork.Repository<ParentFeedback>().Query()
                .Include(f => f.ParentUser)
                .Include(f => f.DemoBooking).ThenInclude(d => d!.Department)
                .Include(f => f.Batch).ThenInclude(b => b!.Course)
                .Include(f => f.Child)
                .AsQueryable();
            if (kind is not null)
            {
                query = query.Where(f => f.Kind == kind);
            }

            var rows = await query.OrderByDescending(f => f.SubmittedAtUtc).ToListAsync(cancellationToken);
            return rows.Select(f => new ParentFeedbackDto
            {
                Id = f.Id,
                Kind = f.Kind,
                ParentName = $"{f.ParentUser.FirstName} {f.ParentUser.LastName}".Trim(),
                ParentEmail = f.ParentUser.Email,
                ChildName = f.Kind == ParentFeedbackKind.Demo
                    ? f.DemoBooking?.ChildName ?? "—"
                    : f.Child is null ? "—" : $"{f.Child.FirstName} {f.Child.LastName}".Trim(),
                Subject = f.Kind == ParentFeedbackKind.Demo ? f.DemoBooking?.Department?.Name : f.Batch?.Course?.Name,
                Rating = f.Rating,
                Comment = f.Comment,
                SubmittedAtUtc = f.SubmittedAtUtc,
            }).ToList();
        }

        /// <summary>Alerts every active Admin and Admission user. Best-effort: the rating is
        /// already saved, so a mail problem must not fail the parent's submit.</summary>
        private async Task NotifyTeamAsync(
            User parent, PendingParentFeedbackDto about, int rating, string? comment, CancellationToken cancellationToken)
        {
            try
            {
                var recipients = await _unitOfWork.Repository<User>().Query()
                    .Where(u => (u.Role == UserRole.Admin || u.Role == UserRole.AdmissionTeam) && u.Status == UserStatus.Active)
                    .ToListAsync(cancellationToken);
                var tokens = new Dictionary<string, string>
                {
                    ["Occasion"] = about.Kind == ParentFeedbackKind.Demo ? "the demo class" : $"the {about.CourseName} course",
                    ["ParentName"] = $"{parent.FirstName} {parent.LastName}".Trim(),
                    ["ChildName"] = about.ChildName,
                    ["Stars"] = $"{rating}/5",
                    ["Comment"] = string.IsNullOrEmpty(comment) ? "(no comment)" : comment,
                };
                foreach (var recipient in recipients)
                {
                    await _notificationService.SendTemplatedEmailAsync(
                        recipient.Id, recipient.Email, NotificationType.General, "parent-feedback-received", tokens, cancellationToken);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not send the parent-feedback alert for parent {ParentId}", parent.Id);
            }
        }
    }
}
