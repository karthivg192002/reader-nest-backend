using System.Linq.Expressions;
using System.Text.Json;
using System.Text.Json.Serialization;
using iucs.readernest.application.Common.Exceptions;
using iucs.readernest.application.Dto.Users;
using iucs.readernest.domain.Entities.Academics;
using iucs.readernest.domain.Entities.Admission;
using iucs.readernest.domain.Entities.Auditing;
using iucs.readernest.domain.Entities.Billing;
using iucs.readernest.domain.Entities.Common;
using iucs.readernest.domain.Entities.Communication;
using iucs.readernest.domain.Entities.Notes;
using iucs.readernest.domain.Entities.Resources;
using iucs.readernest.domain.Entities.Sessions;
using iucs.readernest.domain.Entities.Users;
using iucs.readernest.domain.Enums;
using iucs.readernest.domain.Repository;
using Microsoft.EntityFrameworkCore;

namespace iucs.readernest.application.Services
{
    public class UserDeletionService : IUserDeletionService
    {
        // Cycle-safe and compact: entities here are always loaded AsNoTracking with no
        // .Include(), so every navigation property is already null and gets dropped from the
        // snapshot entirely instead of serializing as a noisy "null" for every relation.
        private static readonly JsonSerializerOptions SnapshotJsonOptions = new()
        {
            ReferenceHandler = ReferenceHandler.IgnoreCycles,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        };

        private readonly IUnitOfWork _unitOfWork;
        private readonly IAuditLogService _auditLog;

        public UserDeletionService(IUnitOfWork unitOfWork, IAuditLogService auditLog)
        {
            _unitOfWork = unitOfWork;
            _auditLog = auditLog;
        }

        public async Task<DataDeletionPreviewDto> PreviewStudentDeletionAsync(Guid childId, CancellationToken cancellationToken = default)
        {
            var child = await _unitOfWork.Repository<Child>().GetByIdAsync(childId, cancellationToken)
                ?? throw new NotFoundException(nameof(Child), childId);

            var items = await RunChildCascadeAsync(childId, dryRun: true, Guid.Empty, "Student", childId, Guid.Empty, cancellationToken);
            return ToPreviewDto($"{child.FirstName} {child.LastName}".Trim(), items);
        }

        public async Task<DataDeletionPreviewDto> PreviewParentDeletionAsync(Guid parentUserId, CancellationToken cancellationToken = default)
        {
            var (user, parentProfile) = await LoadParentAsync(parentUserId, cancellationToken);
            var childIds = await _unitOfWork.Repository<Child>().Query()
                .Where(c => c.ParentProfileId == parentProfile.Id).Select(c => c.Id).ToListAsync(cancellationToken);

            var items = new List<(string Table, string Label, int Count)>();
            foreach (var childId in childIds)
            {
                items.AddRange(await RunChildCascadeAsync(childId, dryRun: true, Guid.Empty, "Parent", parentUserId, Guid.Empty, cancellationToken));
            }
            items.AddRange(await RunParentOnlyCascadeAsync(parentUserId, parentProfile.Id, dryRun: true, Guid.Empty, Guid.Empty, cancellationToken));

            // Multiple children each contribute their own "Invoice"/"BatchEnrollment"/... rows —
            // the popup shows one combined count per table for the whole family, not one block
            // per child.
            var merged = items
                .GroupBy(i => (i.Table, i.Label))
                .Select(g => (g.Key.Table, g.Key.Label, Count: g.Sum(x => x.Count)))
                .ToList();

            return ToPreviewDto($"{user.FirstName} {user.LastName}".Trim(), merged);
        }

