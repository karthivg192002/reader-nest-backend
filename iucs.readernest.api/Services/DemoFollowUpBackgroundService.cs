using iucs.readernest.application.Common;
using iucs.readernest.application.Common.Interfaces;
using iucs.readernest.application.Helper;
using iucs.readernest.application.Services;
using iucs.readernest.domain.Entities.Admission;
using iucs.readernest.domain.Enums;
using iucs.readernest.domain.Repository;
using Microsoft.EntityFrameworkCore;

namespace iucs.readernest.api.Services
{
    /// <summary>
    /// Automatic demo follow-up emails (client, 2026-10-16): a parent whose child attended a demo
    /// but hasn't enrolled is asked whether they'd like to enroll or are still considering --
    /// <see cref="FirstEmailAfter"/> after the demo, then every <see cref="RepeatEvery"/> -- and
    /// it stops by itself once the lead is Enrolled (or marked Not interested, or is already in
    /// the payment/enrollment steps). Settings → Notifications → "Demo follow-up emails" turns it
    /// off; the wording is the "demo-follow-up" email template. Runs once a day.
    /// </summary>
    public class DemoFollowUpBackgroundService : BackgroundService
    {
        private static readonly TimeSpan Interval = TimeSpan.FromHours(1);
        public static readonly TimeSpan FirstEmailAfter = TimeSpan.FromDays(2);
        public static readonly TimeSpan RepeatEvery = TimeSpan.FromDays(15);
        private static readonly TimeSpan RepeatTolerance = TimeSpan.FromHours(3);

        /// <summary>A demo older than this gets no first email -- no blast to every old lead on day one.</summary>
        public static readonly TimeSpan StartWindow = TimeSpan.FromDays(60);

        private static readonly ConversionStatus[] Stopped =
        [
            ConversionStatus.Enrolled,
            ConversionStatus.NotInterested,
            ConversionStatus.ReadyForEnrollment,
            ConversionStatus.PaymentReceived,
        ];

        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<DemoFollowUpBackgroundService> _logger;

        public DemoFollowUpBackgroundService(IServiceScopeFactory scopeFactory, ILogger<DemoFollowUpBackgroundService> logger)
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
                    // 05:00 UTC = 10:30 AM IST, a sensible time for a family email.
                    if (DateTime.UtcNow.Hour == 5)
                    {
                        using var scope = _scopeFactory.CreateScope();
                        await RunCycleAsync(scope.ServiceProvider, DateTime.UtcNow, stoppingToken);
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogError(ex, "Demo follow-up cycle failed; retrying next interval.");
                }

                await Task.Delay(Interval, stoppingToken);
            }
        }

        /// <summary>One pass: emails every lead that is due. Returns how many were sent.</summary>
        public static async Task<int> RunCycleAsync(IServiceProvider services, DateTime nowUtc, CancellationToken cancellationToken)
        {
            var unitOfWork = services.GetRequiredService<IUnitOfWork>();
            if (!await NotificationToggles.IsEnabledAsync(unitOfWork, NotificationToggles.DemoFollowUps, cancellationToken))
            {
                return 0;
            }

            var templates = services.GetRequiredService<IEmailTemplateService>();
            var sender = services.GetRequiredService<IEmailSender>();
            var logger = services.GetService<ILogger<DemoFollowUpBackgroundService>>();

            var firstDueBefore = nowUtc - FirstEmailAfter;
            var oldestFirst = nowUtc - StartWindow;
            // A few hours of slack: the daily run lands at a slightly different minute each day, so an
            // exact 15 x 24h comparison would push some parents to day 16.
            var repeatDueBefore = nowUtc - RepeatEvery + RepeatTolerance;

            var due = await unitOfWork.Repository<DemoBooking>().TrackedQuery()
                .Include(b => b.ClassSession)
                .Where(b => b.ClassSession != null
                    && b.ClassSession.Status == SessionStatus.Completed
                    && !Stopped.Contains(b.ConversionStatus)
                    && ((b.LastFollowUpEmailAtUtc == null
                            && b.ClassSession.ScheduledEndAtUtc <= firstDueBefore
                            && b.ClassSession.ScheduledEndAtUtc >= oldestFirst)
                        || (b.LastFollowUpEmailAtUtc != null && b.LastFollowUpEmailAtUtc <= repeatDueBefore)))
                .ToListAsync(cancellationToken);

            var sent = 0;
            foreach (var booking in due)
            {
                if (!ParentLogin.IsDeliverable(booking.ParentEmail))
                {
                    continue; // a phone-only lead has no real email address
                }

                try
                {
                    var (subject, html) = await templates.RenderAsync(
                        "demo-follow-up",
                        new Dictionary<string, string>
                        {
                            ["ParentName"] = booking.ParentName,
                            ["ChildName"] = booking.ChildName,
                            ["DemoDate"] = DateTimeDisplay.ToLocalDate(booking.ClassSession!.ScheduledStartAtUtc, "dd MMM yyyy"),
                        },
                        cancellationToken);
                    await sender.SendAsync(booking.ParentEmail, subject, html, cancellationToken);
                    booking.FollowUpEmailCount++;
                    booking.LastFollowUpEmailAtUtc = nowUtc;
                    await unitOfWork.SaveChangesAsync(cancellationToken);
                    sent++;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // One bad address must not stop everyone else's email.
                    logger?.LogWarning(ex, "Demo follow-up email failed for booking {BookingId}; continuing.", booking.Id);
                }
            }

            if (sent > 0)
            {
                logger?.LogInformation("Demo follow-up: sent {Count} email(s).", sent);
            }

            return sent;
        }
    }
}
