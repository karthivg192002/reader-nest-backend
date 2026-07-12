using iucs.readernest.application.Dto.Billing;
using iucs.readernest.application.Services;
using iucs.readernest.domain.Entities.Billing;
using iucs.readernest.domain.Enums;
using iucs.readernest.domain.Repository;
using Microsoft.EntityFrameworkCore;

namespace iucs.readernest.api.Services
{
    /// <summary>
    /// Auto billing: on an hourly cycle, generates the next invoice for every active
    /// subscription whose billing date has arrived, advances its next-billing pointer
    /// by the plan's cycle, and flags unpaid invoices past their due date as Overdue.
    /// </summary>
    public class BillingBackgroundService : BackgroundService
    {
        private static readonly TimeSpan Interval = TimeSpan.FromHours(1);

        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<BillingBackgroundService> _logger;

        public BillingBackgroundService(IServiceScopeFactory scopeFactory, ILogger<BillingBackgroundService> logger)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await RunCycleAsync(stoppingToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogError(ex, "Auto billing cycle failed; retrying next interval.");
                }

                await Task.Delay(Interval, stoppingToken);
            }
        }

        private async Task RunCycleAsync(CancellationToken cancellationToken)
        {
            using var scope = _scopeFactory.CreateScope();
            var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            var billingService = scope.ServiceProvider.GetRequiredService<IBillingService>();

            var now = DateTime.UtcNow;

            var dueSubscriptions = await unitOfWork.Repository<Subscription>().Query()
                .Include(s => s.PackagePlan).ThenInclude(p => p.Course)
                .Where(s => s.Status == SubscriptionStatus.Active
                            && s.NextBillingAtUtc != null
                            && s.NextBillingAtUtc <= now)
                .ToListAsync(cancellationToken);

            foreach (var subscription in dueSubscriptions)
            {
                await billingService.CreateInvoiceAsync(
                    new CreateInvoiceRequest
                    {
                        ParentProfileId = subscription.ParentProfileId,
                        ChildId = subscription.ChildId,
                        SubscriptionId = subscription.Id,
                        // Route to the department's payment account (dual-gateway requirement);
                        // plans without a course default to Phonics
                        Department = subscription.PackagePlan.Course?.Department ?? Department.Phonics,
                        Amount = subscription.PackagePlan.Price,
                        DueDate = DateOnly.FromDateTime(now.AddDays(7)),
                    },
                    cancellationToken);

                subscription.NextBillingAtUtc = subscription.PackagePlan.BillingCycle switch
                {
                    BillingCycle.Monthly => subscription.NextBillingAtUtc!.Value.AddMonths(1),
                    BillingCycle.Quarterly => subscription.NextBillingAtUtc!.Value.AddMonths(3),
                    BillingCycle.Yearly => subscription.NextBillingAtUtc!.Value.AddYears(1),
                    _ => null, // one-time plans bill once
                };
            }

            // Fee overdue check: unpaid invoices past their due date turn Overdue
            var today = DateOnly.FromDateTime(now);
            var overdueInvoices = await unitOfWork.Repository<Invoice>().Query()
                .Where(i => (i.Status == InvoiceStatus.Pending || i.Status == InvoiceStatus.PartiallyPaid)
                            && i.DueDate < today)
                .ToListAsync(cancellationToken);

            foreach (var invoice in overdueInvoices)
            {
                invoice.Status = InvoiceStatus.Overdue;
            }

            // Account suspension: any parent left with an overdue invoice and no active
            // suspension gets one; the parent portal blocks sessions/content while Active
            var suspendedCount = 0;
            var overdueParents = await unitOfWork.Repository<Invoice>().Query()
                .Where(i => i.Status == InvoiceStatus.Overdue)
                .Select(i => new { i.ParentProfileId, i.Id })
                .ToListAsync(cancellationToken);
            foreach (var group in overdueParents.GroupBy(o => o.ParentProfileId))
            {
                var hasActive = await unitOfWork.Repository<FeeSuspension>().ExistsAsync(
                    s => s.ParentProfileId == group.Key && s.Status == SuspensionStatus.Active, cancellationToken);
                if (hasActive)
                {
                    continue;
                }

                await unitOfWork.Repository<FeeSuspension>().AddAsync(
                    new FeeSuspension
                    {
                        ParentProfileId = group.Key,
                        InvoiceId = group.First().Id,
                        Reason = "Automatic suspension: invoice overdue.",
                        SuspendedAtUtc = now,
                    },
                    cancellationToken);
                suspendedCount++;
            }

            if (dueSubscriptions.Count > 0 || overdueInvoices.Count > 0 || suspendedCount > 0)
            {
                await unitOfWork.SaveChangesAsync(cancellationToken);
                _logger.LogInformation(
                    "Auto billing: generated {InvoiceCount} invoice(s), marked {OverdueCount} overdue, suspended {SuspendedCount} account(s).",
                    dueSubscriptions.Count, overdueInvoices.Count, suspendedCount);
            }

            // Payment reminders go out once a day (the 08:00 UTC cycle), not every hour
            if (now.Hour == 8)
            {
                await SendPaymentRemindersAsync(scope.ServiceProvider, unitOfWork, today, cancellationToken);
            }
        }

        private static async Task SendPaymentRemindersAsync(
            IServiceProvider services,
            IUnitOfWork unitOfWork,
            DateOnly today,
            CancellationToken cancellationToken)
        {
            var notifications = services.GetRequiredService<INotificationService>();
            var reminderWindow = today.AddDays(3);

            var dueInvoices = await unitOfWork.Repository<Invoice>().Query()
                .Include(i => i.ParentProfile).ThenInclude(p => p.User)
                .Where(i => (i.Status == InvoiceStatus.Pending || i.Status == InvoiceStatus.PartiallyPaid || i.Status == InvoiceStatus.Overdue)
                            && i.DueDate <= reminderWindow)
                .ToListAsync(cancellationToken);

            foreach (var invoice in dueInvoices)
            {
                var user = invoice.ParentProfile.User;
                var outstanding = invoice.Amount - invoice.AmountPaid;
                var subject = invoice.Status == InvoiceStatus.Overdue
                    ? $"Overdue: invoice {invoice.InvoiceNumber}"
                    : $"Payment reminder: invoice {invoice.InvoiceNumber} due {invoice.DueDate:dd MMM}";
                await notifications.SendEmailAsync(
                    user.Id,
                    user.Email,
                    NotificationType.PaymentReminder,
                    subject,
                    $"{outstanding:0.00} {invoice.Currency} is outstanding on invoice {invoice.InvoiceNumber} (due {invoice.DueDate:yyyy-MM-dd}). " +
                    "Use Pay Now on your dashboard to settle it and keep classes uninterrupted.",
                    cancellationToken);
            }
        }
    }
}