        public async Task DeleteStudentAsync(Guid childId, Guid deletedByUserId, CancellationToken cancellationToken = default)
        {
            var child = await _unitOfWork.Repository<Child>().GetByIdAsync(childId, cancellationToken)
                ?? throw new NotFoundException(nameof(Child), childId);
            var childName = $"{child.FirstName} {child.LastName}".Trim();

            await _unitOfWork.ExecuteInSerializableTransactionAsync(async token =>
            {
                var batchId = Guid.NewGuid();
                await RunChildCascadeAsync(childId, dryRun: false, batchId, "Student", childId, deletedByUserId, token);

                await _auditLog.StageAsync(
                    AuditAction.Delete, nameof(Child), childId.ToString(),
                    changesJson: $"{{\"hardDelete\":true,\"deletionBatchId\":\"{batchId}\",\"name\":\"{EscapeForJson(childName)}\"}}",
                    cancellationToken: token);

                await _unitOfWork.SaveChangesAsync(token);
                return true;
            }, cancellationToken);
        }

        public async Task DeleteParentAsync(Guid parentUserId, Guid deletedByUserId, CancellationToken cancellationToken = default)
        {
            var (user, parentProfile) = await LoadParentAsync(parentUserId, cancellationToken);
            var parentName = $"{user.FirstName} {user.LastName}".Trim();
            var childIds = await _unitOfWork.Repository<Child>().Query()
                .Where(c => c.ParentProfileId == parentProfile.Id).Select(c => c.Id).ToListAsync(cancellationToken);

            await _unitOfWork.ExecuteInSerializableTransactionAsync(async token =>
            {
                var batchId = Guid.NewGuid();
                foreach (var childId in childIds)
                {
                    await RunChildCascadeAsync(childId, dryRun: false, batchId, "Parent", parentUserId, deletedByUserId, token);
                }
                await RunParentOnlyCascadeAsync(parentUserId, parentProfile.Id, dryRun: false, batchId, deletedByUserId, token);

                await _auditLog.StageAsync(
                    AuditAction.Delete, nameof(User), parentUserId.ToString(),
                    changesJson: $"{{\"hardDelete\":true,\"deletionBatchId\":\"{batchId}\",\"name\":\"{EscapeForJson(parentName)}\",\"childrenDeleted\":{childIds.Count}}}",
                    cancellationToken: token);

                await _unitOfWork.SaveChangesAsync(token);
                return true;
            }, cancellationToken);
        }

        private async Task<(User User, ParentProfile ParentProfile)> LoadParentAsync(Guid userId, CancellationToken cancellationToken)
        {
            var user = await _unitOfWork.Repository<User>().GetByIdAsync(userId, cancellationToken)
                ?? throw new NotFoundException(nameof(User), userId);
            if (user.Role != UserRole.Parent)
            {
                throw new DomainValidationException("Hard delete is only available for Parent and Student accounts.");
            }
            var parentProfile = await _unitOfWork.Repository<ParentProfile>().FirstOrDefaultAsync(p => p.UserId == userId, cancellationToken)
                ?? throw new DomainValidationException("This account has no parent profile to delete.");
            return (user, parentProfile);
        }

        private static DataDeletionPreviewDto ToPreviewDto(string targetName, IEnumerable<(string Table, string Label, int Count)> items) => new()
        {
            TargetName = targetName,
            Items = items.Select(i => new DataDeletionPreviewItemDto { Table = i.Table, Label = i.Label, Count = i.Count }).ToList(),
        };

        private static string EscapeForJson(string value) => value.Replace("\\", "\\\\").Replace("\"", "\\\"");

