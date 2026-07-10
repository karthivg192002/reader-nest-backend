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

            if (dueSubscriptions.Count > 0 || overdueInvoices.Count > 0)
            {
                await unitOfWork.SaveChangesAsync(cancellationToken);
                _logger.LogInformation(
                    "Auto billing: generated {InvoiceCount} invoice(s), marked {OverdueCount} overdue.",
                    dueSubscriptions.Count, overdueInvoices.Count);
            }
        }
    }
}
