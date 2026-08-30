using System.Security.Cryptography;
using iucs.readernest.application.Common;
using iucs.readernest.application.Common.Exceptions;
using iucs.readernest.application.Common.Interfaces;
using iucs.readernest.application.Dto.Billing;
using iucs.readernest.application.Dto.Common;
using iucs.readernest.application.Mappings;
using iucs.readernest.domain.Common;
using iucs.readernest.domain.Entities.Academics;
using iucs.readernest.domain.Entities.Billing;
using iucs.readernest.domain.Entities.Settings;
using iucs.readernest.domain.Entities.Users;
using iucs.readernest.domain.Enums;
using iucs.readernest.domain.Repository;
using Microsoft.EntityFrameworkCore;

namespace iucs.readernest.application.Services
{
    public class BillingService : IBillingService
    {
        /// <summary>
        /// Ceiling on <see cref="ListInvoicesAsync"/>'s page size. The admin billing screen
        /// asks for a whole working set at once (it filters, sorts and totals client-side),
        /// so this is deliberately generous — but it is still a ceiling, so no caller can
        /// turn the endpoint back into an unbounded table scan by asking for pageSize=100000.
        /// </summary>
        private const int MaxInvoicePageSize = 200;

        private readonly IUnitOfWork _unitOfWork;
        private readonly IAuditLogService _auditLog;
        private readonly IPaymentGateway _paymentGateway;
        private readonly INotificationService _notificationService;

        /// <summary>Only for hand-stamping UpdatedBy on writes that bypass the audit interceptor.</summary>
        private readonly ICurrentUserService _currentUser;
        private readonly IBulkFileReader _bulkFileReader;
        private readonly IInvoicePdfGenerator _invoicePdfGenerator;