        /// <summary>
        /// Everything that belongs to exactly one Child: their invoices (and those invoices'
        /// payment/refund trail), subscriptions, batch enrollments, enrollment forms, progress
        /// reports, gamification awards, attendance and engagement records, and their own fee
        /// suspensions — then the Child row itself. Never touches the parent account, siblings,
        /// or shared/common records (Batch, ClassSession, TeacherProfile) those tables also point
        /// at. <paramref name="rootEntityType"/>/<paramref name="rootEntityId"/> let a Parent-level
        /// delete attribute every row here to the ONE parent batch rather than a separate
        /// per-child one.
        /// </summary>
        private async Task<List<(string Table, string Label, int Count)>> RunChildCascadeAsync(
            Guid childId, bool dryRun, Guid batchId, string rootEntityType, Guid rootEntityId, Guid deletedByUserId,
            CancellationToken cancellationToken)
        {
            var results = new List<(string, string, int)>();

            async Task Track<TEntity>(Expression<Func<TEntity, bool>> predicate, string table, string label)
                where TEntity : BaseEntity
            {
                var count = await ProcessAsync(predicate, table, label, dryRun, batchId, rootEntityType, rootEntityId, deletedByUserId, cancellationToken);
                if (count > 0) results.Add((table, label, count));
            }

            var invoiceIds = await _unitOfWork.Repository<Invoice>().Query()
                .Where(i => i.ChildId == childId).Select(i => i.Id).ToListAsync(cancellationToken);

            if (invoiceIds.Count > 0)
            {
                var txnIds = await _unitOfWork.Repository<PaymentTransaction>().Query()
                    .Where(t => invoiceIds.Contains(t.InvoiceId)).Select(t => t.Id).ToListAsync(cancellationToken);

                if (txnIds.Count > 0)
                {
                    await Track<Refund>(r => txnIds.Contains(r.PaymentTransactionId), "Refund", "Refunds");
                    await Track<PaymentTransaction>(t => txnIds.Contains(t.Id), "PaymentTransaction", "Payment transactions");
                }

                if (!dryRun)
                {
                    await DetachInvoiceReferencesAsync(invoiceIds, deletedByUserId, cancellationToken);
                }

                await Track<Invoice>(i => invoiceIds.Contains(i.Id), "Invoice", "Invoices");
            }

            await Track<Subscription>(s => s.ChildId == childId, "Subscription", "Subscriptions");
            await Track<BatchEnrollment>(e => e.ChildId == childId, "BatchEnrollment", "Batch enrollments");
            await Track<EnrollmentForm>(f => f.ChildId == childId, "EnrollmentForm", "Enrollment forms");
            await Track<ProgressReport>(p => p.ChildId == childId, "ProgressReport", "Progress reports");
            await Track<StudentAward>(a => a.ChildId == childId, "StudentAward", "Gamification awards");
            await Track<SessionAttendance>(a => a.ChildId == childId, "SessionAttendance", "Class attendance records");
            await Track<EngagementEvent>(e => e.ChildId == childId, "EngagementEvent", "Engagement/analytics events");
            // Only this child's OWN fee suspensions -- a family-wide one (ChildId null) is
            // handled once at the parent level (RunParentOnlyCascadeAsync), not duplicated here.
            await Track<FeeSuspension>(f => f.ChildId == childId, "FeeSuspension", "Fee suspensions");

            if (!dryRun)
            {
                // Preserve, don't delete: ClassSessionEventLog is an append-only lifecycle trail
                // ("exists purely to be read, not acted on" -- its own doc comment), the same
                // spirit this whole feature exists to extend. Sever the reference instead so
                // Restrict doesn't block the Child row below; ParticipantName already carries a
                // denormalized snapshot for exactly this "child no longer resolvable" case.
                await _unitOfWork.Repository<ClassSessionEventLog>().ExecuteUpdateAsync(
                    l => l.ChildId == childId,
                    s => s.SetProperty(l => l.ChildId, (Guid?)null),
                    cancellationToken);
            }

            await Track<Child>(c => c.Id == childId, "Child", "Student record");

            return results;
        }

