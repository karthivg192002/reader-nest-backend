using iucs.readernest.application.Common.Exceptions;
using iucs.readernest.application.Dto.Communication;
using iucs.readernest.domain.Entities.Communication;
using iucs.readernest.domain.Entities.Users;
using iucs.readernest.domain.Enums;
using iucs.readernest.domain.Repository;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace iucs.readernest.application.Services
{
    public class SupportTicketService : ISupportTicketService
    {
        private const int PreviewLength = 140;

        private readonly IUnitOfWork _unitOfWork;
        private readonly INotificationService _notificationService;
        private readonly IConfiguration _configuration;
        private readonly ILogger<SupportTicketService> _logger;

        public SupportTicketService(
            IUnitOfWork unitOfWork,
            INotificationService notificationService,
            IConfiguration configuration,
            ILogger<SupportTicketService> logger)
        {
            _unitOfWork = unitOfWork;
            _notificationService = notificationService;
            _configuration = configuration;
            _logger = logger;
        }

        public async Task<IReadOnlyList<SupportTicketSummaryDto>> ListForParentAsync(
            Guid parentUserId, CancellationToken cancellationToken = default)
        {
            return await SummaryQuery(BaseQuery().Where(t => t.ParentUserId == parentUserId), cancellationToken);
        }

        public async Task<SupportTicketDetailDto> GetForParentAsync(
            Guid parentUserId, Guid ticketId, CancellationToken cancellationToken = default)
        {
            // Someone else's ticket answers 404, not 403, so ids can't be probed for existence.
            await EnsureParentOwnsAsync(parentUserId, ticketId, cancellationToken);
            return await LoadDetailAsync(ticketId, cancellationToken);
        }

        public async Task<SupportTicketDetailDto> CreateAsync(
            Guid parentUserId, CreateSupportTicketRequest request, CancellationToken cancellationToken = default)
        {
            var subject = request.Subject?.Trim() ?? string.Empty;
            var message = request.Message?.Trim() ?? string.Empty;
            if (subject.Length == 0 || message.Length == 0)
            {
                throw new DomainValidationException("Please add a subject and describe your concern.");
            }

            var parent = await _unitOfWork.Repository<User>().Query()
                .FirstOrDefaultAsync(u => u.Id == parentUserId && u.Role == UserRole.Parent, cancellationToken)
                ?? throw new NotFoundException("Parent account not found.");

            if (request.ChildId is not null)
            {
                var ownsChild = await _unitOfWork.Repository<Child>().Query()
                    .AnyAsync(c => c.Id == request.ChildId && c.ParentProfile.UserId == parentUserId, cancellationToken);
                if (!ownsChild)
                {
                    throw new DomainValidationException("Please pick one of your own children.");
                }
            }

            var now = DateTime.UtcNow;
            var ticket = new SupportTicket
            {
                ParentUserId = parentUserId,
                ChildId = request.ChildId,
                Category = request.Category,
                Subject = subject,
                Status = SupportTicketStatus.Open,
                LastActivityAtUtc = now,
                AwaitingStaffReply = true,
            };
            ticket.Messages.Add(new SupportTicketMessage
            {
                AuthorUserId = parentUserId,
                IsStaff = false,
                Body = message,
            });
            await _unitOfWork.Repository<SupportTicket>().AddAsync(ticket, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            await NotifyStaffAsync("support-ticket-raised", ticket, parent, message, cancellationToken);
            return await LoadDetailAsync(ticket.Id, cancellationToken);
        }

        public async Task<SupportTicketDetailDto> ParentReplyAsync(
            Guid parentUserId, Guid ticketId, ReplySupportTicketRequest request, CancellationToken cancellationToken = default)
        {
            var message = RequireMessage(request);
            await EnsureParentOwnsAsync(parentUserId, ticketId, cancellationToken);

            var ticket = await TrackedTicketAsync(ticketId, cancellationToken);
            if (ticket.Status is SupportTicketStatus.Resolved or SupportTicketStatus.Closed)
            {
                ticket.Status = SupportTicketStatus.Open;
                ticket.ResolvedAtUtc = null;
            }

            ticket.AwaitingStaffReply = true;
            ticket.LastActivityAtUtc = DateTime.UtcNow;
            await _unitOfWork.Repository<SupportTicketMessage>().AddAsync(new SupportTicketMessage
            {
                SupportTicketId = ticket.Id,
                AuthorUserId = parentUserId,
                IsStaff = false,
                Body = message,
            }, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            var parent = await _unitOfWork.Repository<User>().Query()
                .FirstAsync(u => u.Id == parentUserId, cancellationToken);
            await NotifyStaffAsync("support-ticket-parent-replied", ticket, parent, message, cancellationToken);
            return await LoadDetailAsync(ticket.Id, cancellationToken);
        }

        public async Task<IReadOnlyList<SupportTicketSummaryDto>> ListForStaffAsync(
            SupportTicketStatus? status, string? search, CancellationToken cancellationToken = default)
        {
            var query = BaseQuery();
            if (status is not null)
            {
                query = query.Where(t => t.Status == status);
            }

            if (!string.IsNullOrWhiteSpace(search))
            {
                var term = search.Trim().ToLower();
                query = query.Where(t =>
                    t.Subject.ToLower().Contains(term) ||
                    t.ParentUser.Email.ToLower().Contains(term) ||
                    (t.ParentUser.FirstName + " " + t.ParentUser.LastName).ToLower().Contains(term) ||
                    (t.Child != null && (t.Child.FirstName + " " + t.Child.LastName).ToLower().Contains(term)));
            }

            return await SummaryQuery(query, cancellationToken);
        }

        public async Task<SupportTicketCountsDto> GetCountsAsync(CancellationToken cancellationToken = default)
        {
            var byStatus = await BaseQuery()
                .GroupBy(t => t.Status)
                .Select(g => new { Status = g.Key, Count = g.Count() })
                .ToListAsync(cancellationToken);
            var awaiting = await BaseQuery()
                .CountAsync(t => t.AwaitingStaffReply
                                 && (t.Status == SupportTicketStatus.Open || t.Status == SupportTicketStatus.InProgress),
                    cancellationToken);

            int CountOf(SupportTicketStatus s) => byStatus.FirstOrDefault(x => x.Status == s)?.Count ?? 0;
            return new SupportTicketCountsDto
            {
                Open = CountOf(SupportTicketStatus.Open),
                InProgress = CountOf(SupportTicketStatus.InProgress),
                Resolved = CountOf(SupportTicketStatus.Resolved),
                Closed = CountOf(SupportTicketStatus.Closed),
                AwaitingReply = awaiting,
            };
        }

        public async Task<SupportTicketDetailDto> GetForStaffAsync(Guid ticketId, CancellationToken cancellationToken = default)
        {
            return await LoadDetailAsync(ticketId, cancellationToken);
        }

        public async Task<SupportTicketDetailDto> StaffReplyAsync(
            Guid staffUserId, Guid ticketId, ReplySupportTicketRequest request, CancellationToken cancellationToken = default)
        {
            var message = RequireMessage(request);
            var ticket = await TrackedTicketAsync(ticketId, cancellationToken);
            if (ticket.Status == SupportTicketStatus.Open)
            {
                ticket.Status = SupportTicketStatus.InProgress;
            }

            ticket.AwaitingStaffReply = false;
            ticket.LastActivityAtUtc = DateTime.UtcNow;
            await _unitOfWork.Repository<SupportTicketMessage>().AddAsync(new SupportTicketMessage
            {
                SupportTicketId = ticket.Id,
                AuthorUserId = staffUserId,
                IsStaff = true,
                Body = message,
            }, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            var staff = await _unitOfWork.Repository<User>().Query()
                .FirstAsync(u => u.Id == staffUserId, cancellationToken);
            await NotifyParentAsync(ticket, $"{staff.FirstName} from our team wrote: “{message}”", cancellationToken);
            return await LoadDetailAsync(ticket.Id, cancellationToken);
        }

        public async Task<SupportTicketDetailDto> UpdateStatusAsync(
            Guid staffUserId, Guid ticketId, SupportTicketStatus status, CancellationToken cancellationToken = default)
        {
            var ticket = await TrackedTicketAsync(ticketId, cancellationToken);
            if (ticket.Status == status)
            {
                return await LoadDetailAsync(ticket.Id, cancellationToken);
            }

            ticket.Status = status;
            ticket.LastActivityAtUtc = DateTime.UtcNow;
            ticket.ResolvedAtUtc = status is SupportTicketStatus.Resolved or SupportTicketStatus.Closed ? DateTime.UtcNow : null;
            if (status is SupportTicketStatus.Resolved or SupportTicketStatus.Closed)
            {
                ticket.AwaitingStaffReply = false;
            }

            await _unitOfWork.SaveChangesAsync(cancellationToken);

            // Only outcomes a family needs to hear about; the Open <-> In Progress shuffle is internal triage.
            if (status is SupportTicketStatus.Resolved or SupportTicketStatus.Closed)
            {
                var update = status == SupportTicketStatus.Resolved
                    ? "Our team has marked this ticket as resolved. If anything still needs attention, just reply on the ticket and it will reopen."
                    : "This ticket has been closed. You can reply on it any time to reopen it, or raise a new ticket.";
                await NotifyParentAsync(ticket, update, cancellationToken);
            }

            return await LoadDetailAsync(ticket.Id, cancellationToken);
        }

        public static string ReferenceFor(Guid id) => $"T-{id.ToString("N")[..8].ToUpperInvariant()}";

        private IQueryable<SupportTicket> BaseQuery() => _unitOfWork.Repository<SupportTicket>().Query();

        private static async Task<IReadOnlyList<SupportTicketSummaryDto>> SummaryQuery(
            IQueryable<SupportTicket> query, CancellationToken cancellationToken)
        {
            var rows = await query
                .OrderByDescending(t => t.LastActivityAtUtc)
                .Select(t => new
                {
                    t.Id,
                    t.Subject,
                    t.Category,
                    t.Status,
                    t.ParentUserId,
                    ParentFirst = t.ParentUser.FirstName,
                    ParentLast = t.ParentUser.LastName,
                    ParentEmail = t.ParentUser.Email,
                    ParentPhone = t.ParentUser.Phone,
                    t.ChildId,
                    ChildFirst = t.Child != null ? t.Child.FirstName : null,
                    ChildLast = t.Child != null ? t.Child.LastName : null,
                    t.AwaitingStaffReply,
                    MessageCount = t.Messages.Count(),
                    LastMessage = t.Messages.OrderByDescending(m => m.CreatedAtUtc).Select(m => m.Body).FirstOrDefault(),
                    t.CreatedAtUtc,
                    t.LastActivityAtUtc,
                    t.ResolvedAtUtc,
                })
                .ToListAsync(cancellationToken);

            return rows.Select(r => new SupportTicketSummaryDto
            {
                Id = r.Id,
                Reference = ReferenceFor(r.Id),
                Subject = r.Subject,
                Category = r.Category,
                Status = r.Status,
                ParentUserId = r.ParentUserId,
                ParentName = $"{r.ParentFirst} {r.ParentLast}".Trim(),
                ParentEmail = r.ParentEmail,
                ParentPhone = r.ParentPhone,
                ChildId = r.ChildId,
                ChildName = r.ChildFirst is null ? null : $"{r.ChildFirst} {r.ChildLast}".Trim(),
                AwaitingStaffReply = r.AwaitingStaffReply,
                MessageCount = r.MessageCount,
                LastMessagePreview = Preview(r.LastMessage),
                CreatedAtUtc = r.CreatedAtUtc,
                LastActivityAtUtc = r.LastActivityAtUtc,
                ResolvedAtUtc = r.ResolvedAtUtc,
            }).ToList();
        }

        private async Task<SupportTicketDetailDto> LoadDetailAsync(Guid ticketId, CancellationToken cancellationToken)
        {
            var ticket = await BaseQuery()
                .Include(t => t.ParentUser)
                .Include(t => t.Child)
                .Include(t => t.Messages).ThenInclude(m => m.AuthorUser)
                .AsSplitQuery()
                .FirstOrDefaultAsync(t => t.Id == ticketId, cancellationToken)
                ?? throw new NotFoundException("Support ticket", ticketId);

            var messages = ticket.Messages.OrderBy(m => m.CreatedAtUtc).ToList();
            return new SupportTicketDetailDto
            {
                Id = ticket.Id,
                Reference = ReferenceFor(ticket.Id),
                Subject = ticket.Subject,
                Category = ticket.Category,
                Status = ticket.Status,
                ParentUserId = ticket.ParentUserId,
                ParentName = $"{ticket.ParentUser.FirstName} {ticket.ParentUser.LastName}".Trim(),
                ParentEmail = ticket.ParentUser.Email,
                ParentPhone = ticket.ParentUser.Phone,
                ChildId = ticket.ChildId,
                ChildName = ticket.Child is null ? null : $"{ticket.Child.FirstName} {ticket.Child.LastName}".Trim(),
                AwaitingStaffReply = ticket.AwaitingStaffReply,
                MessageCount = messages.Count,
                LastMessagePreview = Preview(messages.LastOrDefault()?.Body),
                CreatedAtUtc = ticket.CreatedAtUtc,
                LastActivityAtUtc = ticket.LastActivityAtUtc,
                ResolvedAtUtc = ticket.ResolvedAtUtc,
                Messages = messages.Select(m => new SupportTicketMessageDto
                {
                    Id = m.Id,
                    AuthorName = $"{m.AuthorUser.FirstName} {m.AuthorUser.LastName}".Trim(),
                    IsStaff = m.IsStaff,
                    Body = m.Body,
                    CreatedAtUtc = m.CreatedAtUtc,
                }).ToList(),
            };
        }

        private async Task EnsureParentOwnsAsync(Guid parentUserId, Guid ticketId, CancellationToken cancellationToken)
        {
            var owns = await BaseQuery().AnyAsync(t => t.Id == ticketId && t.ParentUserId == parentUserId, cancellationToken);
            if (!owns)
            {
                throw new NotFoundException("Support ticket", ticketId);
            }
        }

        private async Task<SupportTicket> TrackedTicketAsync(Guid ticketId, CancellationToken cancellationToken)
        {
            return await _unitOfWork.Repository<SupportTicket>().TrackedQuery()
                .FirstOrDefaultAsync(t => t.Id == ticketId, cancellationToken)
                ?? throw new NotFoundException("Support ticket", ticketId);
        }

        private static string RequireMessage(ReplySupportTicketRequest request)
        {
            var message = request.Message?.Trim() ?? string.Empty;
            if (message.Length == 0)
            {
                throw new DomainValidationException("Please write a message.");
            }

            return message;
        }

        private static string? Preview(string? body)
        {
            if (string.IsNullOrEmpty(body))
            {
                return null;
            }

            return body.Length <= PreviewLength ? body : body[..PreviewLength].TrimEnd() + "…";
        }

        private string FrontendBaseUrl() => (_configuration["Frontend:BaseUrl"] ?? "http://localhost:5173").TrimEnd('/');

        /// <summary>
        /// Alerts every active Admin plus every active Sub Admin holding SupportTickets:View.
        /// Best-effort: the ticket is already saved, so a mail problem must not fail the parent's action.
        /// </summary>
        private async Task NotifyStaffAsync(
            string templateKey, SupportTicket ticket, User parent, string message, CancellationToken cancellationToken)
        {
            try
            {
                var module = PermissionModule.SupportTickets.ToString();
                var grants = _unitOfWork.Repository<SubAdminPermission>().Query();
                var recipients = await _unitOfWork.Repository<User>().Query()
                    .Where(u => u.Status == UserStatus.Active
                                && (u.Role == UserRole.Admin
                                    || (u.Role == UserRole.SubAdmin
                                        && grants.Any(p => p.UserId == u.Id && p.Module == module && p.CanView))))
                    .ToListAsync(cancellationToken);

                var baseUrl = FrontendBaseUrl();
                var tokens = new Dictionary<string, string>
                {
                    ["Reference"] = ReferenceFor(ticket.Id),
                    ["ParentName"] = $"{parent.FirstName} {parent.LastName}".Trim(),
                    ["Category"] = CategoryLabel(ticket.Category),
                    ["Subject"] = ticket.Subject,
                    ["Message"] = message,
                };
                foreach (var recipient in recipients)
                {
                    var portal = recipient.Role == UserRole.Admin ? "admin" : "subadmin";
                    var recipientTokens = new Dictionary<string, string>(tokens)
                    {
                        ["TicketUrl"] = $"{baseUrl}/{portal}/support-tickets?ticket={ticket.Id}",
                    };
                    await _notificationService.SendTemplatedEmailAsync(
                        recipient.Id, recipient.Email, NotificationType.General, templateKey, recipientTokens, cancellationToken);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not send the support-ticket staff alert for ticket {TicketId}", ticket.Id);
            }
        }

        /// <summary>Emails (and bell-notifies) the parent. Best-effort, like <see cref="NotifyStaffAsync"/>.</summary>
        private async Task NotifyParentAsync(SupportTicket ticket, string update, CancellationToken cancellationToken)
        {
            try
            {
                var parent = await _unitOfWork.Repository<User>().Query()
                    .FirstAsync(u => u.Id == ticket.ParentUserId, cancellationToken);
                await _notificationService.SendTemplatedEmailAsync(
                    parent.Id, parent.Email, NotificationType.General, "support-ticket-updated",
                    new Dictionary<string, string>
                    {
                        ["Reference"] = ReferenceFor(ticket.Id),
                        ["Subject"] = ticket.Subject,
                        ["Update"] = update,
                        ["Status"] = StatusLabel(ticket.Status),
                        ["TicketUrl"] = $"{FrontendBaseUrl()}/parent/support?ticket={ticket.Id}",
                    },
                    cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not send the support-ticket update to the parent for ticket {TicketId}", ticket.Id);
            }
        }

        private static string CategoryLabel(SupportTicketCategory category) => category switch
        {
            SupportTicketCategory.ClassSchedule => "Class timing / schedule",
            SupportTicketCategory.Teacher => "Teacher",
            SupportTicketCategory.BillingPayment => "Billing / payment",
            SupportTicketCategory.Technical => "Technical issue",
            SupportTicketCategory.Enrollment => "Enrollment",
            _ => "Other",
        };

        private static string StatusLabel(SupportTicketStatus status) => status switch
        {
            SupportTicketStatus.InProgress => "In progress",
            _ => status.ToString(),
        };
    }
}