        public BillingService(
            IUnitOfWork unitOfWork,
            IAuditLogService auditLog,
            IPaymentGateway paymentGateway,
            INotificationService notificationService,
            ICurrentUserService currentUser,
            IBulkFileReader bulkFileReader,
            IInvoicePdfGenerator invoicePdfGenerator)
        {
            _unitOfWork = unitOfWork;
            _auditLog = auditLog;
            _paymentGateway = paymentGateway;
            _notificationService = notificationService;
            _currentUser = currentUser;
            _bulkFileReader = bulkFileReader;
            _invoicePdfGenerator = invoicePdfGenerator;
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
                .Include(a => a.Department)
                .OrderBy(a => a.Department.Name)
                .ToListAsync(cancellationToken);

            // One aggregate query for every account's totals, instead of pulling each
            // account's ENTIRE transaction history into memory just to Count()/Sum() it
            // in C# — this used to be N queries each materializing an unbounded row set.
            var totals = await _unitOfWork.Repository<PaymentTransaction>().Query()
                .Where(t => t.Status == TransactionStatus.Success)
                .GroupBy(t => t.PaymentAccountId)
                .Select(g => new { AccountId = g.Key, Count = g.Count(), Total = g.Sum(t => t.Amount) })
                .ToDictionaryAsync(g => g.AccountId, cancellationToken);

            var result = new List<PaymentAccountDto>(accounts.Count);
            foreach (var account in accounts)
            {
                // Only the 5 most recent transactions need their full Invoice/Child
                // details — bounded at the SQL level (Take(5)) rather than in memory.
                // Status filter matches the totals aggregate above: without it, a failed
                // or pending retry attempt shows up here looking like an extra completed
                // payment for the same invoice (the DTO carries no status the UI surfaces),
                // while TotalCollected/TransactionCount correctly exclude it — a visible
                // mismatch between "recent transactions" and the totals right next to it.
                var recent = await _unitOfWork.Repository<PaymentTransaction>().Query()
                    .Where(t => t.PaymentAccountId == account.Id && t.Status == TransactionStatus.Success)
                    .Include(t => t.Invoice).ThenInclude(i => i.Child)
                    .OrderByDescending(t => t.CreatedAtUtc)
                    .Take(5)
                    .ToListAsync(cancellationToken);

                totals.TryGetValue(account.Id, out var accountTotals);

                result.Add(new PaymentAccountDto
                {
                    Id = account.Id,
                    Name = account.Name,
                    DepartmentId = account.DepartmentId,
                    DepartmentName = account.Department?.Name ?? string.Empty,
                    GatewayProvider = account.GatewayProvider,
                    GatewayAccountRef = account.GatewayAccountRef,
                    IsActive = account.IsActive,
                    TransactionCount = accountTotals?.Count ?? 0,
                    TotalCollected = accountTotals?.Total ?? 0m,
                    RecentTransactions = recent.Select(t => new PaymentAccountTransactionDto
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
            var account = await _unitOfWork.Repository<PaymentAccount>().TrackedQuery()
                .Include(a => a.Department)
                .FirstOrDefaultAsync(a => a.Id == id, cancellationToken)
                ?? throw new NotFoundException(nameof(PaymentAccount), id);

            account.Name = request.Name.Trim();
            var provider = request.GatewayProvider.Trim();
            var accountRef = request.GatewayAccountRef.Trim();
            account.GatewayProvider = provider;
            account.GatewayAccountRef = accountRef;
            account.IsActive = request.IsActive;
            _unitOfWork.Repository<PaymentAccount>().Update(account);

            if (request.ApplyToAllDepartments)
            {
                // Every other department's account converges on this same gateway wiring —
                // Name is deliberately left alone (stays "<Department> Department Account" for
                // the card label), only the actual routing fields sync.
                var others = await _unitOfWork.Repository<PaymentAccount>().TrackedQuery()
                    .Where(a => a.Id != account.Id)
                    .ToListAsync(cancellationToken);
                foreach (var other in others)
                {
                    other.GatewayProvider = provider;
                    other.GatewayAccountRef = accountRef;
                    other.IsActive = request.IsActive;
                    _unitOfWork.Repository<PaymentAccount>().Update(other);
                }

                // A department that predates the auto-create-on-department-creation behaviour
                // has no PaymentAccount row at all, so the loop above never reaches it — it was
                // invisible on this screen and CreateInvoiceAsync throws NotFoundException the
                // first time anyone tries to bill it (discovered live: English and Hindi).
                // "One gateway account for the whole business" should mean literally every
                // department, so backfill the ones with no row yet too.
                var coveredDepartmentIds = await _unitOfWork.Repository<PaymentAccount>().Query()
                    .Select(a => a.DepartmentId)
                    .ToListAsync(cancellationToken);
                var uncoveredDepartments = await _unitOfWork.Repository<Department>().Query()
                    .Where(d => !coveredDepartmentIds.Contains(d.Id))
                    .ToListAsync(cancellationToken);
                foreach (var department in uncoveredDepartments)
                {
                    await _unitOfWork.Repository<PaymentAccount>().AddAsync(
                        new PaymentAccount
                        {
                            Name = $"{department.Name} Department Account",
                            DepartmentId = department.Id,
                            GatewayProvider = provider,
                            GatewayAccountRef = accountRef,
                            IsActive = request.IsActive,
                        },
                        cancellationToken);
                }
            }

            await _auditLog.StageAsync(AuditAction.Update, nameof(PaymentAccount), account.Id.ToString(),
                $"Gateway wiring set to {account.GatewayProvider}/{account.GatewayAccountRef}"
                    + (request.ApplyToAllDepartments ? " (applied to all departments)" : ""),
                cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return new PaymentAccountDto
            {
                Id = account.Id,
                Name = account.Name,
                DepartmentId = account.DepartmentId,
                DepartmentName = account.Department?.Name ?? string.Empty,
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
                ValidityDays = request.ValidityDays,
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
            plan.ValidityDays = request.ValidityDays;
            plan.IsActive = request.IsActive;

            await _auditLog.StageAsync(AuditAction.Update, nameof(PackagePlan), plan.Id.ToString(), cancellationToken: cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return plan.ToDto();
        }

        public async Task<BulkImportResult> BulkImportPlansAsync(Stream file, string fileName, CancellationToken cancellationToken = default)
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

                    Guid? courseId = null;
                    var courseName = row.GetOrNull("CourseName");
                    if (courseName is not null)
                    {
                        var course = await _unitOfWork.Repository<Course>().FirstOrDefaultAsync(c => c.Name == courseName, cancellationToken)
                            ?? throw new NotFoundException($"No course named '{courseName}'.");
                        courseId = course.Id;
                    }

                    var billingTypeText = row.GetOrNull("BillingType")
                        ?? throw new DomainValidationException("BillingType is required (Subscription, SessionBased or OneTime).");
                    if (!Enum.TryParse<BillingType>(billingTypeText, true, out var billingType))
                    {
                        throw new DomainValidationException($"BillingType '{billingTypeText}' is not valid — use Subscription, SessionBased or OneTime.");
                    }

                    var billingCycleText = row.GetOrNull("BillingCycle")
                        ?? throw new DomainValidationException("BillingCycle is required (Monthly, Quarterly, Yearly or OneTime).");
                    if (!Enum.TryParse<BillingCycle>(billingCycleText, true, out var billingCycle))
                    {
                        throw new DomainValidationException($"BillingCycle '{billingCycleText}' is not valid — use Monthly, Quarterly, Yearly or OneTime.");
                    }

                    var priceText = row.GetOrNull("Price") ?? throw new DomainValidationException("Price is required.");
                    if (!decimal.TryParse(priceText, out var price))
                    {
                        throw new DomainValidationException($"Price '{priceText}' is not a number.");
                    }

                    int? sessionsIncluded = null;
                    var sessionsText = row.GetOrNull("SessionsIncluded");
                    if (sessionsText is not null)
                    {
                        if (!int.TryParse(sessionsText, out var parsedSessions))
                        {
                            throw new DomainValidationException($"SessionsIncluded '{sessionsText}' is not a whole number.");
                        }
                        sessionsIncluded = parsedSessions;
                    }

                    int? validityDays = null;
                    var validityText = row.GetOrNull("ValidityDays");
                    if (validityText is not null)
                    {
                        if (!int.TryParse(validityText, out var parsedValidity))
                        {
                            throw new DomainValidationException($"ValidityDays '{validityText}' is not a whole number.");
                        }
                        validityDays = parsedValidity;
                    }

                    await CreatePlanAsync(
                        new SavePackagePlanRequest
                        {
                            Name = name,
                            CourseId = courseId,
                            BillingType = billingType,
                            BillingCycle = billingCycle,
                            Price = price,
                            SessionsIncluded = sessionsIncluded,
                            ValidityDays = validityDays,
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

        public async Task<string> ExportPlansCsvAsync(CancellationToken cancellationToken = default)
        {
            var plans = await ListPlansAsync(cancellationToken);
            var courseIds = plans.Where(p => p.CourseId.HasValue).Select(p => p.CourseId!.Value).Distinct().ToList();
            var courseNames = await _unitOfWork.Repository<Course>().Query()
                .Where(c => courseIds.Contains(c.Id))
                .ToDictionaryAsync(c => c.Id, c => c.Name, cancellationToken);

            string[] headers = ["Name", "CourseName", "BillingType", "BillingCycle", "Price", "SessionsIncluded", "ValidityDays", "IsActive"];
            var rows = plans.Select(p => new List<string?>
            {
                p.Name,
                p.CourseId.HasValue ? courseNames.GetValueOrDefault(p.CourseId.Value) : null,
                p.BillingType.ToString(),
                p.BillingCycle.ToString(),
                p.Price.ToString(System.Globalization.CultureInfo.InvariantCulture),
                p.SessionsIncluded?.ToString(),
                p.ValidityDays?.ToString(),
                p.IsActive ? "true" : "false",
            });
            return CsvWriter.BuildCsv(headers, rows);
        }

        /// <summary>
        /// The navigation properties <see cref="BillingMappings.ToDto(Invoice)"/> reads.
        /// Lazy loading is not enabled on this context, so an invoice fetched without these
        /// does not lazy-load them and does not throw either — the mapping is null-safe, so
        /// it silently returns an InvoiceDto with ChildName/CourseName/ParentName/ParentEmail
        /// all null. Routing every invoice-to-DTO read through this keeps the payload the
        /// same shape no matter which endpoint produced it.
        /// </summary>
        private static IQueryable<Invoice> WithDtoIncludes(IQueryable<Invoice> query) =>
            query
                .Include(i => i.Child)
                .Include(i => i.Course)
                .Include(i => i.ParentProfile).ThenInclude(p => p.User)
                .Include(i => i.Subscription).ThenInclude(s => s!.PackagePlan).ThenInclude(p => p.Course);

        public async Task<PagedResult<InvoiceDto>> ListInvoicesAsync(
            InvoiceStatus? status,
            Guid? parentProfileId,
            int page,
            int pageSize,
            CancellationToken cancellationToken = default)
        {
            page = Math.Max(page, 1);
            pageSize = Math.Clamp(pageSize, 1, MaxInvoicePageSize);

            var query = WithDtoIncludes(_unitOfWork.Repository<Invoice>().Query());
            if (status.HasValue)
            {
                query = query.Where(i => i.Status == status.Value);
            }

            if (parentProfileId.HasValue)
            {
                query = query.Where(i => i.ParentProfileId == parentProfileId.Value);
            }

            // Counted before the includes matter: EF translates this to a COUNT over the
            // filtered set, so the joins cost nothing on this half of the round trip.
            var totalCount = await query.CountAsync(cancellationToken);

            var invoices = await query
                .OrderByDescending(i => i.IssuedAtUtc)
                .ThenBy(i => i.Id)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync(cancellationToken);

            return new PagedResult<InvoiceDto>
            {
                Items = invoices.Select(i => i.ToDto()).ToList(),
                TotalCount = totalCount,
                Page = page,
                PageSize = pageSize,
            };
        }

        /// <summary>
        /// "invoice.*" AppSetting keys (Settings → General → Invoice Details). Real bank/GST/
        /// signatory details are org-specific and must never be hardcoded in source (they used
        /// to be, both here and in InvoicePdfGenerator — a real bank account number, IFSC and
        /// GSTIN sitting in plaintext in git history). This placeholder is shown only until an
        /// admin fills in Settings → General → Invoice Details; the real values now live solely
        /// in the database.
        /// </summary>
        private const string InvoiceSettingNotConfigured = "Not configured";

        private static readonly HashSet<string> InvoiceSettingKeys =
        [
            "invoice.accountNumber",
            "invoice.ifscCode",
            "invoice.branchName",
            "invoice.gstNumber",
            "invoice.accountName",
            "invoice.contactEmail",
            "invoice.signatoryName",
            "invoice.signatoryTitle",
        ];

        public async Task<(byte[] Content, string FileName)> GenerateInvoicePdfAsync(Guid id, CancellationToken cancellationToken = default)
        {
            var invoice = await WithDtoIncludes(_unitOfWork.Repository<Invoice>().Query())
                .FirstOrDefaultAsync(i => i.Id == id, cancellationToken)
                ?? throw new NotFoundException(nameof(Invoice), id);

            var settings = await _unitOfWork.Repository<AppSetting>().Query()
                .Where(s => InvoiceSettingKeys.Contains(s.Key))
                .ToDictionaryAsync(s => s.Key, s => s.Value, cancellationToken);

            string Setting(string key) =>
                settings.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
                    ? value
                    : InvoiceSettingNotConfigured;

            var data = new InvoicePdfData
            {
                InvoiceNumber = invoice.InvoiceNumber,
                IssuedAtUtc = invoice.IssuedAtUtc,
                ParentName = invoice.ParentProfile?.User is null
                    ? "—"
                    : $"{invoice.ParentProfile.User.FirstName} {invoice.ParentProfile.User.LastName}".Trim(),
                ParentPhone = invoice.ParentProfile?.User?.Phone,
                Description = invoice.Course?.Name ?? invoice.Subscription?.PackagePlan?.Course?.Name ?? "Course Fee",
                Amount = invoice.Amount,
                Currency = invoice.Currency,
                AccountNumber = Setting("invoice.accountNumber"),
                IfscCode = Setting("invoice.ifscCode"),
                BranchName = Setting("invoice.branchName"),
                GstNumber = Setting("invoice.gstNumber"),
                AccountName = Setting("invoice.accountName"),
                ContactEmail = Setting("invoice.contactEmail"),
                SignatoryName = Setting("invoice.signatoryName"),
                SignatoryTitle = Setting("invoice.signatoryTitle"),
            };

            var content = _invoicePdfGenerator.Generate(data);
            return (content, $"{invoice.InvoiceNumber}.pdf");
        }

        public async Task<InvoiceDto> CreateInvoiceAsync(
            CreateInvoiceRequest request,
            CancellationToken cancellationToken = default,
            bool isAutoGenerated = false)
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

            var department = await _unitOfWork.Repository<Department>().GetByIdAsync(request.DepartmentId, cancellationToken)
                ?? throw new NotFoundException(nameof(Department), request.DepartmentId);

            account ??= await _unitOfWork.Repository<PaymentAccount>()
                .FirstOrDefaultAsync(a => a.DepartmentId == request.DepartmentId && a.IsActive, cancellationToken)
                ?? throw new NotFoundException($"No active payment account is configured for the {department.Name} department.");

            var invoice = new Invoice
            {
                InvoiceNumber = GenerateNumber("INV"),
                ParentProfileId = request.ParentProfileId,
                ChildId = request.ChildId,
                SubscriptionId = request.SubscriptionId,
                CourseId = request.CourseId,
                PaymentAccountId = account.Id,
                DepartmentId = request.DepartmentId,
                Amount = request.Amount,
                DueDate = request.DueDate,
                IssuedAtUtc = DateTime.UtcNow,
                IsAutoGenerated = isAutoGenerated,
            };
            await _unitOfWork.Repository<Invoice>().AddAsync(invoice, cancellationToken);
            await _auditLog.StageAsync(AuditAction.Create, nameof(Invoice), invoice.Id.ToString(), cancellationToken: cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            // Invoices are emailed to the parent automatically the moment they're issued
            // (covers both manual invoices and the recurring-billing background service).
            // Settings → Notifications → "Fee payment reminders" also turns this one off —
            // it's the same parent-facing fee email as the daily due/overdue sweep
            // (SendPaymentRemindersAsync in BillingBackgroundService); leaving it ungated
            // meant every subscription renewal kept emailing the parent regardless of the toggle.
            var parentUser = await NotificationToggles.IsEnabledAsync(_unitOfWork, NotificationToggles.FeeReminders, cancellationToken)
                ? await _unitOfWork.Repository<ParentProfile>().Query()
                    .Where(p => p.Id == invoice.ParentProfileId)
                    .Select(p => p.User)
                    .FirstOrDefaultAsync(cancellationToken)
                : null;
            if (parentUser is not null)
            {
                await NotifyUserAsync(
                    parentUser,
                    NotificationType.PaymentReminder,
                    "invoice-issued",
                    new Dictionary<string, string>
                    {
                        ["ParentFirstName"] = parentUser.FirstName,
                        ["InvoiceNumber"] = invoice.InvoiceNumber,
                        ["Department"] = department.Name,
                        ["Amount"] = invoice.Amount.ToString("0.00"),
                        ["Currency"] = invoice.Currency,
                        ["DueDate"] = invoice.DueDate.ToString("yyyy-MM-dd"),
                    },
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
            // The balance read-then-write below (remaining = Amount - AmountPaid, then
            // AmountPaid += amount) is the same check-then-act shape RequestRefundAsync's own
            // comment documents: two concurrent settlements on the same invoice (a realistic
            // scenario — a parent can start a gateway checkout and then also pay cash at the
            // centre before it settles) can both read the same AmountPaid, both compute
            // "remaining" against it, and both commit their own absolute AmountPaid value —
            // the second commit silently overwrites the first's contribution, e.g. two real
            // ₹500 payments collected but the invoice ends up showing only ₹500 paid instead
            // of ₹1000. SERIALIZABLE (with the invoice re-read fresh inside, not the value
            // fetched before this block started) makes PostgreSQL abort one of two truly
            // concurrent attempts instead of letting them silently clobber each other.
            var settled = await _unitOfWork.ExecuteInSerializableTransactionAsync(async ct =>
            {
                var inv = await _unitOfWork.Repository<Invoice>().GetByIdAsync(invoiceId, ct)
                    ?? throw new NotFoundException(nameof(Invoice), invoiceId);

                if (inv.Status is InvoiceStatus.Paid or InvoiceStatus.Cancelled)
                {
                    throw new DomainValidationException($"Invoice '{inv.InvoiceNumber}' is already {inv.Status}.");
                }

                var remaining = inv.Amount - inv.AmountPaid;
                if (request.Amount > remaining)
                {
                    throw new DomainValidationException($"Payment of {request.Amount} exceeds the outstanding balance of {remaining}.");
                }

                // A cash recording settles the parent's pending cash intent when one exists,
                // so the intent doesn't linger Pending next to a duplicate Success row.
                var pendingIntent = request.Method == PaymentMethod.Cash
                    ? await _unitOfWork.Repository<PaymentTransaction>()
                        .FirstOrDefaultAsync(
                            t => t.InvoiceId == inv.Id
                                && t.Method == PaymentMethod.Cash
                                && t.Status == TransactionStatus.Pending,
                            ct)
                    : null;

                string receiptNumber;
                if (pendingIntent is not null)
                {
                    receiptNumber = GenerateNumber("RCP");
                    pendingIntent.Amount = request.Amount;
                    pendingIntent.Status = TransactionStatus.Success;
                    pendingIntent.PaidAtUtc = DateTime.UtcNow;
                    pendingIntent.ReceiptNumber = receiptNumber;
                    _unitOfWork.Repository<PaymentTransaction>().Update(pendingIntent);
                }
                else
                {
                    receiptNumber = GenerateNumber("RCP");
                    await _unitOfWork.Repository<PaymentTransaction>().AddAsync(
                        new PaymentTransaction
                        {
                            InvoiceId = inv.Id,
                            PaymentAccountId = inv.PaymentAccountId,
                            Amount = request.Amount,
                            Currency = inv.Currency,
                            Status = TransactionStatus.Success,
                            GatewayTransactionId = request.GatewayTransactionId,
                            Method = request.Method,
                            PaidAtUtc = DateTime.UtcNow,
                            ReceiptNumber = receiptNumber,
                        },
                        ct);
                }

                await ApplyPaymentToInvoiceAsync(inv, request.Amount, ct);

                await _auditLog.StageAsync(AuditAction.Payment, nameof(Invoice), inv.Id.ToString(),
                    changesJson: $"{{\"amount\":{request.Amount}}}", cancellationToken: ct);
                await _unitOfWork.SaveChangesAsync(ct);
                return (Invoice: inv, ReceiptNumber: receiptNumber);
            }, cancellationToken);

            var invoice = settled.Invoice;

            // Outside the transaction: a retried attempt must not re-send this email.
            await NotifyAdminsAsync(
                NotificationType.PaymentReceived,
                "payment-received-admin",
                new Dictionary<string, string>
                {
                    ["Amount"] = request.Amount.ToString("0.00"),
                    ["Currency"] = invoice.Currency,
                    ["InvoiceNumber"] = invoice.InvoiceNumber,
                    ["Status"] = invoice.Status.ToString(),
                },
                cancellationToken);

            // Caught live: RecordPaymentAsync (an admin manually recording a payment collected
            // through any method -- bank transfer, cheque, or a cash payment that never went
            // through the parent's own "I'll pay in cash" intent) only ever notified Admins,
            // same gap as SettleGatewayTransactionAsync had. Method-specific template so a cash
            // recording still reads as "cash payment" (and carries the receipt number), matching
            // what ConfirmCashIntentAsync already sends for that same wording.
            var parentUser = await _unitOfWork.Repository<ParentProfile>().Query()
                .Where(p => p.Id == invoice.ParentProfileId)
                .Select(p => p.User)
                .FirstOrDefaultAsync(cancellationToken);
            if (parentUser is not null)
            {
                if (request.Method == PaymentMethod.Cash)
                {
                    await NotifyUserAsync(
                        parentUser,
                        NotificationType.PaymentReceived,
                        "cash-payment-confirmed-parent",
                        new Dictionary<string, string>
                        {
                            ["Amount"] = request.Amount.ToString("0.00"),
                            ["Currency"] = invoice.Currency,
                            ["InvoiceNumber"] = invoice.InvoiceNumber,
                            ["ReceiptNumber"] = settled.ReceiptNumber,
                        },
                        cancellationToken);
                }
                else
                {
                    await NotifyUserAsync(
                        parentUser,
                        NotificationType.PaymentReceived,
                        "gateway-payment-confirmed-parent",
                        new Dictionary<string, string>
                        {
                            ["Amount"] = request.Amount.ToString("0.00"),
                            ["Currency"] = invoice.Currency,
                            ["InvoiceNumber"] = invoice.InvoiceNumber,
                        },
                        cancellationToken);
                }
            }

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

                // Access restoration: full payment on THIS invoice auto-lifts any active fee
                // suspension for the parent — but only when nothing else is outstanding. A
                // suspension is one row per parent (it can cover several overdue invoices at
                // once, see BillingBackgroundService), so lifting it just because one of
                // several overdue invoices got paid would silently restore access while the
                // parent still owes money on another invoice.
                var hasOtherOverdueInvoice = await _unitOfWork.Repository<Invoice>().ExistsAsync(
                    i => i.ParentProfileId == invoice.ParentProfileId
                        && i.Id != invoice.Id
                        && i.Status == InvoiceStatus.Overdue,
                    cancellationToken);

                if (!hasOtherOverdueInvoice)
                {
                    // TrackedQuery() fetches every matching row already tracked, so each is
                    // mutated in place with no extra per-row round trip to re-fetch it.
                    var suspensions = await _unitOfWork.Repository<FeeSuspension>().TrackedQuery()
                        .Where(s => s.ParentProfileId == invoice.ParentProfileId && s.Status == SuspensionStatus.Active)
                        .ToListAsync(cancellationToken);
                    foreach (var suspension in suspensions)
                    {
                        suspension.Status = SuspensionStatus.Lifted;
                        suspension.LiftedAtUtc = DateTime.UtcNow;
                        suspension.AutoRestored = true;
                        _unitOfWork.Repository<FeeSuspension>().Update(suspension);
                    }
                }

                // Close any OTHER still-pending cash intents on this invoice: the money
                // arrived through another payment, so there is nothing left to collect and
                // they must not linger in the staff confirmation queue.
                //
                // The second Where is not redundant with the first. Callers reach here having
                // already flipped the intent they are settling to Success on the TRACKED entity,
                // but that change is not committed yet, so the DB-side predicate still sees it as
                // Pending and returns it. EF then hands back the tracked instance without
                // clobbering its pending modification — so filtering again in memory (where
                // Status is already Success) is what excludes the transaction currently being
                // settled. Without it, confirming a cash payment that fully settles its invoice
                // immediately re-marked that very payment Failed: the invoice was Paid but the
                // money's own transaction row was not Success, so it vanished from the invoice's
                // receipt list and could never be refunded.
                var staleIntents = (await _unitOfWork.Repository<PaymentTransaction>().TrackedQuery()
                    .Where(t => t.InvoiceId == invoice.Id
                        && t.Method == PaymentMethod.Cash
                        && t.Status == TransactionStatus.Pending)
                    .ToListAsync(cancellationToken))
                    .Where(t => t.Status == TransactionStatus.Pending)
                    .ToList();
                foreach (var intent in staleIntents)
                {
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

        /// <summary>
        /// Starting a new payment attempt (cash intent, gateway redirect, or inline checkout)
        /// makes any other still-pending intent on this same invoice stale — the parent chose
        /// a different method or retried, so the old one must not be settleable independently
        /// later. Without this, two concurrent pending intents (e.g. an abandoned redirect link
        /// plus a fresh inline checkout) could both complete at the gateway; the invoice-status
        /// guard in SettleGatewayTransactionAsync stops that from double-crediting the invoice,
        /// but leaving the stale one live is unnecessary exposure this closes at the source.
        /// </summary>
        private async Task SupersedePendingIntentsAsync(Guid invoiceId, CancellationToken cancellationToken)
        {
            var stalePending = await _unitOfWork.Repository<PaymentTransaction>().TrackedQuery()
                .Where(t => t.InvoiceId == invoiceId && t.Status == TransactionStatus.Pending)
                .ToListAsync(cancellationToken);

            foreach (var stale in stalePending)
            {
                stale.Status = TransactionStatus.Failed;
                stale.FailureReason = "Superseded by a new payment attempt on this invoice.";
                _unitOfWork.Repository<PaymentTransaction>().Update(stale);
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

            return (invoice.ToDto(), $"{parent.User.FirstName} {parent.User.LastName}".Trim());
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
                await SupersedePendingIntentsAsync(invoice.Id, cancellationToken);
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
                    "cash-intent-billing-staff",
                    new Dictionary<string, string>
                    {
                        ["Amount"] = remaining.ToString("0.00"),
                        ["Currency"] = invoice.Currency,
                        ["InvoiceNumber"] = invoice.InvoiceNumber,
                    },
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

            await SupersedePendingIntentsAsync(invoice.Id, cancellationToken);
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

            await SupersedePendingIntentsAsync(invoice.Id, cancellationToken);
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
                DisplayName = await BrandSettings.GetNameAsync(_unitOfWork, cancellationToken),
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

            var refreshed = await WithDtoIncludes(_unitOfWork.Repository<Invoice>().Query())
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
            // Same lost-update shape as RecordPaymentAsync/ConfirmCashIntentAsync — a webhook
            // settling this transaction can race a concurrent cash confirmation or a second
            // webhook delivery for a *different* transaction on the same invoice. Wrapped from
            // the transaction fetch through the balance write; every read inside is fresh per
            // attempt so a detected conflict retries against the truly-committed state. Returns
            // null for every early-exit branch that never touches the invoice balance (nothing
            // to notify about); non-null only once a payment was actually applied.
            var settled = await _unitOfWork.ExecuteInSerializableTransactionAsync<(PaymentTransaction Transaction, Invoice Invoice, decimal Amount)?>(async ct =>
            {
                // Idempotency: webhooks retry — an already-settled reference is a no-op.
                // A settled row's reference becomes "ref|paymentId", so match the prefix too.
                var prefix = gatewayReference + "|";
                var transaction = await _unitOfWork.Repository<PaymentTransaction>()
                    .FirstOrDefaultAsync(
                        t => t.GatewayTransactionId == gatewayReference
                            || (t.GatewayTransactionId != null && t.GatewayTransactionId.StartsWith(prefix)),
                        ct)
                    ?? throw new NotFoundException($"No payment transaction matches gateway reference '{gatewayReference}'.");

                if (transaction.Status != TransactionStatus.Pending)
                {
                    if (succeeded && transaction.Status == TransactionStatus.Failed)
                    {
                        // The gateway reports a paid link/order we'd already given up on (expired,
                        // or superseded by a later payment attempt on the same invoice) — real
                        // money may have arrived after the fact. Flag it instead of a silent no-op
                        // so someone reconciles it by hand; don't touch the invoice balance here.
                        await _auditLog.StageAsync(AuditAction.Payment, nameof(Invoice), transaction.InvoiceId.ToString(),
                            changesJson: $"{{\"lateSuccessOnSupersededTransaction\":\"{gatewayReference}\"}}",
                            cancellationToken: ct);
                        await _unitOfWork.SaveChangesAsync(ct);
                    }

                    return null;
                }

                if (!succeeded)
                {
                    transaction.Status = TransactionStatus.Failed;
                    transaction.FailureReason = failureReason?.Length > 500 ? failureReason[..500] : failureReason;
                    _unitOfWork.Repository<PaymentTransaction>().Update(transaction);
                    await _unitOfWork.SaveChangesAsync(ct);
                    return null;
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

                var invoice = await _unitOfWork.Repository<Invoice>().GetByIdAsync(transaction.InvoiceId, ct)
                    ?? throw new NotFoundException(nameof(Invoice), transaction.InvoiceId);

                if (invoice.Status == InvoiceStatus.Cancelled)
                {
                    // A cancelled invoice never accepts payment, regardless of balance.
                    await _auditLog.StageAsync(AuditAction.Payment, nameof(Invoice), invoice.Id.ToString(),
                        changesJson: $"{{\"overpayment\":{transaction.Amount},\"gatewayRef\":\"{gatewayReference}\",\"invoiceStatus\":\"{invoice.Status}\"}}",
                        cancellationToken: ct);
                    await _unitOfWork.SaveChangesAsync(ct);
                    return null;
                }

                var remaining = invoice.Amount - invoice.AmountPaid;
                if (remaining <= 0)
                {
                    // Another payment (a parallel checkout attempt, or a manual cash entry) already
                    // settled this invoice before this gateway transaction confirmed. The money did
                    // arrive at the gateway — the transaction is still recorded as Success above —
                    // but it must not be double-applied to an invoice that's already covered.
                    // Flagged in the audit trail since it now needs a manual refund.
                    await _auditLog.StageAsync(AuditAction.Payment, nameof(Invoice), invoice.Id.ToString(),
                        changesJson: $"{{\"overpayment\":{transaction.Amount},\"gatewayRef\":\"{gatewayReference}\",\"invoiceStatus\":\"{invoice.Status}\"}}",
                        cancellationToken: ct);
                    await _unitOfWork.SaveChangesAsync(ct);
                    return null;
                }

                // Same idea, partial case: another payment landed on part of the balance while
                // this gateway transaction was in flight. Apply only what's actually still owed —
                // never let AmountPaid run past Amount — and flag the excess for reconciliation
                // instead of silently inflating the invoice past 100% paid.
                var amountToApply = transaction.Amount;
                if (amountToApply > remaining)
                {
                    await _auditLog.StageAsync(AuditAction.Payment, nameof(Invoice), invoice.Id.ToString(),
                        changesJson: $"{{\"partialOverpayment\":{amountToApply - remaining},\"gatewayRef\":\"{gatewayReference}\"}}",
                        cancellationToken: ct);
                    amountToApply = remaining;
                }

                await ApplyPaymentToInvoiceAsync(invoice, amountToApply, ct);

                await _auditLog.StageAsync(AuditAction.Payment, nameof(Invoice), invoice.Id.ToString(),
                    changesJson: $"{{\"amount\":{amountToApply},\"gatewayRef\":\"{gatewayReference}\"}}", cancellationToken: ct);
                await _unitOfWork.SaveChangesAsync(ct);
                return (transaction, invoice, amountToApply);
            }, cancellationToken);

            if (settled is null) return;

            // Outside the transaction: a retried attempt must not re-send this email.
            await NotifyAdminsAsync(
                NotificationType.PaymentReceived,
                "payment-received-admin",
                new Dictionary<string, string>
                {
                    ["Amount"] = settled.Value.Transaction.Amount.ToString("0.00"),
                    ["Currency"] = settled.Value.Invoice.Currency,
                    ["InvoiceNumber"] = settled.Value.Invoice.InvoiceNumber,
                    ["Status"] = settled.Value.Invoice.Status.ToString(),
                },
                cancellationToken);

            // Caught live: this webhook path (the real Razorpay/Cashfree settlement -- what
            // actually fires when a parent pays online) used to only notify Admins. A parent
            // paying by cash gets ConfirmCashIntentAsync's own confirmation email the moment
            // staff confirm it; a parent paying online got nothing from the platform at all.
            var parentUser = await _unitOfWork.Repository<ParentProfile>().Query()
                .Where(p => p.Id == settled.Value.Invoice.ParentProfileId)
                .Select(p => p.User)
                .FirstOrDefaultAsync(cancellationToken);
            if (parentUser is not null)
            {
                await NotifyUserAsync(
                    parentUser,
                    NotificationType.PaymentReceived,
                    "gateway-payment-confirmed-parent",
                    new Dictionary<string, string>
                    {
                        ["Amount"] = settled.Value.Amount.ToString("0.00"),
                        ["Currency"] = settled.Value.Invoice.Currency,
                        ["InvoiceNumber"] = settled.Value.Invoice.InvoiceNumber,
                    },
                    cancellationToken);
            }
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
            // See RecordPaymentAsync's own comment: the balance read-then-write here has the
            // exact same lost-update shape, and a cash confirmation racing a gateway payment
            // settling the same invoice moments apart is a realistic way to hit it. Re-fetch
            // fresh inside the transaction (not the values from before this block) so a retry
            // after a detected conflict sees the truly-committed balance.
            var (transaction, invoice, amount) = await _unitOfWork.ExecuteInSerializableTransactionAsync(async ct =>
            {
                var txn = await LoadPendingCashIntentAsync(transactionId, ct);
                var inv = await _unitOfWork.Repository<Invoice>().GetByIdAsync(txn.InvoiceId, ct)
                    ?? throw new NotFoundException(nameof(Invoice), txn.InvoiceId);

                var amt = request.Amount ?? txn.Amount;
                var remaining = inv.Amount - inv.AmountPaid;

                // Stale intent: the invoice was settled by another payment while this intent sat
                // pending (older data predating the auto-close in ApplyPaymentToInvoiceAsync).
                // Close it here so it leaves the confirmation queue instead of erroring forever.
                if (remaining <= 0)
                {
                    txn.Status = TransactionStatus.Failed;
                    txn.FailureReason = "Invoice was already fully paid; cash intent closed without collection.";
                    _unitOfWork.Repository<PaymentTransaction>().Update(txn);
                    await _unitOfWork.SaveChangesAsync(ct);
                    throw new DomainValidationException(
                        "This invoice is already fully paid — the cash intent has been closed. Do not collect the cash.");
                }

                if (amt > remaining)
                {
                    throw new DomainValidationException($"Confirmed amount {amt} exceeds the outstanding balance of {remaining}.");
                }

                txn.Amount = amt;
                txn.Status = TransactionStatus.Success;
                txn.PaidAtUtc = DateTime.UtcNow;
                txn.ReceiptNumber = GenerateNumber("RCP");
                _unitOfWork.Repository<PaymentTransaction>().Update(txn);

                await ApplyPaymentToInvoiceAsync(inv, amt, ct);

                await _auditLog.StageAsync(AuditAction.Payment, nameof(Invoice), inv.Id.ToString(),
                    changesJson: $"{{\"cashConfirmed\":{amt},\"reference\":\"{txn.GatewayTransactionId}\"}}",
                    cancellationToken: ct);
                await _unitOfWork.SaveChangesAsync(ct);
                return (txn, inv, amt);
            }, cancellationToken);

            // Close the loop with the parent: their portal invoice flips as soon as staff confirm.
            // Outside the transaction: a retried attempt must not re-send this email.
            var parentUser = await _unitOfWork.Repository<ParentProfile>().Query()
                .Where(p => p.Id == invoice.ParentProfileId)
                .Select(p => p.User)
                .FirstOrDefaultAsync(cancellationToken);
            if (parentUser is not null)
            {
                await NotifyUserAsync(
                    parentUser,
                    NotificationType.PaymentReceived,
                    "cash-payment-confirmed-parent",
                    new Dictionary<string, string>
                    {
                        ["Amount"] = amount.ToString("0.00"),
                        ["Currency"] = invoice.Currency,
                        ["InvoiceNumber"] = invoice.InvoiceNumber,
                        ["ReceiptNumber"] = transaction.ReceiptNumber ?? "",
                    },
                    cancellationToken);
                await _unitOfWork.SaveChangesAsync(cancellationToken);
            }

            var parentName = parentUser is null ? "—" : $"{parentUser.FirstName} {parentUser.LastName}".Trim();
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

            var invoice = await WithDtoIncludes(_unitOfWork.Repository<Invoice>().Query())
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
            var refreshed = await WithDtoIncludes(_unitOfWork.Repository<Invoice>().Query())
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

            // Caught live: nothing told the parent their access was restored either.
            await NotifyUserAsync(
                saved.ParentProfile.User,
                NotificationType.FeeSuspension,
                "fee-suspension-lifted-parent",
                new Dictionary<string, string>(),
                cancellationToken);

            return ToDto(saved);
        }

        public async Task<IReadOnlyList<PaymentTransactionDto>> ListInvoiceTransactionsAsync(Guid invoiceId, CancellationToken cancellationToken = default)
        {
            var transactions = await _unitOfWork.Repository<PaymentTransaction>().Query()
                .Where(t => t.InvoiceId == invoiceId && t.Status == TransactionStatus.Success)
                .OrderByDescending(t => t.PaidAtUtc)
                .ToListAsync(cancellationToken);

            if (transactions.Count == 0)
            {
                return [];
            }

            var transactionIds = transactions.Select(t => t.Id).ToList();
            var refundedByTransaction = await _unitOfWork.Repository<Refund>().Query()
                .Where(r => transactionIds.Contains(r.PaymentTransactionId) && r.Status != RefundStatus.Rejected)
                .GroupBy(r => r.PaymentTransactionId)
                .Select(g => new { TransactionId = g.Key, Total = g.Sum(r => r.Amount) })
                .ToDictionaryAsync(g => g.TransactionId, g => g.Total, cancellationToken);

            return transactions.Select(t => new PaymentTransactionDto
            {
                Id = t.Id,
                Amount = t.Amount,
                Status = t.Status.ToString(),
                Method = t.Method?.ToString(),
                PaidAtUtc = t.PaidAtUtc,
                ReceiptNumber = t.ReceiptNumber,
                AlreadyRefunded = refundedByTransaction.GetValueOrDefault(t.Id),
            }).ToList();
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

            // Only money that actually arrived can be given back. A Pending intent (a cash
            // declaration nobody has collected yet, or an abandoned checkout) and a Failed
            // one both have a zero real balance, so approving a refund against either
            // disburses funds the platform never received — the "cannot exceed the
            // transaction amount" ceiling below is meaningless without this.
            if (transaction.Status != TransactionStatus.Success)
            {
                throw new DomainValidationException(
                    $"Only a successful payment can be refunded; this transaction is {transaction.Status}.");
            }

            // Summing the existing refunds and inserting the new one must be one atomic unit.
            // Read then insert with nothing in between is the same check-then-act shape as the
            // demo-booking double-assignment (see DemoBookingService.CreateAsync): two requests
            // for the same transaction can both read the same "already refunded" total, both find
            // room under the ceiling, and both insert. The result is two live refunds that
            // together exceed the money actually collected — and each is independently
            // approvable, because ReviewRefundAsync's atomic claim is per refund ROW, so the
            // ceiling enforced here is the only thing standing between them and a double
            // disbursement. SERIALIZABLE makes the sum and the insert mutually exclusive:
            // PostgreSQL's SSI aborts one of two overlapping attempts with SQLSTATE 40001 and
            // the unit of work retries it against the committed state, where the ceiling is now
            // correctly exceeded and the refusal below fires.
            var refundId = await _unitOfWork.ExecuteInSerializableTransactionAsync(async ct =>
            {
                // Sum every refund not already rejected — a second request must not be able to
                // stack on top of one still pending review or one already paid out.
                var alreadyRefunded = await _unitOfWork.Repository<Refund>().Query()
                    .Where(r => r.PaymentTransactionId == transaction.Id && r.Status != RefundStatus.Rejected)
                    .SumAsync(r => (decimal?)r.Amount, ct) ?? 0m;

                if (alreadyRefunded + request.Amount > transaction.Amount)
                {
                    throw new DomainValidationException(
                        $"Refund of {request.Amount} would exceed the transaction amount of {transaction.Amount} " +
                        $"({alreadyRefunded} already requested/refunded).");
                }

                var refund = new Refund
                {
                    PaymentTransactionId = transaction.Id,
                    Amount = request.Amount,
                    Reason = request.Reason.Trim(),
                };
                await _unitOfWork.Repository<Refund>().AddAsync(refund, ct);
                await _auditLog.StageAsync(AuditAction.Create, nameof(Refund), refund.Id.ToString(), cancellationToken: ct);
                await _unitOfWork.SaveChangesAsync(ct);
                return refund.Id;
            }, cancellationToken);

            var saved = await _unitOfWork.Repository<Refund>().Query()
                .Include(r => r.PaymentTransaction).ThenInclude(t => t.Invoice)
                .FirstAsync(r => r.Id == refundId, cancellationToken);

            // Caught live: nothing told billing staff a refund needed review at all -- the only
            // way to notice one was requested was to happen to check Billing & Finance → Refunds.
            await NotifyBillingStaffAsync(
                NotificationType.PaymentReceived,
                "refund-requested-billing-staff",
                new Dictionary<string, string>
                {
                    ["Amount"] = saved.Amount.ToString("0.00"),
                    ["Currency"] = saved.PaymentTransaction.Currency,
                    ["InvoiceNumber"] = saved.PaymentTransaction.Invoice?.InvoiceNumber ?? "—",
                    ["Reason"] = saved.Reason,
                },
                cancellationToken);

            return ToDto(saved);
        }

        /// <remarks>
        /// RACE FIXED (found 2026-08-09, fixed 2026-08-10). This used to read the refund's
        /// Status, check it was still Requested, and mutate it afterwards with nothing in
        /// between — so under READ COMMITTED two concurrent approvals of the SAME refund (an
        /// admin double-click, or two admins working the same queue) could both read "still
        /// Requested" and both reach _paymentGateway.RefundAsync, refunding real money twice.
        /// <para>
        /// The status transition is now a single conditional UPDATE
        /// (<c>SET status = ... WHERE id = @id AND status = 'Requested'</c>) issued through
        /// IRepository.ExecuteUpdateAsync, and the gateway is only called by whoever that
        /// UPDATE reports as having won. That is atomic without any isolation-level change:
        /// the second approver's UPDATE blocks on the row's write lock until the first commits,
        /// then re-reads the row, no longer matches <c>status = 'Requested'</c>, and affects 0
        /// rows — which this method turns into a ConflictException instead of a second
        /// disbursement. The in-memory check above it is kept only as a cheap, friendlier error
        /// for the ordinary already-reviewed case; it is not what makes this safe.
        /// </para>
        /// <para>
        /// Approval claims the row as Approved BEFORE the gateway call and only marks it
        /// Processed once the gateway has confirmed, so the window during which money is in
        /// flight is a distinct, already-non-Requested state. That is deliberately fail-closed:
        /// if the gateway call throws (including an ambiguous timeout that may in fact have
        /// disbursed), the refund is left Approved rather than released back to Requested, so no
        /// retry can double-pay it — an operator reconciles it against the gateway and finishes
        /// or rejects it by hand. Releasing the claim automatically would re-open exactly the
        /// double-refund hole this fixes.
        /// </para>
        /// </remarks>
        public async Task<RefundDto> ReviewRefundAsync(Guid id, ReviewRefundRequest request, CancellationToken cancellationToken = default)
        {
            var refunds = _unitOfWork.Repository<Refund>();

            // Load tracked (Query() is AsNoTracking; mutating that never persists).
            var refund = await refunds.GetByIdAsync(id, cancellationToken)
                ?? throw new NotFoundException(nameof(Refund), id);

            // Cheap, friendly rejection of the ordinary "someone already dealt with this"
            // case. This read can be stale by the time it is acted on — the conditional
            // UPDATE below is the guard that actually decides who wins.
            if (refund.Status != RefundStatus.Requested)
            {
                throw new DomainValidationException($"This refund is already {refund.Status}.");
            }

            // ExecuteUpdateAsync bypasses the change tracker, so the audit interceptor never
            // sees these writes — stamp what it would have stamped by hand.
            var reviewedAtUtc = DateTime.UtcNow;
            var reviewedBy = _currentUser.UserId;

            if (!request.Approve)
            {
                var rejected = await refunds.ExecuteUpdateAsync(
                    r => r.Id == id && r.Status == RefundStatus.Requested,
                    setters => setters
                        .SetProperty(r => r.Status, RefundStatus.Rejected)
                        .SetProperty(r => r.UpdatedAtUtc, reviewedAtUtc)
                        .SetProperty(r => r.UpdatedBy, reviewedBy),
                    cancellationToken);
                if (rejected == 0)
                {
                    throw new ConflictException("This refund was already reviewed by another request; nothing was changed.");
                }
            }
            else
            {
                // Claim the refund before spending any money on it. Winning this UPDATE is what
                // grants the right to call the gateway; losing it (0 rows) means another request
                // already owns this refund, so we must not disburse.
                var claimed = await refunds.ExecuteUpdateAsync(
                    r => r.Id == id && r.Status == RefundStatus.Requested,
                    setters => setters
                        .SetProperty(r => r.Status, RefundStatus.Approved)
                        .SetProperty(r => r.UpdatedAtUtc, reviewedAtUtc)
                        .SetProperty(r => r.UpdatedBy, reviewedBy),
                    cancellationToken);
                if (claimed == 0)
                {
                    throw new ConflictException("This refund was already reviewed by another request; it has not been refunded twice.");
                }

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

                    // Anything thrown here leaves the refund parked in Approved on purpose: the
                    // disbursement's outcome is unknown, so it must not become approvable again.
                    var result = await _paymentGateway.RefundAsync(transaction, account, refund.Amount, cancellationToken);
                    refund.GatewayRefundId = result.GatewayRefundId;
                }

                // Money is out; close the claim. The tracked entity still believes it is
                // Requested (it was loaded before the claim UPDATE), which is harmless — EF
                // writes the new value either way, and the DTO below is re-read from the row.
                refund.Status = RefundStatus.Processed;
                refund.ProcessedAtUtc = DateTime.UtcNow;
            }

            await _auditLog.StageAsync(AuditAction.Update, nameof(Refund), refund.Id.ToString(), cancellationToken: cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            var saved = await _unitOfWork.Repository<Refund>().Query()
                .Include(r => r.PaymentTransaction).ThenInclude(t => t.Invoice)
                .FirstAsync(r => r.Id == id, cancellationToken);

            // Caught live: the parent learned nothing either way -- not that their refund was
            // rejected, and not that an approved one had actually been paid out.
            var refundParentUser = saved.PaymentTransaction.Invoice is { } refundInvoice
                ? await _unitOfWork.Repository<ParentProfile>().Query()
                    .Where(p => p.Id == refundInvoice.ParentProfileId)
                    .Select(p => p.User)
                    .FirstOrDefaultAsync(cancellationToken)
                : null;
            if (refundParentUser is not null)
            {
                var refundTokens = new Dictionary<string, string>
                {
                    ["Amount"] = saved.Amount.ToString("0.00"),
                    ["Currency"] = saved.PaymentTransaction.Currency,
                    ["InvoiceNumber"] = saved.PaymentTransaction.Invoice!.InvoiceNumber,
                };
                await NotifyUserAsync(
                    refundParentUser,
                    NotificationType.PaymentReceived,
                    saved.Status == RefundStatus.Rejected ? "refund-rejected-parent" : "refund-processed-parent",
                    refundTokens,
                    cancellationToken);
            }

            return ToDto(saved);
        }

        private async Task NotifyUserAsync(
            User user,
            NotificationType type,
            string templateKey,
            IReadOnlyDictionary<string, string> tokens,
            CancellationToken cancellationToken)
        {
            await _notificationService.SendTemplatedEmailAsync(user.Id, user.Email, type, templateKey, tokens, cancellationToken);
        }

        private async Task NotifyAdminsAsync(
            NotificationType type,
            string templateKey,
            IReadOnlyDictionary<string, string> tokens,
            CancellationToken cancellationToken)
        {
            var admins = await _unitOfWork.Repository<User>().Query()
                .Where(u => u.Role == UserRole.Admin && u.Status == UserStatus.Active)
                .ToListAsync(cancellationToken);
            foreach (var admin in admins)
            {
                await _notificationService.SendTemplatedEmailAsync(admin.Id, admin.Email, type, templateKey, tokens, cancellationToken);
            }
        }

        /// <summary>Admin + Admission Team — the two roles that actually own cash confirmation.</summary>
        private async Task NotifyBillingStaffAsync(
            NotificationType type,
            string templateKey,
            IReadOnlyDictionary<string, string> tokens,
            CancellationToken cancellationToken)
        {
            var staff = await _unitOfWork.Repository<User>().Query()
                .Where(u => (u.Role == UserRole.Admin || u.Role == UserRole.AdmissionTeam) && u.Status == UserStatus.Active)
                .ToListAsync(cancellationToken);
            foreach (var user in staff)
            {
                await _notificationService.SendTemplatedEmailAsync(user.Id, user.Email, type, templateKey, tokens, cancellationToken);
            }
        }

        private static FeeSuspensionDto ToDto(FeeSuspension suspension)
        {
            return new FeeSuspensionDto
            {
                Id = suspension.Id,
                ParentProfileId = suspension.ParentProfileId,
                ParentName = $"{suspension.ParentProfile.User.FirstName} {suspension.ParentProfile.User.LastName}".Trim(),
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
                EndDate = plan.ValidityDays.HasValue ? request.StartDate.AddDays(plan.ValidityDays.Value) : null,
                // Started subscriptions get their first invoice below, so the pointer moves
                // one cycle out; a future start leaves the first invoice to the billing job
                // on the start date itself.
                NextBillingAtUtc = startsNow ? NextBillingFrom(startUtc, plan.BillingCycle) : startUtc,
            };
            await _unitOfWork.Repository<Subscription>().AddAsync(subscription, cancellationToken);
            await _auditLog.StageAsync(AuditAction.Create, nameof(Subscription), subscription.Id.ToString(), cancellationToken: cancellationToken);
            try
            {
                await _unitOfWork.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException ex) when (ex.InnerException?.Message.Contains(
                "ix_subscriptions_child_id_package_plan_id", StringComparison.Ordinal) == true)
            {
                // Backstop for the ExistsAsync check above, which is check-then-insert with no
                // locking: two concurrent requests for the same child+plan can both pass it
                // before either commits. The DB's own unique index (SubscriptionConfiguration)
                // is what actually prevents the duplicate row; translate its rare violation into
                // the same clean error the common-case check already gives the caller. The filter
                // keeps unrelated save failures (dropped connection, FK violation, concurrency
                // conflict) from being misreported as this specific conflict.
                throw new ConflictException("This child already has an active subscription on that plan.");
            }

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
                        CourseId = plan.CourseId,
                        DepartmentId = await DepartmentIdForPlanAsync(plan, cancellationToken),
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

            // Reactivating is an Active-subscription *creation* as far as the one-active-per
            // child+plan rule goes, so it needs the same duplicate check CreateSubscriptionAsync
            // makes. A cancelled subscription whose child has since been re-subscribed to the
            // same plan is exactly this case, and without the check the only thing stopping the
            // second Active row is ix_subscriptions_child_id_package_plan_id — whose violation
            // escapes as a raw DbUpdateException (a 500), not the 409 the admin should see.
            var alreadyActive = await _unitOfWork.Repository<Subscription>().ExistsAsync(
                s => s.Id != id
                    && s.ChildId == subscription.ChildId
                    && s.PackagePlanId == subscription.PackagePlanId
                    && s.Status == SubscriptionStatus.Active,
                cancellationToken);
            if (alreadyActive)
            {
                throw new ConflictException("This child already has an active subscription on that plan.");
            }

            subscription.Status = SubscriptionStatus.Active;
            subscription.CancelledAtUtc = null;
            subscription.NextBillingAtUtc = NextBillingFrom(DateTime.UtcNow, plan.BillingCycle);
            // Same reasoning as NextBillingAtUtc above: renewal restarts the clock from now, not
            // the original StartDate -- without this a validity-days plan's stale, already-past
            // EndDate would leave the just-renewed subscription immediately re-expirable by the
            // next sweep.
            subscription.EndDate = plan.ValidityDays.HasValue
                ? DateOnly.FromDateTime(DateTime.UtcNow).AddDays(plan.ValidityDays.Value)
                : null;
            _unitOfWork.Repository<Subscription>().Update(subscription);

            // Renewal conversion is tracked in the audit trail for the renewal-rate report
            await _auditLog.StageAsync(AuditAction.Update, nameof(Subscription), subscription.Id.ToString(),
                changesJson: "{\"renewed\":true}", cancellationToken: cancellationToken);
            try
            {
                await _unitOfWork.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException ex) when (ex.InnerException?.Message.Contains(
                "ix_subscriptions_child_id_package_plan_id", StringComparison.Ordinal) == true)
            {
                // Same check-then-write race CreateSubscriptionAsync guards: the ExistsAsync
                // above takes no lock, so a concurrent create/renew for this child+plan can
                // commit between it and this save. Translate the index's violation into the
                // same clean conflict rather than letting it surface as a 500.
                throw new ConflictException("This child already has an active subscription on that plan.");
            }

            // Renewal bills right away, same as a fresh start — the next cycle is the job's.
            await CreateInvoiceAsync(
                new CreateInvoiceRequest
                {
                    ParentProfileId = subscription.ParentProfileId,
                    ChildId = subscription.ChildId,
                    SubscriptionId = subscription.Id,
                    CourseId = plan.CourseId,
                    DepartmentId = await DepartmentIdForPlanAsync(plan, cancellationToken),
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

            // A cancelled subscription stops billing going forward, but its already-issued,
            // still-unsettled invoices were otherwise left Pending/Overdue forever — nothing
            // else ever revisits them once the subscription they belong to is gone, so the
            // parent kept seeing a payable balance for a plan they'd already cancelled.
            var openInvoiceIds = await _unitOfWork.Repository<Invoice>().Query()
                .Where(i => i.SubscriptionId == subscription.Id
                    && i.Status != InvoiceStatus.Paid
                    && i.Status != InvoiceStatus.Cancelled)
                .Select(i => i.Id)
                .ToListAsync(cancellationToken);
            foreach (var openInvoiceId in openInvoiceIds)
            {
                // Load each one tracked (Query() above is AsNoTracking) so the mutation persists.
                var openInvoice = await _unitOfWork.Repository<Invoice>().GetByIdAsync(openInvoiceId, cancellationToken)
                    ?? throw new NotFoundException(nameof(Invoice), openInvoiceId);
                openInvoice.Status = InvoiceStatus.Cancelled;
                _unitOfWork.Repository<Invoice>().Update(openInvoice);
                await _auditLog.StageAsync(AuditAction.Update, nameof(Invoice), openInvoice.Id.ToString(),
                    changesJson: "{\"reason\":\"subscription_cancelled\"}", cancellationToken: cancellationToken);
            }

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
        private async Task<Guid> DepartmentIdForPlanAsync(PackagePlan plan, CancellationToken cancellationToken)
        {
            if (plan.CourseId is null)
            {
                return WellKnownDepartments.Phonics;
            }

            var course = await _unitOfWork.Repository<Course>().GetByIdAsync(plan.CourseId.Value, cancellationToken);
            return course?.DepartmentId ?? WellKnownDepartments.Phonics;
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
                ChildName = $"{subscription.Child.FirstName} {subscription.Child.LastName}".Trim(),
                PackagePlanId = subscription.PackagePlanId,
                PlanName = subscription.PackagePlan.Name,
                Status = subscription.Status,
                StartDate = subscription.StartDate,
                EndDate = subscription.EndDate,
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