        /// <summary>
        /// Everything that belongs to the Parent account itself, not to any specific child:
        /// family-level invoices/fee suspensions (ChildId null), enrollment forms submitted
        /// before a child record existed, resource access grants, notifications, chat/floating-
        /// note history, short links, bulk-email records — then the ParentProfile and User rows
        /// themselves. Call after every child has already gone through
        /// <see cref="RunChildCascadeAsync"/>.
        /// </summary>
        private async Task<List<(string Table, string Label, int Count)>> RunParentOnlyCascadeAsync(
            Guid userId, Guid parentProfileId, bool dryRun, Guid batchId, Guid deletedByUserId, CancellationToken cancellationToken)
        {
            var results = new List<(string, string, int)>();
            const string rootType = "Parent";

            async Task Track<TEntity>(Expression<Func<TEntity, bool>> predicate, string table, string label)
                where TEntity : BaseEntity
            {
                var count = await ProcessAsync(predicate, table, label, dryRun, batchId, rootType, userId, deletedByUserId, cancellationToken);
                if (count > 0) results.Add((table, label, count));
            }

            // Family-level invoices (no specific child) and their own payment/refund trail.
            var invoiceIds = await _unitOfWork.Repository<Invoice>().Query()
                .Where(i => i.ParentProfileId == parentProfileId && i.ChildId == null).Select(i => i.Id).ToListAsync(cancellationToken);
            if (invoiceIds.Count > 0)
            {
                var txnIds = await _unitOfWork.Repository<PaymentTransaction>().Query()
                    .Where(t => invoiceIds.Contains(t.InvoiceId)).Select(t => t.Id).ToListAsync(cancellationToken);
                if (txnIds.Count > 0)
                {
                    await Track<Refund>(r => txnIds.Contains(r.PaymentTransactionId), "Refund", "Refunds");
                    await Track<PaymentTransaction>(t => txnIds.Contains(t.Id), "PaymentTransaction", "Payment transactions");
                }
                if (!dryRun)
                {
                    await DetachInvoiceReferencesAsync(invoiceIds, deletedByUserId, cancellationToken);
                }
                await Track<Invoice>(i => invoiceIds.Contains(i.Id), "Invoice", "Invoices");
            }

            await Track<EnrollmentForm>(f => f.ParentProfileId == parentProfileId && f.ChildId == null, "EnrollmentForm", "Enrollment forms");
            // Family-wide fee suspension (no specific child) -- see RunChildCascadeAsync's own note.
            await Track<FeeSuspension>(f => f.ParentProfileId == parentProfileId && f.ChildId == null, "FeeSuspension", "Fee suspensions");
            await Track<ResourceAccess>(r => r.ParentProfileId == parentProfileId, "ResourceAccess", "Resource access grants");

            await Track<Notification>(n => n.RecipientUserId == userId, "Notification", "Notifications");
            await Track<ChatMessage>(m => m.UserId == userId, "ChatMessage", "Chatbot conversation history");
            await Track<ChatEscalation>(e => e.UserId == userId, "ChatEscalation", "Escalated chatbot questions");
            await Track<FloatingNote>(n => n.UserId == userId, "FloatingNote", "Personal notes");
            await Track<ShortLink>(s => s.CreatedByUserId == userId, "ShortLink", "Short links created");
            await Track<BulkEmailRecipient>(r => r.RecipientUserId == userId, "BulkEmailRecipient", "Bulk email deliveries");
            await Track<BulkEmailReply>(r => r.ParentUserId == userId, "BulkEmailReply", "Bulk email replies");
            await Track<PinResetToken>(t => t.UserId == userId, "PinResetToken", "PIN reset requests");
            await Track<AccessRequest>(r => r.RequestedByUserId == userId, "AccessRequest", "Access requests");
            // SubAdminPermission never applies to a Parent-role account, kept for completeness —
            // this account's own Role already routed it here, not the Sub Admin permission path.
            await Track<SubAdminPermission>(p => p.UserId == userId, "SubAdminPermission", "Sub Admin permission grants");

            if (!dryRun)
            {
                // Preserve, don't delete -- same reasoning as RunChildCascadeAsync's ClassSessionEventLog
                // detach. A ChatEscalation this user merely RESOLVED (not asked) belongs to someone
                // else's question and must survive; only sever the resolver reference.
                await _unitOfWork.Repository<ClassSessionEventLog>().ExecuteUpdateAsync(
                    l => l.UserId == userId,
                    s => s.SetProperty(l => l.UserId, (Guid?)null),
                    cancellationToken);
                await _unitOfWork.Repository<ChatEscalation>().ExecuteUpdateAsync(
                    e => e.ResolvedByUserId == userId,
                    s => s.SetProperty(e => e.ResolvedByUserId, (Guid?)null).SetProperty(e => e.UpdatedAtUtc, DateTime.UtcNow).SetProperty(e => e.UpdatedBy, deletedByUserId),
                    cancellationToken);
            }

            await Track<ParentProfile>(p => p.Id == parentProfileId, "ParentProfile", "Parent profile");
            await Track<User>(u => u.Id == userId, "User", "Account");

            return results;
        }

