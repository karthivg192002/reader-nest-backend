using System.Security.Cryptography;
using iucs.readernest.application.Common.Exceptions;
using iucs.readernest.application.Common.Interfaces;
using iucs.readernest.application.Dto.Billing;
using iucs.readernest.application.Mappings;
using iucs.readernest.domain.Entities.Academics;
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

        public async Task<PaymentAccountDto> UpdatePaymentAccountAsync(
            Guid id,
            UpdatePaymentAccountRequest request,
            CancellationToken cancellationToken = default)
        {
            var account = await _unitOfWork.Repository<PaymentAccount>().GetByIdAsync(id, cancellationToken)
                ?? throw new NotFoundException(nameof(PaymentAccount), id);

            account.Name = request.Name.Trim();
            account.GatewayProvider = request.GatewayProvider.Trim();
            account.GatewayAccountRef = request.GatewayAccountRef.Trim();
            account.IsActive = request.IsActive;
            _unitOfWork.Repository<PaymentAccount>().Update(account);

            await _auditLog.StageAsync(AuditAction.Update, nameof(PaymentAccount), account.Id.ToString(),
                $"Gateway wiring set to {account.GatewayProvider}/{account.GatewayAccountRef}", cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return new PaymentAccountDto
            {
                Id = account.Id,
                Name = account.Name,
                Department = account.Department,
                GatewayProvider = account.GatewayProvider,
                GatewayAccountRef = account.GatewayAccountRef,
                IsActive = account.IsActive,
            };
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
            IQueryable<Invoice> query = _unitOfWork.Repository<Invoice>().Query()
                .Include(i => i.Child)
                .Include(i => i.Subscription).ThenInclude(s => s!.PackagePlan).ThenInclude(p => p.Course);
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

            // Invoices are emailed to the parent automatically the moment they're issued
            // (covers both manual invoices and the recurring-billing background service).
            var parentUser = await _unitOfWork.Repository<ParentProfile>().Query()
                .Where(p => p.Id == invoice.ParentProfileId)
                .Select(p => p.User)
                .FirstOrDefaultAsync(cancellationToken);
            if (parentUser is not null)
            {
                var body =
                    $"INVOICE — The Reader Nest\n" +
                    $"Invoice no: {invoice.InvoiceNumber}\n" +
                    $"Billed to:  {parentUser.FirstName} {parentUser.LastName}\n" +
                    $"Department: {invoice.Department}\n" +
                    $"Amount due: {invoice.Amount:0.00} {invoice.Currency}\n" +
                    $"Due date:   {invoice.DueDate:yyyy-MM-dd}\n\n" +
                    "You can pay securely from the parent portal (Payments & Billing → Pay Now) " +
                    "or download this invoice there. Please ignore this email if you have already paid.";
                await NotifyUserAsync(
                    parentUser,
                    NotificationType.PaymentReminder,
                    $"Invoice {invoice.InvoiceNumber} — {invoice.Amount:0.00} {invoice.Currency} due {invoice.DueDate:dd MMM yyyy}",
                    body,
                    cancellationToken);
                await _unitOfWork.SaveChangesAsync(cancellationToken); // persist the notification log row
            }

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

            // A cash recording settles the parent's pending cash intent when one exists,
            // so the intent doesn't linger Pending next to a duplicate Success row.
            var pendingIntent = request.Method == PaymentMethod.Cash
                ? await _unitOfWork.Repository<PaymentTransaction>()
                    .FirstOrDefaultAsync(
                        t => t.InvoiceId == invoice.Id
                            && t.Method == PaymentMethod.Cash
                            && t.Status == TransactionStatus.Pending,
                        cancellationToken)
                : null;

            if (pendingIntent is not null)
            {
                pendingIntent.Amount = request.Amount;
                pendingIntent.Status = TransactionStatus.Success;
                pendingIntent.PaidAtUtc = DateTime.UtcNow;
                pendingIntent.ReceiptNumber = GenerateNumber("RCP");
                _unitOfWork.Repository<PaymentTransaction>().Update(pendingIntent);
            }
            else
            {
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
            }

            await ApplyPaymentToInvoiceAsync(invoice, request.Amount, cancellationToken);

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

        /// <summary>Shared by manual recording and webhook settlement: balance, status and suspension auto-lift.</summary>
        private async Task ApplyPaymentToInvoiceAsync(Invoice invoice, decimal amount, CancellationToken cancellationToken)
        {
            invoice.AmountPaid += amount;
            if (invoice.AmountPaid >= invoice.Amount)
            {
                invoice.Status = InvoiceStatus.Paid;
                invoice.PaidAtUtc = DateTime.UtcNow;

                // Access restoration: full payment auto-lifts any active fee suspension.
                // Load each tracked (Query() is AsNoTracking, so mutations there never persist).
                var suspensionIds = await _unitOfWork.Repository<FeeSuspension>().Query()
                    .Where(s => s.ParentProfileId == invoice.ParentProfileId && s.Status == SuspensionStatus.Active)
                    .Select(s => s.Id)
                    .ToListAsync(cancellationToken);
                foreach (var suspensionId in suspensionIds)
                {
                    var suspension = await _unitOfWork.Repository<FeeSuspension>().GetByIdAsync(suspensionId, cancellationToken);
                    if (suspension is null)
                    {
                        continue;
                    }

                    suspension.Status = SuspensionStatus.Lifted;
                    suspension.LiftedAtUtc = DateTime.UtcNow;
                    suspension.AutoRestored = true;
                    _unitOfWork.Repository<FeeSuspension>().Update(suspension);
                }

                // Close any other still-pending cash intents on this invoice: the money
                // arrived through another payment, so there is nothing left to collect and
                // they must not linger in the staff confirmation queue.
                var staleIntentIds = await _unitOfWork.Repository<PaymentTransaction>().Query()
                    .Where(t => t.InvoiceId == invoice.Id
                        && t.Method == PaymentMethod.Cash
                        && t.Status == TransactionStatus.Pending)
                    .Select(t => t.Id)
                    .ToListAsync(cancellationToken);
                foreach (var intentId in staleIntentIds)
                {
                    var intent = await _unitOfWork.Repository<PaymentTransaction>().GetByIdAsync(intentId, cancellationToken);
                    if (intent is null)
                    {
                        continue;
                    }

                    intent.Status = TransactionStatus.Failed;
                    intent.FailureReason = "Invoice was settled by another payment before this cash intent was collected.";
                    _unitOfWork.Repository<PaymentTransaction>().Update(intent);
                }
            }
            else
            {
                invoice.Status = InvoiceStatus.PartiallyPaid;
            }
        }

        public async Task<(InvoiceDto Invoice, string ParentName)> GetParentInvoiceAsync(
            Guid parentUserId,
            Guid invoiceId,
            CancellationToken cancellationToken = default)
        {
            var parent = await _unitOfWork.Repository<ParentProfile>().Query()
                .Include(p => p.User)
                .FirstOrDefaultAsync(p => p.UserId == parentUserId, cancellationToken)
                ?? throw new NotFoundException("No parent profile is linked to the current account.");

            var invoice = await _unitOfWork.Repository<Invoice>().Query()
                .Include(i => i.Child)
                .Include(i => i.Subscription).ThenInclude(s => s!.PackagePlan).ThenInclude(p => p.Course)
                .FirstOrDefaultAsync(i => i.Id == invoiceId && i.ParentProfileId == parent.Id, cancellationToken)
                ?? throw new NotFoundException(nameof(Invoice), invoiceId);

            return (invoice.ToDto(), $"{parent.User.FirstName} {parent.User.LastName}");
        }

        public async Task<ParentPaymentResultDto> InitiateParentPaymentAsync(
            Guid parentUserId,
            Guid invoiceId,
            InitiateParentPaymentRequest request,
            CancellationToken cancellationToken = default)
        {
            var parent = await _unitOfWork.Repository<ParentProfile>()
                .FirstOrDefaultAsync(p => p.UserId == parentUserId, cancellationToken)
                ?? throw new NotFoundException("No parent profile is linked to the current account.");

            // Ownership: a parent can only pay their own invoice
            var invoice = await _unitOfWork.Repository<Invoice>()
                .FirstOrDefaultAsync(i => i.Id == invoiceId && i.ParentProfileId == parent.Id, cancellationToken)
                ?? throw new NotFoundException(nameof(Invoice), invoiceId);

            if (invoice.Status is InvoiceStatus.Paid or InvoiceStatus.Cancelled)
            {
                throw new DomainValidationException($"Invoice '{invoice.InvoiceNumber}' is already {invoice.Status}.");
            }

            var remaining = invoice.Amount - invoice.AmountPaid;
            var methodKey = request.MethodKey.Trim().ToLowerInvariant();

            if (methodKey == "cash")
            {
                var reference = $"CASH-{Guid.NewGuid():N}";
                await _unitOfWork.Repository<PaymentTransaction>().AddAsync(
                    new PaymentTransaction
                    {
                        InvoiceId = invoice.Id,
                        PaymentAccountId = invoice.PaymentAccountId,
                        Amount = remaining,
                        Currency = invoice.Currency,
                        Status = TransactionStatus.Pending,
                        GatewayTransactionId = reference,
                        Method = PaymentMethod.Cash,
                    },
                    cancellationToken);

                await _auditLog.StageAsync(AuditAction.Payment, nameof(Invoice), invoice.Id.ToString(),
                    changesJson: $"{{\"cashIntent\":\"{reference}\"}}", cancellationToken: cancellationToken);
                await _unitOfWork.SaveChangesAsync(cancellationToken);

                // Admission Team owns payment follow-up day-to-day; Admin stays in the loop too.
                await NotifyBillingStaffAsync(
                    NotificationType.PaymentReceived,
                    "Cash payment intent",
                    $"A parent chose to pay {remaining:0.00} {invoice.Currency} in cash for invoice {invoice.InvoiceNumber}. Record the payment once collected.",
                    cancellationToken);

                return new ParentPaymentResultDto
                {
                    Mode = "cash",
                    GatewayReference = reference,
                    Message = $"Cash payment of {remaining:0.00} {invoice.Currency} noted. Please pay at the centre; your invoice updates once the team confirms receipt.",
                };
            }

            // Gateway path: checkout link + a pending transaction the webhook settles.
            // The parent's chosen method key routes to that gateway regardless of the
            // department account's provider default.
            var account = await _unitOfWork.Repository<PaymentAccount>().GetByIdAsync(invoice.PaymentAccountId, cancellationToken)
                ?? throw new NotFoundException(nameof(PaymentAccount), invoice.PaymentAccountId);

            var link = await _paymentGateway.CreatePaymentLinkAsync(invoice, account, methodKey, cancellationToken);

            // Chosen gateway can't start a checkout (turned off / missing keys) → surface the
            // actionable reason; no pending transaction is created.
            if (link.UnavailableReason is not null)
            {
                return new ParentPaymentResultDto
                {
                    Mode = "unavailable",
                    Message = link.UnavailableReason,
                };
            }

            await _unitOfWork.Repository<PaymentTransaction>().AddAsync(
                new PaymentTransaction
                {
                    InvoiceId = invoice.Id,
                    PaymentAccountId = invoice.PaymentAccountId,
                    Amount = remaining,
                    Currency = invoice.Currency,
                    Status = TransactionStatus.Pending,
                    GatewayTransactionId = link.GatewayReference,
                },
                cancellationToken);

            await _auditLog.StageAsync(AuditAction.Payment, nameof(Invoice), invoice.Id.ToString(),
                changesJson: $"{{\"checkoutRef\":\"{link.GatewayReference}\"}}", cancellationToken: cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return new ParentPaymentResultDto
            {
                Mode = "redirect",
                Url = link.Url,
                GatewayReference = link.GatewayReference,
                Message = "Complete the payment in the secure checkout page. Your invoice updates automatically once the gateway confirms.",
            };
        }

        public async Task<InlineCheckoutDto> StartParentInlineCheckoutAsync(
            Guid parentUserId,
            Guid invoiceId,
            InitiateParentPaymentRequest request,
            CancellationToken cancellationToken = default)
        {
            var parent = await _unitOfWork.Repository<ParentProfile>().Query()
                .Include(p => p.User)
                .FirstOrDefaultAsync(p => p.UserId == parentUserId, cancellationToken)
                ?? throw new NotFoundException("No parent profile is linked to the current account.");

            // Ownership: a parent can only pay their own invoice
            var invoice = await _unitOfWork.Repository<Invoice>()
                .FirstOrDefaultAsync(i => i.Id == invoiceId && i.ParentProfileId == parent.Id, cancellationToken)
                ?? throw new NotFoundException(nameof(Invoice), invoiceId);

            if (invoice.Status is InvoiceStatus.Paid or InvoiceStatus.Cancelled)
            {
                throw new DomainValidationException($"Invoice '{invoice.InvoiceNumber}' is already {invoice.Status}.");
            }

            var account = await _unitOfWork.Repository<PaymentAccount>().GetByIdAsync(invoice.PaymentAccountId, cancellationToken)
                ?? throw new NotFoundException(nameof(PaymentAccount), invoice.PaymentAccountId);

            var checkout = await _paymentGateway.CreateInlineCheckoutAsync(
                invoice,
                account,
                request.MethodKey.Trim().ToLowerInvariant(),
                new InlinePayerInfo
                {
                    Name = $"{parent.User.FirstName} {parent.User.LastName}".Trim(),
                    Email = parent.User.Email,
                    Contact = parent.User.Phone,
                },
                cancellationToken);

            if (checkout.UnavailableReason is not null)
            {
                return new InlineCheckoutDto { Mode = "unavailable", Message = checkout.UnavailableReason };
            }

            await _unitOfWork.Repository<PaymentTransaction>().AddAsync(
                new PaymentTransaction
                {
                    InvoiceId = invoice.Id,
                    PaymentAccountId = invoice.PaymentAccountId,
                    Amount = invoice.Amount - invoice.AmountPaid,
                    Currency = invoice.Currency,
                    Status = TransactionStatus.Pending,
                    GatewayTransactionId = checkout.OrderId,
                },
                cancellationToken);

            await _auditLog.StageAsync(AuditAction.Payment, nameof(Invoice), invoice.Id.ToString(),
                changesJson: $"{{\"inlineCheckoutRef\":\"{checkout.OrderId}\"}}", cancellationToken: cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return new InlineCheckoutDto
            {
                Mode = "inline",
                KeyId = checkout.KeyId,
                OrderId = checkout.OrderId,
                Amount = checkout.AmountMinor,
                Currency = checkout.Currency,
                DisplayName = "The Reader Nest",
                Description = checkout.Description,
                PrefillName = checkout.PrefillName,
                PrefillEmail = checkout.PrefillEmail,
                PrefillContact = checkout.PrefillContact,
            };
        }

        public async Task<InvoiceDto> VerifyParentInlineCheckoutAsync(
            Guid parentUserId,
            Guid invoiceId,
            VerifyInlineCheckoutRequest request,
            CancellationToken cancellationToken = default)
        {
            var parent = await _unitOfWork.Repository<ParentProfile>()
                .FirstOrDefaultAsync(p => p.UserId == parentUserId, cancellationToken)
                ?? throw new NotFoundException("No parent profile is linked to the current account.");

            var invoice = await _unitOfWork.Repository<Invoice>()
                .FirstOrDefaultAsync(i => i.Id == invoiceId && i.ParentProfileId == parent.Id, cancellationToken)
                ?? throw new NotFoundException(nameof(Invoice), invoiceId);

            // The order must be one of THIS invoice's pending checkouts — a valid signature
            // for some other invoice's order must not settle this one.
            var belongs = await _unitOfWork.Repository<PaymentTransaction>().ExistsAsync(
                t => t.InvoiceId == invoice.Id && t.GatewayTransactionId == request.OrderId, cancellationToken);
            if (!belongs)
            {
                throw new NotFoundException($"No pending checkout matches order '{request.OrderId}' on this invoice.");
            }

            var verified = await _paymentGateway.VerifyInlineCheckoutAsync(
                request.OrderId, request.PaymentId, request.Signature, cancellationToken);
            if (!verified)
            {
                throw new DomainValidationException(
                    "The payment could not be verified. If you were charged, it will reconcile automatically within the hour — or use 'I've paid — verify now'.");
            }

            await SettleGatewayTransactionAsync(request.OrderId, succeeded: true, request.PaymentId, null, cancellationToken);

            var refreshed = await _unitOfWork.Repository<Invoice>().Query()
                .FirstOrDefaultAsync(i => i.Id == invoice.Id, cancellationToken) ?? invoice;
            return refreshed.ToDto();
        }

        public async Task SettleGatewayTransactionAsync(
            string gatewayReference,
            bool succeeded,
            string? gatewayPaymentId,
            string? failureReason,
            CancellationToken cancellationToken = default)
        {
            // Idempotency: webhooks retry — an already-settled reference is a no-op.
            // A settled row's reference becomes "ref|paymentId", so match the prefix too.
            var prefix = gatewayReference + "|";
            var transaction = await _unitOfWork.Repository<PaymentTransaction>()
                .FirstOrDefaultAsync(
                    t => t.GatewayTransactionId == gatewayReference
                        || (t.GatewayTransactionId != null && t.GatewayTransactionId.StartsWith(prefix)),
                    cancellationToken)
                ?? throw new NotFoundException($"No payment transaction matches gateway reference '{gatewayReference}'.");

            if (transaction.Status != TransactionStatus.Pending)
            {
                return;
            }

            if (!succeeded)
            {
                transaction.Status = TransactionStatus.Failed;
                transaction.FailureReason = failureReason?.Length > 500 ? failureReason[..500] : failureReason;
                _unitOfWork.Repository<PaymentTransaction>().Update(transaction);
                await _unitOfWork.SaveChangesAsync(cancellationToken);
                return;
            }

            transaction.Status = TransactionStatus.Success;
            transaction.PaidAtUtc = DateTime.UtcNow;
            transaction.ReceiptNumber = GenerateNumber("RCP");
            if (!string.IsNullOrWhiteSpace(gatewayPaymentId))
            {
                // Keep the link reference (webhook correlation key) and append the concrete payment id
                transaction.GatewayTransactionId = $"{gatewayReference}|{gatewayPaymentId}";
            }

            _unitOfWork.Repository<PaymentTransaction>().Update(transaction);

            var invoice = await _unitOfWork.Repository<Invoice>().GetByIdAsync(transaction.InvoiceId, cancellationToken)
                ?? throw new NotFoundException(nameof(Invoice), transaction.InvoiceId);
            await ApplyPaymentToInvoiceAsync(invoice, transaction.Amount, cancellationToken);

            await _auditLog.StageAsync(AuditAction.Payment, nameof(Invoice), invoice.Id.ToString(),
                changesJson: $"{{\"amount\":{transaction.Amount},\"gatewayRef\":\"{gatewayReference}\"}}", cancellationToken: cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            await NotifyAdminsAsync(
                NotificationType.PaymentReceived,
                "Payment received",
                $"Gateway payment of {transaction.Amount:0.00} {invoice.Currency} confirmed for invoice {invoice.InvoiceNumber} ({invoice.Status}).",
                cancellationToken);
        }

        public async Task<IReadOnlyList<CashIntentDto>> ListPendingCashIntentsAsync(CancellationToken cancellationToken = default)
        {
            var intents = await _unitOfWork.Repository<PaymentTransaction>().Query()
                .Where(t => t.Method == PaymentMethod.Cash && t.Status == TransactionStatus.Pending)
                .Include(t => t.Invoice)
                .OrderBy(t => t.CreatedAtUtc)
                .ToListAsync(cancellationToken);

            var parentProfileIds = intents.Select(t => t.Invoice.ParentProfileId).Distinct().ToList();
            var parentNames = await _unitOfWork.Repository<ParentProfile>().Query()
                .Where(p => parentProfileIds.Contains(p.Id))
                .Select(p => new { p.Id, Name = p.User.FirstName + " " + p.User.LastName })
                .ToDictionaryAsync(p => p.Id, p => p.Name, cancellationToken);

            return intents.Select(t => ToCashIntentDto(t, parentNames.GetValueOrDefault(t.Invoice.ParentProfileId, "—"))).ToList();
        }

        public async Task<CashIntentDto> ConfirmCashIntentAsync(
            Guid transactionId,
            ConfirmCashIntentRequest request,
            CancellationToken cancellationToken = default)
        {
            var transaction = await LoadPendingCashIntentAsync(transactionId, cancellationToken);
            var invoice = await _unitOfWork.Repository<Invoice>().GetByIdAsync(transaction.InvoiceId, cancellationToken)
                ?? throw new NotFoundException(nameof(Invoice), transaction.InvoiceId);

            var amount = request.Amount ?? transaction.Amount;
            var remaining = invoice.Amount - invoice.AmountPaid;

            // Stale intent: the invoice was settled by another payment while this intent sat
            // pending (older data predating the auto-close in ApplyPaymentToInvoiceAsync).
            // Close it here so it leaves the confirmation queue instead of erroring forever.
            if (remaining <= 0)
            {
                transaction.Status = TransactionStatus.Failed;
                transaction.FailureReason = "Invoice was already fully paid; cash intent closed without collection.";
                _unitOfWork.Repository<PaymentTransaction>().Update(transaction);
                await _unitOfWork.SaveChangesAsync(cancellationToken);
                throw new DomainValidationException(
                    "This invoice is already fully paid — the cash intent has been closed. Do not collect the cash.");
            }

            if (amount > remaining)
            {
                throw new DomainValidationException($"Confirmed amount {amount} exceeds the outstanding balance of {remaining}.");
            }

            transaction.Amount = amount;
            transaction.Status = TransactionStatus.Success;
            transaction.PaidAtUtc = DateTime.UtcNow;
            transaction.ReceiptNumber = GenerateNumber("RCP");
            _unitOfWork.Repository<PaymentTransaction>().Update(transaction);

            await ApplyPaymentToInvoiceAsync(invoice, amount, cancellationToken);

            await _auditLog.StageAsync(AuditAction.Payment, nameof(Invoice), invoice.Id.ToString(),
                changesJson: $"{{\"cashConfirmed\":{amount},\"reference\":\"{transaction.GatewayTransactionId}\"}}",
                cancellationToken: cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            // Close the loop with the parent: their portal invoice flips as soon as staff confirm.
            var parentUser = await _unitOfWork.Repository<ParentProfile>().Query()
                .Where(p => p.Id == invoice.ParentProfileId)
                .Select(p => p.User)
                .FirstOrDefaultAsync(cancellationToken);
            if (parentUser is not null)
            {
                await NotifyUserAsync(
                    parentUser,
                    NotificationType.PaymentReceived,
                    $"Cash payment received — invoice {invoice.InvoiceNumber}",
                    $"We have received your cash payment of {amount:0.00} {invoice.Currency} for invoice {invoice.InvoiceNumber}. " +
                    $"Receipt no: {transaction.ReceiptNumber}. Thank you.",
                    cancellationToken);
                await _unitOfWork.SaveChangesAsync(cancellationToken);
            }

            var parentName = parentUser is null ? "—" : $"{parentUser.FirstName} {parentUser.LastName}";
            transaction.Invoice = invoice;
            return ToCashIntentDto(transaction, parentName);
        }

        public async Task RejectCashIntentAsync(
            Guid transactionId,
            RejectCashIntentRequest request,
            CancellationToken cancellationToken = default)
        {
            var transaction = await LoadPendingCashIntentAsync(transactionId, cancellationToken);

            transaction.Status = TransactionStatus.Failed;
            transaction.FailureReason = string.IsNullOrWhiteSpace(request.Reason)
                ? "Cash intent rejected by billing staff."
                : request.Reason.Trim();
            _unitOfWork.Repository<PaymentTransaction>().Update(transaction);

            await _auditLog.StageAsync(AuditAction.Payment, nameof(PaymentTransaction), transaction.Id.ToString(),
                changesJson: $"{{\"cashRejected\":\"{transaction.GatewayTransactionId}\"}}", cancellationToken: cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }

        private async Task<PaymentTransaction> LoadPendingCashIntentAsync(Guid transactionId, CancellationToken cancellationToken)
        {
            var transaction = await _unitOfWork.Repository<PaymentTransaction>()
                .FirstOrDefaultAsync(t => t.Id == transactionId, cancellationToken)
                ?? throw new NotFoundException(nameof(PaymentTransaction), transactionId);

            if (transaction.Method != PaymentMethod.Cash)
            {
                throw new DomainValidationException("Only cash payment intents can be confirmed or rejected here.");
            }

            if (transaction.Status != TransactionStatus.Pending)
            {
                throw new DomainValidationException($"This cash intent is already {transaction.Status}.");
            }

            return transaction;
        }

        private static CashIntentDto ToCashIntentDto(PaymentTransaction transaction, string parentName)
        {
            return new CashIntentDto
            {
                TransactionId = transaction.Id,
                InvoiceId = transaction.InvoiceId,
                InvoiceNumber = transaction.Invoice.InvoiceNumber,
                ParentName = parentName,
                Amount = transaction.Amount,
                Currency = transaction.Currency,
                Reference = transaction.GatewayTransactionId ?? "—",
                RequestedAtUtc = transaction.CreatedAtUtc,
            };
        }

        public async Task<InvoiceDto> ReconcileInvoicePaymentAsync(
            Guid parentUserId,
            Guid invoiceId,
            CancellationToken cancellationToken = default)
        {
            var parent = await _unitOfWork.Repository<ParentProfile>()
                .FirstOrDefaultAsync(p => p.UserId == parentUserId, cancellationToken)
                ?? throw new NotFoundException("No parent profile is linked to the current account.");

            var invoice = await _unitOfWork.Repository<Invoice>().Query()
                .FirstOrDefaultAsync(i => i.Id == invoiceId && i.ParentProfileId == parent.Id, cancellationToken)
                ?? throw new NotFoundException(nameof(Invoice), invoiceId);

            // Already closed → nothing to poll; hand back the current state.
            if (invoice.Status is InvoiceStatus.Paid or InvoiceStatus.Cancelled)
            {
                return invoice.ToDto();
            }

            // Pending gateway checkout attempts for this invoice (cash intents are settled by admins).
            var pending = await _unitOfWork.Repository<PaymentTransaction>().Query()
                .Where(t => t.InvoiceId == invoice.Id
                            && t.Status == TransactionStatus.Pending
                            && t.GatewayTransactionId != null
                            && (t.Method == null || t.Method != PaymentMethod.Cash))
                .ToListAsync(cancellationToken);

            foreach (var transaction in pending)
            {
                var status = await _paymentGateway.GetPaymentStatusAsync(transaction.GatewayTransactionId!, cancellationToken);
                switch (status.State)
                {
                    case GatewayPaymentState.Paid:
                        await SettleGatewayTransactionAsync(
                            transaction.GatewayTransactionId!, succeeded: true, status.PaymentId, null, cancellationToken);
                        break;
                    case GatewayPaymentState.Failed:
                        await SettleGatewayTransactionAsync(
                            transaction.GatewayTransactionId!, succeeded: false, null,
                            "The payment link was cancelled or expired.", cancellationToken);
                        break;
                    // Pending / Unknown → leave the transaction as-is.
                }
            }

            // Re-read so the returned status reflects any settlement just applied.
            var refreshed = await _unitOfWork.Repository<Invoice>().Query()
                .FirstOrDefaultAsync(i => i.Id == invoiceId, cancellationToken)
                ?? invoice;
            return refreshed.ToDto();
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
            // Load tracked (Query() is AsNoTracking; mutating that never persists).
            var suspension = await _unitOfWork.Repository<FeeSuspension>().GetByIdAsync(id, cancellationToken)
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

            var saved = await _unitOfWork.Repository<FeeSuspension>().Query()
                .Include(s => s.ParentProfile).ThenInclude(p => p.User)
                .Include(s => s.Invoice)
                .FirstAsync(s => s.Id == id, cancellationToken);
            return ToDto(saved);
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
            // Load tracked (Query() is AsNoTracking; mutating that never persists).
            var refund = await _unitOfWork.Repository<Refund>().GetByIdAsync(id, cancellationToken)
                ?? throw new NotFoundException(nameof(Refund), id);

            if (refund.Status != RefundStatus.Requested)
            {
                throw new DomainValidationException($"This refund is already {refund.Status}.");
            }

            if (request.Approve)
            {
                // Cash has nothing to call a gateway for — the money was handed back at the
                // centre. A gateway-settled transaction gets disbursed for real through the
                // same department account (and gateway) the original payment used.
                var transaction = await _unitOfWork.Repository<PaymentTransaction>()
                    .GetByIdAsync(refund.PaymentTransactionId, cancellationToken)
                    ?? throw new NotFoundException(nameof(PaymentTransaction), refund.PaymentTransactionId);

                if (transaction.Method != PaymentMethod.Cash)
                {
                    var account = await _unitOfWork.Repository<PaymentAccount>()
                        .GetByIdAsync(transaction.PaymentAccountId, cancellationToken)
                        ?? throw new NotFoundException(nameof(PaymentAccount), transaction.PaymentAccountId);
                    var result = await _paymentGateway.RefundAsync(transaction, account, refund.Amount, cancellationToken);
                    refund.GatewayRefundId = result.GatewayRefundId;
                }

                refund.Status = RefundStatus.Processed;
                refund.ProcessedAtUtc = DateTime.UtcNow;
            }
            else
            {
                refund.Status = RefundStatus.Rejected;
            }

            await _auditLog.StageAsync(AuditAction.Update, nameof(Refund), refund.Id.ToString(), cancellationToken: cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            var saved = await _unitOfWork.Repository<Refund>().Query()
                .Include(r => r.PaymentTransaction).ThenInclude(t => t.Invoice)
                .FirstAsync(r => r.Id == id, cancellationToken);
            return ToDto(saved);
        }

        private async Task NotifyUserAsync(
            User user,
            NotificationType type,
            string subject,
            string body,
            CancellationToken cancellationToken)
        {
            await _notificationService.SendEmailAsync(user.Id, user.Email, type, subject, body, cancellationToken);
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

        /// <summary>Admin + Admission Team — the two roles that actually own cash confirmation.</summary>
        private async Task NotifyBillingStaffAsync(
            NotificationType type,
            string subject,
            string body,
            CancellationToken cancellationToken)
        {
            var staff = await _unitOfWork.Repository<User>().Query()
                .Where(u => (u.Role == UserRole.Admin || u.Role == UserRole.AdmissionTeam) && u.Status == UserStatus.Active)
                .ToListAsync(cancellationToken);
            foreach (var user in staff)
            {
                await _notificationService.SendEmailAsync(user.Id, user.Email, type, subject, body, cancellationToken);
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
                GatewayRefundId = refund.GatewayRefundId,
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

            var link = await _paymentGateway.CreatePaymentLinkAsync(invoice, account, cancellationToken: cancellationToken);

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

            var startUtc = request.StartDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            var startsNow = request.StartDate <= today;

            var subscription = new Subscription
            {
                ParentProfileId = request.ParentProfileId,
                ChildId = request.ChildId,
                PackagePlanId = request.PackagePlanId,
                StartDate = request.StartDate,
                // Started subscriptions get their first invoice below, so the pointer moves
                // one cycle out; a future start leaves the first invoice to the billing job
                // on the start date itself.
                NextBillingAtUtc = startsNow ? NextBillingFrom(startUtc, plan.BillingCycle) : startUtc,
            };
            await _unitOfWork.Repository<Subscription>().AddAsync(subscription, cancellationToken);
            await _auditLog.StageAsync(AuditAction.Create, nameof(Subscription), subscription.Id.ToString(), cancellationToken: cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            // First invoice is issued immediately — the parent has something to pay the
            // moment the subscription starts (the hourly job only handles later cycles),
            // and one-time plans (which have no next-billing pointer) bill here or never.
            if (startsNow)
            {
                await CreateInvoiceAsync(
                    new CreateInvoiceRequest
                    {
                        ParentProfileId = request.ParentProfileId,
                        ChildId = request.ChildId,
                        SubscriptionId = subscription.Id,
                        Department = await DepartmentForPlanAsync(plan, cancellationToken),
                        Amount = plan.Price,
                        DueDate = today.AddDays(7),
                    },
                    cancellationToken);
            }

            return ToDto(await SubscriptionQuery().FirstAsync(s => s.Id == subscription.Id, cancellationToken));
        }

        public async Task<SubscriptionDto> RenewSubscriptionAsync(Guid id, CancellationToken cancellationToken = default)
        {
            // Load tracked (Query() is AsNoTracking; mutating that never persists).
            var subscription = await _unitOfWork.Repository<Subscription>().GetByIdAsync(id, cancellationToken)
                ?? throw new NotFoundException(nameof(Subscription), id);

            if (subscription.Status == SubscriptionStatus.Active)
            {
                throw new DomainValidationException("This subscription is already active; nothing to renew.");
            }

            var plan = await _unitOfWork.Repository<PackagePlan>().GetByIdAsync(subscription.PackagePlanId, cancellationToken)
                ?? throw new NotFoundException(nameof(PackagePlan), subscription.PackagePlanId);

            subscription.Status = SubscriptionStatus.Active;
            subscription.CancelledAtUtc = null;
            subscription.NextBillingAtUtc = NextBillingFrom(DateTime.UtcNow, plan.BillingCycle);
            _unitOfWork.Repository<Subscription>().Update(subscription);

            // Renewal conversion is tracked in the audit trail for the renewal-rate report
            await _auditLog.StageAsync(AuditAction.Update, nameof(Subscription), subscription.Id.ToString(),
                changesJson: "{\"renewed\":true}", cancellationToken: cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            // Renewal bills right away, same as a fresh start — the next cycle is the job's.
            await CreateInvoiceAsync(
                new CreateInvoiceRequest
                {
                    ParentProfileId = subscription.ParentProfileId,
                    ChildId = subscription.ChildId,
                    SubscriptionId = subscription.Id,
                    Department = await DepartmentForPlanAsync(plan, cancellationToken),
                    Amount = plan.Price,
                    DueDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(7),
                },
                cancellationToken);

            return ToDto(await SubscriptionQuery().FirstAsync(s => s.Id == id, cancellationToken));
        }

        public async Task<SubscriptionDto> CancelSubscriptionAsync(Guid id, CancellationToken cancellationToken = default)
        {
            // Load tracked (Query() is AsNoTracking; mutating that never persists).
            var subscription = await _unitOfWork.Repository<Subscription>().GetByIdAsync(id, cancellationToken)
                ?? throw new NotFoundException(nameof(Subscription), id);

            if (subscription.Status == SubscriptionStatus.Cancelled)
            {
                throw new DomainValidationException("This subscription is already cancelled.");
            }

            subscription.Status = SubscriptionStatus.Cancelled;
            subscription.CancelledAtUtc = DateTime.UtcNow;
            subscription.NextBillingAtUtc = null;
            _unitOfWork.Repository<Subscription>().Update(subscription);

            await _auditLog.StageAsync(AuditAction.Update, nameof(Subscription), subscription.Id.ToString(), cancellationToken: cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return ToDto(await SubscriptionQuery().FirstAsync(s => s.Id == id, cancellationToken));
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

        /// <summary>Invoices route to the plan's course department; plans without a course default to Phonics.</summary>
        private async Task<Department> DepartmentForPlanAsync(PackagePlan plan, CancellationToken cancellationToken)
        {
            if (plan.CourseId is null)
            {
                return Department.Phonics;
            }

            var course = await _unitOfWork.Repository<Course>().GetByIdAsync(plan.CourseId.Value, cancellationToken);
            return course?.Department ?? Department.Phonics;
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
