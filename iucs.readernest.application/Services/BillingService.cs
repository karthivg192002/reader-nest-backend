using System.Security.Cryptography;
using iucs.readernest.application.Common.Exceptions;
using iucs.readernest.application.Common.Interfaces;
using iucs.readernest.application.Dto.Billing;
using iucs.readernest.application.Mappings;
using iucs.readernest.domain.Entities.Billing;
using iucs.readernest.domain.Entities.Users;
using iucs.readernest.domain.Enums;
using iucs.readernest.domain.Repository;
using Microsoft.EntityFrameworkCore;

namespace iucs.readernest.application.Services
{
    public class BillingService : IBillingService
    {
        private readonly IUnitOfWork _unitOfWork;
        private readonly IAuditLogService _auditLog;
        private readonly IPaymentGateway _paymentGateway;
        private readonly INotificationService _notificationService;

        public BillingService(
            IUnitOfWork unitOfWork,
            IAuditLogService auditLog,
            IPaymentGateway paymentGateway,
            INotificationService notificationService)
        {
            _unitOfWork = unitOfWork;
            _auditLog = auditLog;
            _paymentGateway = paymentGateway;
            _notificationService = notificationService;
        }

        public async Task<IReadOnlyList<PackagePlanDto>> ListPlansAsync(CancellationToken cancellationToken = default)
        {
            var plans = await _unitOfWork.Repository<PackagePlan>().Query()
                .OrderBy(p => p.Name)
                .ToListAsync(cancellationToken);

            return plans.Select(p => p.ToDto()).ToList();
        }

        public async Task<IReadOnlyList<PaymentAccountDto>> ListPaymentAccountsAsync(CancellationToken cancellationToken = default)
        {
            var accounts = await _unitOfWork.Repository<PaymentAccount>().Query()
                .OrderBy(a => a.Department)
                .ToListAsync(cancellationToken);

            var result = new List<PaymentAccountDto>(accounts.Count);
            foreach (var account in accounts)
            {
                var transactions = await _unitOfWork.Repository<PaymentTransaction>().Query()
                    .Where(t => t.PaymentAccountId == account.Id)
                    .Include(t => t.Invoice).ThenInclude(i => i.Child)
                    .OrderByDescending(t => t.CreatedAtUtc)
                    .ToListAsync(cancellationToken);

                result.Add(new PaymentAccountDto
                {
                    Id = account.Id,
                    Name = account.Name,
                    Department = account.Department,
                    GatewayProvider = account.GatewayProvider,
                    GatewayAccountRef = account.GatewayAccountRef,
                    IsActive = account.IsActive,
                    TransactionCount = transactions.Count(t => t.Status == TransactionStatus.Success),
                    TotalCollected = transactions
                        .Where(t => t.Status == TransactionStatus.Success)
                        .Sum(t => t.Amount),
                    RecentTransactions = transactions.Take(5).Select(t => new PaymentAccountTransactionDto
                    {
                        Id = t.Id,
                        InvoiceNumber = t.Invoice.InvoiceNumber,
                        StudentName = t.Invoice.Child is { } c ? $"{c.FirstName} {c.LastName}".Trim() : null,
                        Amount = t.Amount,
                        Status = t.Status,
                        DateUtc = t.PaidAtUtc ?? t.CreatedAtUtc,
                    }).ToList(),
                });
            }

            return result;
        }