        /// <summary>
        /// Sever (never delete) FeeSuspension/DemoBooking rows still pointing at an invoice
        /// that's about to be removed -- both represent something independent of this specific
        /// child/invoice (a family-wide suspension, a lead's demo booking) and must survive the
        /// invoice they happened to reference going away.
        /// </summary>
        private async Task DetachInvoiceReferencesAsync(List<Guid> invoiceIds, Guid deletedByUserId, CancellationToken cancellationToken)
        {
            await _unitOfWork.Repository<FeeSuspension>().ExecuteUpdateAsync(
                f => f.InvoiceId != null && invoiceIds.Contains(f.InvoiceId!.Value),
                s => s.SetProperty(f => f.InvoiceId, (Guid?)null).SetProperty(f => f.UpdatedAtUtc, DateTime.UtcNow).SetProperty(f => f.UpdatedBy, deletedByUserId),
                cancellationToken);
            await _unitOfWork.Repository<DemoBooking>().ExecuteUpdateAsync(
                d => d.InvoiceId != null && invoiceIds.Contains(d.InvoiceId!.Value),
                s => s.SetProperty(d => d.InvoiceId, (Guid?)null).SetProperty(d => d.UpdatedAtUtc, DateTime.UtcNow).SetProperty(d => d.UpdatedBy, deletedByUserId),
                cancellationToken);
        }

        /// <summary>
        /// One table's worth of work: count the matching rows (always — both preview and real
        /// runs need the count), and when <paramref name="dryRun"/> is false, snapshot each one
        /// into DataDeletionLog and then actually remove them. Sharing this one body between
        /// preview and delete is deliberate — the two must never be able to drift apart and show
        /// a popup that doesn't match what actually gets deleted.
        /// </summary>
        private async Task<int> ProcessAsync<TEntity>(
            Expression<Func<TEntity, bool>> predicate,
            string table,
            string label,
            bool dryRun,
            Guid batchId,
            string rootEntityType,
            Guid rootEntityId,
            Guid deletedByUserId,
            CancellationToken cancellationToken)
            where TEntity : BaseEntity
        {
            var repository = _unitOfWork.Repository<TEntity>();
            var rows = await repository.ListForHardDeleteAsync(predicate, cancellationToken);
            if (rows.Count == 0)
            {
                return 0;
            }

            if (!dryRun)
            {
                var logRepository = _unitOfWork.Repository<DataDeletionLog>();
                foreach (var row in rows)
                {
                    await logRepository.AddAsync(new DataDeletionLog
                    {
                        DeletionBatchId = batchId,
                        RootEntityType = rootEntityType,
                        RootEntityId = rootEntityId,
                        TableName = table,
                        RecordId = row.Id,
                        DataJson = JsonSerializer.Serialize(row, SnapshotJsonOptions),
                        DeletedByUserId = deletedByUserId,
                    }, cancellationToken);
                }

                await repository.ExecuteHardDeleteAsync(predicate, cancellationToken);
            }

            return rows.Count;
        }
    }
}
