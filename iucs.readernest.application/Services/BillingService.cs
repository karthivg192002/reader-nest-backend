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

        public BillingService(IUnitOfWork unitOfWork, IAuditLogService auditLog, IPaymentGateway paymentGateway)
        {
            _unitOfWork = unitOfWork;
            _auditLog = auditLog;
            _paymentGateway = paymentGateway;
        }

        public async Task<IReadOnlyList<PackagePlanDto>> ListPlansAsync(CancellationToken cancellationToken = default)
        {
            var plans = await _unitOfWork.Repository<PackagePlan>().Query()
                .OrderBy(p => p.Name)
                .ToListAsync(cancellationToken);

            return plans.Select(p => p.ToDto()).ToList();
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
            var parentExists = await _unitOfWork.Repository<ParentProfile>()
                .ExistsAsync(p => p.Id == request.ParentProfileId, cancellationToken);
            if (!parentExists)
            {
                throw new NotFoundException(nameof(ParentProfile), request.ParentProfileId);
            }

            // Dual-gateway requirement: every invoice routes through its department's account
            var account = await _unitOfWork.Repository<PaymentAccount>()
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
            }
            else
            {
                invoice.Status = InvoiceStatus.PartiallyPaid;
            }

            await _auditLog.StageAsync(AuditAction.Payment, nameof(Invoice), invoice.Id.ToString(),
                changesJson: $"{{\"amount\":{request.Amount}}}", cancellationToken: cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return invoice.ToDto();
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

        private static string GenerateNumber(string prefix)
        {
            // Date-scoped random suffix; the unique index guards the rare collision
            var suffix = Convert.ToHexString(RandomNumberGenerator.GetBytes(4));
            return $"{prefix}-{DateTime.UtcNow:yyyyMMdd}-{suffix}";
        }
    }
}