        public async Task SetParentPaymentAccountAsync(
            SavePaymentMappingRequest request,
            CancellationToken cancellationToken = default)
        {
            var parent = await _unitOfWork.Repository<ParentProfile>()
                .FirstOrDefaultAsync(p => p.UserId == request.ParentUserId, cancellationToken)
                ?? throw new NotFoundException("No parent profile is linked to that account.");

            var accountExists = await _unitOfWork.Repository<PaymentAccount>()
                .ExistsAsync(a => a.Id == request.PaymentAccountId, cancellationToken);
            if (!accountExists)
            {
                throw new NotFoundException(nameof(PaymentAccount), request.PaymentAccountId);
            }

            parent.PaymentAccountId = request.PaymentAccountId;
            _unitOfWork.Repository<ParentProfile>().Update(parent);

            await _auditLog.StageAsync(AuditAction.Update, nameof(ParentProfile), parent.Id.ToString(),
                $"Mapped to payment account {request.PaymentAccountId}", cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }

        public async Task<PackagePlanDto> CreatePlanAsync(
            SavePackagePlanRequest request,
            CancellationToken cancellationToken = default)
        {
            var plan = new PackagePlan
            {
                Name = request.Name.Trim(),
                CourseId = request.CourseId,
                BillingType = request.BillingType,
                BillingCycle = request.BillingCycle,
                Price = request.Price,
                SessionsIncluded = request.SessionsIncluded,
                IsActive = request.IsActive,
            };
            await _unitOfWork.Repository<PackagePlan>().AddAsync(plan, cancellationToken);
            await _auditLog.StageAsync(AuditAction.Create, nameof(PackagePlan), plan.Id.ToString(), cancellationToken: cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return plan.ToDto();
        }

        public async Task<PackagePlanDto> UpdatePlanAsync(
            Guid id,
            SavePackagePlanRequest request,
            CancellationToken cancellationToken = default)
        {
            var plan = await _unitOfWork.Repository<PackagePlan>().GetByIdAsync(id, cancellationToken)
                ?? throw new NotFoundException(nameof(PackagePlan), id);

            plan.Name = request.Name.Trim();
            plan.CourseId = request.CourseId;
            plan.BillingType = request.BillingType;
            plan.BillingCycle = request.BillingCycle;
            plan.Price = request.Price;
            plan.SessionsIncluded = request.SessionsIncluded;
            plan.IsActive = request.IsActive;

            await _auditLog.StageAsync(AuditAction.Update, nameof(PackagePlan), plan.Id.ToString(), cancellationToken: cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return plan.ToDto();
        }

        public async Task<IReadOnlyList<InvoiceDto>> ListInvoicesAsync(
            InvoiceStatus? status,
            Guid? parentProfileId,
            CancellationToken cancellationToken = default)
        {
            var query = _unitOfWork.Repository<Invoice>().Query();
            if (status.HasValue)
            {
                query = query.Where(i => i.Status == status.Value);
            }

            if (parentProfileId.HasValue)
            {
                query = query.Where(i => i.ParentProfileId == parentProfileId.Value);
            }

            var invoices = await query.OrderByDescending(i => i.IssuedAtUtc).ToListAsync(cancellationToken);
            return invoices.Select(i => i.ToDto()).ToList();
        }

        public async Task<InvoiceDto> CreateInvoiceAsync(
            CreateInvoiceRequest request,
            CancellationToken cancellationToken = default)
        {
            var parent = await _unitOfWork.Repository<ParentProfile>()
                .FirstOrDefaultAsync(p => p.Id == request.ParentProfileId, cancellationToken)
                ?? throw new NotFoundException(nameof(ParentProfile), request.ParentProfileId);

            // Admin override (Payment Gateway Mapping): if this parent is pinned to a
            // specific account, route through it; otherwise fall back to the invoice's
            // department account (the default dual-gateway behaviour).
            PaymentAccount? account = null;
            if (parent.PaymentAccountId.HasValue)
            {
                account = await _unitOfWork.Repository<PaymentAccount>()
                    .FirstOrDefaultAsync(a => a.Id == parent.PaymentAccountId.Value && a.IsActive, cancellationToken);
            }

            account ??= await _unitOfWork.Repository<PaymentAccount>()
                .FirstOrDefaultAsync(a => a.Department == request.Department && a.IsActive, cancellationToken)
                ?? throw new NotFoundException($"No active payment account is configured for the {request.Department} department.");

            var invoice = new Invoice
            {
                InvoiceNumber = GenerateNumber("INV"),
                ParentProfileId = request.ParentProfileId,
                ChildId = request.ChildId,
                SubscriptionId = request.SubscriptionId,
                PaymentAccountId = account.Id,
                Department = request.Department,
                Amount = request.Amount,
                DueDate = request.DueDate,
                IssuedAtUtc = DateTime.UtcNow,
            };
            await _unitOfWork.Repository<Invoice>().AddAsync(invoice, cancellationToken);
            await _auditLog.StageAsync(AuditAction.Create, nameof(Invoice), invoice.Id.ToString(), cancellationToken: cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return invoice.ToDto();
        }

        public async Task<InvoiceDto> RecordPaymentAsync(
            Guid invoiceId,
            RecordPaymentRequest request,
            CancellationToken cancellationToken = default)
        {
            var invoice = await _unitOfWork.Repository<Invoice>().GetByIdAsync(invoiceId, cancellationToken)
                ?? throw new NotFoundException(nameof(Invoice), invoiceId);

            if (invoice.Status is InvoiceStatus.Paid or InvoiceStatus.Cancelled)
            {
                throw new DomainValidationException($"Invoice '{invoice.InvoiceNumber}' is already {invoice.Status}.");
            }

            var remaining = invoice.Amount - invoice.AmountPaid;
            if (request.Amount > remaining)
            {
                throw new DomainValidationException($"Payment of {request.Amount} exceeds the outstanding balance of {remaining}.");
            }

            await _unitOfWork.Repository<PaymentTransaction>().AddAsync(
                new PaymentTransaction
                {
                    InvoiceId = invoice.Id,
                    PaymentAccountId = invoice.PaymentAccountId,
                    Amount = request.Amount,
                    Currency = invoice.Currency,
                    Status = TransactionStatus.Success,
                    GatewayTransactionId = request.GatewayTransactionId,
                    Method = request.Method,
                    PaidAtUtc = DateTime.UtcNow,
                    ReceiptNumber = GenerateNumber("RCP"),
                },
                cancellationToken);

            invoice.AmountPaid += request.Amount;
            if (invoice.AmountPaid >= invoice.Amount)
            {
                invoice.Status = InvoiceStatus.Paid;
                invoice.PaidAtUtc = DateTime.UtcNow;

                // Access restoration: full payment auto-lifts any active fee suspension
                var suspensions = await _unitOfWork.Repository<FeeSuspension>().Query()
                    .Where(s => s.ParentProfileId == invoice.ParentProfileId && s.Status == SuspensionStatus.Active)
                    .ToListAsync(cancellationToken);
                foreach (var suspension in suspensions)
                {
                    suspension.Status = SuspensionStatus.Lifted;
                    suspension.LiftedAtUtc = DateTime.UtcNow;
                    suspension.AutoRestored = true;
                }
            }
            else
            {
                invoice.Status = InvoiceStatus.PartiallyPaid;
            }

            await _auditLog.StageAsync(AuditAction.Payment, nameof(Invoice), invoice.Id.ToString(),
                changesJson: $"{{\"amount\":{request.Amount}}}", cancellationToken: cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            await NotifyAdminsAsync(
                NotificationType.PaymentReceived,
                "Payment received",
                $"Payment of {request.Amount:0.00} {invoice.Currency} recorded against invoice {invoice.InvoiceNumber} ({invoice.Status}).",
                cancellationToken);

            return invoice.ToDto();
        }

        public async Task<IReadOnlyList<FeeSuspensionDto>> ListSuspensionsAsync(
            SuspensionStatus? status,
            CancellationToken cancellationToken = default)
        {
            IQueryable<FeeSuspension> query = _unitOfWork.Repository<FeeSuspension>().Query()
                .Include(s => s.ParentProfile).ThenInclude(p => p.User)
                .Include(s => s.Invoice);
            if (status.HasValue)
            {
                query = query.Where(s => s.Status == status.Value);
            }

            var suspensions = await query.OrderByDescending(s => s.SuspendedAtUtc).ToListAsync(cancellationToken);
            return suspensions.Select(ToDto).ToList();
        }

        public async Task<FeeSuspensionDto> LiftSuspensionAsync(Guid id, CancellationToken cancellationToken = default)
        {
            var suspension = await _unitOfWork.Repository<FeeSuspension>().Query()
                .Include(s => s.ParentProfile).ThenInclude(p => p.User)
                .Include(s => s.Invoice)
                .FirstOrDefaultAsync(s => s.Id == id, cancellationToken)
                ?? throw new NotFoundException(nameof(FeeSuspension), id);

            if (suspension.Status != SuspensionStatus.Active)
            {
                throw new DomainValidationException("This suspension has already been lifted.");
            }

            suspension.Status = SuspensionStatus.Lifted;
            suspension.LiftedAtUtc = DateTime.UtcNow;
            suspension.AutoRestored = false;

            await _auditLog.StageAsync(AuditAction.Update, nameof(FeeSuspension), suspension.Id.ToString(), cancellationToken: cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return ToDto(suspension);
        }

        public async Task<IReadOnlyList<RefundDto>> ListRefundsAsync(CancellationToken cancellationToken = default)
        {
            var refunds = await _unitOfWork.Repository<Refund>().Query()
                .Include(r => r.PaymentTransaction).ThenInclude(t => t.Invoice)
                .OrderByDescending(r => r.CreatedAtUtc)
                .ToListAsync(cancellationToken);
            return refunds.Select(ToDto).ToList();
        }

        public async Task<RefundDto> RequestRefundAsync(RequestRefundRequest request, CancellationToken cancellationToken = default)
        {
            var transaction = await _unitOfWork.Repository<PaymentTransaction>().GetByIdAsync(request.PaymentTransactionId, cancellationToken)
                ?? throw new NotFoundException(nameof(PaymentTransaction), request.PaymentTransactionId);

            if (request.Amount > transaction.Amount)
            {
                throw new DomainValidationException($"Refund of {request.Amount} exceeds the transaction amount of {transaction.Amount}.");
            }

            var refund = new Refund
            {
                PaymentTransactionId = transaction.Id,
                Amount = request.Amount,
                Reason = request.Reason.Trim(),
            };
            await _unitOfWork.Repository<Refund>().AddAsync(refund, cancellationToken);
            await _auditLog.StageAsync(AuditAction.Create, nameof(Refund), refund.Id.ToString(), cancellationToken: cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            var saved = await _unitOfWork.Repository<Refund>().Query()
                .Include(r => r.PaymentTransaction).ThenInclude(t => t.Invoice)
                .FirstAsync(r => r.Id == refund.Id, cancellationToken);
            return ToDto(saved);
        }

        public async Task<RefundDto> ReviewRefundAsync(Guid id, ReviewRefundRequest request, CancellationToken cancellationToken = default)
        {
            var refund = await _unitOfWork.Repository<Refund>().Query()
                .Include(r => r.PaymentTransaction).ThenInclude(t => t.Invoice)
                .FirstOrDefaultAsync(r => r.Id == id, cancellationToken)
                ?? throw new NotFoundException(nameof(Refund), id);

            if (refund.Status != RefundStatus.Requested)
            {
                throw new DomainValidationException($"This refund is already {refund.Status}.");
            }

            if (request.Approve)
            {
                // Gateway disbursement follows once real accounts exist; recorded as processed here
                refund.Status = RefundStatus.Processed;
                refund.ProcessedAtUtc = DateTime.UtcNow;
            }
            else
            {
                refund.Status = RefundStatus.Rejected;
            }

            await _auditLog.StageAsync(AuditAction.Update, nameof(Refund), refund.Id.ToString(), cancellationToken: cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return ToDto(refund);
        }

        private async Task NotifyAdminsAsync(
            NotificationType type,
            string subject,
            string body,
            CancellationToken cancellationToken)
        {
            var admins = await _unitOfWork.Repository<User>().Query()
                .Where(u => u.Role == UserRole.Admin && u.Status == UserStatus.Active)
                .ToListAsync(cancellationToken);
            foreach (var admin in admins)
            {
                await _notificationService.SendEmailAsync(admin.Id, admin.Email, type, subject, body, cancellationToken);
            }
        }

        private static FeeSuspensionDto ToDto(FeeSuspension suspension)
        {
            return new FeeSuspensionDto
            {
                Id = suspension.Id,
                ParentProfileId = suspension.ParentProfileId,
                ParentName = $"{suspension.ParentProfile.User.FirstName} {suspension.ParentProfile.User.LastName}",
                InvoiceId = suspension.InvoiceId,
                InvoiceNumber = suspension.Invoice?.InvoiceNumber,
                Reason = suspension.Reason,
                Status = suspension.Status,
                SuspendedAtUtc = suspension.SuspendedAtUtc,
                LiftedAtUtc = suspension.LiftedAtUtc,
                AutoRestored = suspension.AutoRestored,
            };
        }

        private static RefundDto ToDto(Refund refund)
        {
            return new RefundDto
            {
                Id = refund.Id,
                PaymentTransactionId = refund.PaymentTransactionId,
                InvoiceNumber = refund.PaymentTransaction?.Invoice?.InvoiceNumber,
                Amount = refund.Amount,
                Reason = refund.Reason,
                Status = refund.Status,
                ProcessedAtUtc = refund.ProcessedAtUtc,
            };
        }

        public async Task<PaymentLinkDto> CreatePaymentLinkAsync(Guid invoiceId, CancellationToken cancellationToken = default)
        {
            var invoice = await _unitOfWork.Repository<Invoice>().GetByIdAsync(invoiceId, cancellationToken)
                ?? throw new NotFoundException(nameof(Invoice), invoiceId);

            if (invoice.Status is InvoiceStatus.Paid or InvoiceStatus.Cancelled)
            {
                throw new DomainValidationException($"Invoice '{invoice.InvoiceNumber}' is already {invoice.Status}; no payment link is needed.");
            }

            var account = await _unitOfWork.Repository<PaymentAccount>().GetByIdAsync(invoice.PaymentAccountId, cancellationToken)
                ?? throw new NotFoundException(nameof(PaymentAccount), invoice.PaymentAccountId);

            var link = await _paymentGateway.CreatePaymentLinkAsync(invoice, account, cancellationToken);

            await _auditLog.StageAsync(AuditAction.Update, nameof(Invoice), invoice.Id.ToString(),
                changesJson: $"{{\"paymentLinkRef\":\"{link.GatewayReference}\"}}", cancellationToken: cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return new PaymentLinkDto
            {
                InvoiceId = invoice.Id,
                InvoiceNumber = invoice.InvoiceNumber,
                Url = link.Url,
                GatewayReference = link.GatewayReference,
                AmountDue = invoice.Amount - invoice.AmountPaid,
            };
        }

        public async Task<IReadOnlyList<SubscriptionDto>> ListSubscriptionsAsync(
            SubscriptionStatus? status,
            CancellationToken cancellationToken = default)
        {
            IQueryable<Subscription> query = SubscriptionQuery();
            if (status.HasValue)
            {
                query = query.Where(s => s.Status == status.Value);
            }

            var subscriptions = await query.OrderByDescending(s => s.CreatedAtUtc).ToListAsync(cancellationToken);
            return subscriptions.Select(ToDto).ToList();
        }

        public async Task<SubscriptionDto> CreateSubscriptionAsync(
            CreateSubscriptionRequest request,
            CancellationToken cancellationToken = default)
        {
            var plan = await _unitOfWork.Repository<PackagePlan>().GetByIdAsync(request.PackagePlanId, cancellationToken)
                ?? throw new NotFoundException(nameof(PackagePlan), request.PackagePlanId);
            var childBelongs = await _unitOfWork.Repository<Child>().ExistsAsync(
                c => c.Id == request.ChildId && c.ParentProfileId == request.ParentProfileId, cancellationToken);
            if (!childBelongs)
            {
                throw new DomainValidationException("The child does not belong to the given parent profile.");
            }

            var duplicate = await _unitOfWork.Repository<Subscription>().ExistsAsync(
                s => s.ChildId == request.ChildId && s.PackagePlanId == request.PackagePlanId && s.Status == SubscriptionStatus.Active,
                cancellationToken);
            if (duplicate)
            {
                throw new ConflictException("This child already has an active subscription on that plan.");
            }

            var subscription = new Subscription
            {
                ParentProfileId = request.ParentProfileId,
                ChildId = request.ChildId,
                PackagePlanId = request.PackagePlanId,
                StartDate = request.StartDate,
                NextBillingAtUtc = NextBillingFrom(request.StartDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc), plan.BillingCycle),
            };
            await _unitOfWork.Repository<Subscription>().AddAsync(subscription, cancellationToken);
            await _auditLog.StageAsync(AuditAction.Create, nameof(Subscription), subscription.Id.ToString(), cancellationToken: cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return ToDto(await SubscriptionQuery().FirstAsync(s => s.Id == subscription.Id, cancellationToken));
        }

        public async Task<SubscriptionDto> RenewSubscriptionAsync(Guid id, CancellationToken cancellationToken = default)
        {
            var subscription = await SubscriptionQuery().FirstOrDefaultAsync(s => s.Id == id, cancellationToken)
                ?? throw new NotFoundException(nameof(Subscription), id);

            if (subscription.Status == SubscriptionStatus.Active)
            {
                throw new DomainValidationException("This subscription is already active; nothing to renew.");
            }

            subscription.Status = SubscriptionStatus.Active;
            subscription.CancelledAtUtc = null;
            subscription.NextBillingAtUtc = NextBillingFrom(DateTime.UtcNow, subscription.PackagePlan.BillingCycle);

            // Renewal conversion is tracked in the audit trail for the renewal-rate report
            await _auditLog.StageAsync(AuditAction.Update, nameof(Subscription), subscription.Id.ToString(),
                changesJson: "{\"renewed\":true}", cancellationToken: cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return ToDto(subscription);
        }

        public async Task<SubscriptionDto> CancelSubscriptionAsync(Guid id, CancellationToken cancellationToken = default)
        {
            var subscription = await SubscriptionQuery().FirstOrDefaultAsync(s => s.Id == id, cancellationToken)
                ?? throw new NotFoundException(nameof(Subscription), id);

            if (subscription.Status == SubscriptionStatus.Cancelled)
            {
                throw new DomainValidationException("This subscription is already cancelled.");
            }

            subscription.Status = SubscriptionStatus.Cancelled;
            subscription.CancelledAtUtc = DateTime.UtcNow;
            subscription.NextBillingAtUtc = null;

            await _auditLog.StageAsync(AuditAction.Update, nameof(Subscription), subscription.Id.ToString(), cancellationToken: cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return ToDto(subscription);
        }

        private static DateTime? NextBillingFrom(DateTime fromUtc, BillingCycle cycle)
        {
            return cycle switch
            {
                BillingCycle.Monthly => fromUtc.AddMonths(1),
                BillingCycle.Quarterly => fromUtc.AddMonths(3),
                BillingCycle.Yearly => fromUtc.AddYears(1),
                _ => null, // one-time plans bill once at creation
            };
        }

        private IQueryable<Subscription> SubscriptionQuery()
        {
            return _unitOfWork.Repository<Subscription>().Query()
                .Include(s => s.Child)
                .Include(s => s.PackagePlan);
        }

        private static SubscriptionDto ToDto(Subscription subscription)
        {
            return new SubscriptionDto
            {
                Id = subscription.Id,
                ParentProfileId = subscription.ParentProfileId,
                ChildId = subscription.ChildId,
                ChildName = $"{subscription.Child.FirstName} {subscription.Child.LastName}",
                PackagePlanId = subscription.PackagePlanId,
                PlanName = subscription.PackagePlan.Name,
                Status = subscription.Status,
                StartDate = subscription.StartDate,
                NextBillingAtUtc = subscription.NextBillingAtUtc,
                CancelledAtUtc = subscription.CancelledAtUtc,
            };
        }

        private static string GenerateNumber(string prefix)
        {
            // Date-scoped random suffix; the unique index guards the rare collision
            var suffix = Convert.ToHexString(RandomNumberGenerator.GetBytes(4));
            return $"{prefix}-{DateTime.UtcNow:yyyyMMdd}-{suffix}";
        }
    }
}
