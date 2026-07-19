using iucs.readernest.application.Common.Exceptions;
using iucs.readernest.application.Dto.Academics;
using iucs.readernest.application.Dto.Auth;
using iucs.readernest.application.Dto.Batches;
using iucs.readernest.application.Dto.Billing;
using iucs.readernest.application.Dto.Courses;
using iucs.readernest.application.Dto.Enrollment;
using iucs.readernest.application.Dto.Payouts;
using iucs.readernest.application.Dto.Sessions;
using iucs.readernest.application.Dto.Users;
using iucs.readernest.application.Helper;
using iucs.readernest.application.Services;
using iucs.readernest.domain.Entities.Academics;
using iucs.readernest.domain.Entities.Billing;
using iucs.readernest.domain.Entities.Sessions;
using iucs.readernest.domain.Entities.Users;
using iucs.readernest.domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace iucs.readernest.tests
{
    public class SmokeTests : IDisposable
    {
        private readonly TestDatabase _db = new();
        private readonly BcryptPasswordHasher _hasher = new();
        private readonly FakeEmailSender _emailSender = new();
        private readonly AuditLogService _auditLog;
        private readonly NotificationService _notifications;

        public SmokeTests()
        {
            _auditLog = new AuditLogService(_db.UnitOfWork, _db.CurrentUser);
            _notifications = new NotificationService(_db.UnitOfWork, _emailSender, NullLogger<NotificationService>.Instance);
        }

        private AuthService CreateAuthService() => new(_db.UnitOfWork, _hasher, new FakeTokenService(), _auditLog);

        private readonly FakeWhatsAppSender _whatsAppSender = new();

        private readonly FakeSmsSender _smsSender = new();

        private UserService CreateUserService() => new(_db.UnitOfWork, _hasher, _notifications, _auditLog, _emailSender, _whatsAppSender, _smsSender);

        private CourseService CreateCourseService() => new(_db.UnitOfWork, _auditLog);

        private BatchService CreateBatchService() => new(_db.UnitOfWork, _auditLog);

        private PayoutService CreatePayoutService() => new(_db.UnitOfWork, _auditLog, _notifications);

        private SessionService CreateSessionService() => new(_db.UnitOfWork, _auditLog, CreatePayoutService(), _notifications, _db.CurrentUser);

        private BillingService CreateBillingService() => new(_db.UnitOfWork, _auditLog, new FakePaymentGateway(), _notifications);

        private BillingService CreateBillingService(FakePaymentGateway gateway) => new(_db.UnitOfWork, _auditLog, gateway, _notifications);

        private EnrollmentService CreateEnrollmentService() => new(_db.UnitOfWork, _auditLog, CreateBillingService());

        private MenuService CreateMenuService() => new(_db.UnitOfWork, _auditLog);

        private AcademicOpsService CreateAcademicOpsService() => new(_db.UnitOfWork, _auditLog, _notifications);

        private GamificationService CreateGamificationService() => new(_db.UnitOfWork);

        // ---- WBS business-rule coverage (Reader_Nest_LMS.pdf pp.28–32) ----

        [Fact]
        public async Task TeacherNoShow_AppliesDeduction_AndCarriesForward()
        {
            var (_, _, session) = await SeedBatchWithSessionAsync(totalSessions: 2);
            await CreatePayoutService().SetRateAsync(new SavePayoutRateRequest
            {
                TeacherProfileId = session.TeacherProfileId,
                DurationMinutes = 45,
                RatePerSession = 1000,
                EffectiveFrom = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-30)),
            });

            var carried = await CreateSessionService().MarkNoShowAsync(
                session.Id, new MarkNoShowRequest { Party = NoShowParty.Teacher });

            var original = await _db.Context.ClassSessions.FindAsync(session.Id);
            Assert.Equal(SessionStatus.TeacherNoShow, original!.Status);
            Assert.Equal(SessionStatus.CarriedForward, (await _db.Context.ClassSessions.FindAsync(carried.Id))!.Status);
            var item = Assert.Single(_db.Context.PayoutItems.ToList());
            Assert.Equal(PayoutItemType.TeacherNoShowDeduction, item.Type);
            Assert.Equal(-1000m, item.Amount); // default penalty: 100% of the session rate
        }

        [Fact]
        public async Task TeacherNoShow_AppliesConfiguredPenaltyPercent()
        {
            var (_, _, session) = await SeedBatchWithSessionAsync(totalSessions: 2);
            await CreatePayoutService().SetRateAsync(new SavePayoutRateRequest
            {
                TeacherProfileId = session.TeacherProfileId,
                DurationMinutes = 45,
                RatePerSession = 1000,
                TeacherNoShowPenaltyPercent = 150, // WBS p.31 "Penalty configuration"
                EffectiveFrom = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-30)),
            });

            await CreateSessionService().MarkNoShowAsync(
                session.Id, new MarkNoShowRequest { Party = NoShowParty.Teacher });

            var item = Assert.Single(_db.Context.PayoutItems.ToList());
            Assert.Equal(PayoutItemType.TeacherNoShowDeduction, item.Type);
            Assert.Equal(-1500m, item.Amount);
            Assert.Contains("150% of session rate", item.Note);
        }

        [Fact]
        public async Task DefaultRateCard_PaysTeachersWithoutOwnRates_AndTeacherRateOverridesIt()
        {
            var (batch, _, session) = await SeedBatchWithSessionAsync(totalSessions: 3);
            var payoutService = CreatePayoutService();

            // Only the centre-wide default card exists (TeacherProfileId = null)
            await payoutService.SetRateAsync(new SavePayoutRateRequest
            {
                TeacherProfileId = null,
                DurationMinutes = 45,
                RatePerSession = 800,
                EffectiveFrom = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-30)),
            });

            await CreateSessionService().CompleteAsync(session.Id);
            var defaultPaid = Assert.Single(_db.Context.PayoutItems.ToList());
            Assert.Equal(PayoutItemType.SessionEarning, defaultPaid.Type);
            Assert.Equal(800m, defaultPaid.Amount); // paid from the default card

            // The teacher's own rate takes precedence over the default from then on
            await payoutService.SetRateAsync(new SavePayoutRateRequest
            {
                TeacherProfileId = session.TeacherProfileId,
                DurationMinutes = 45,
                RatePerSession = 1200,
                EffectiveFrom = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-30)),
            });

            var secondStart = session.ScheduledStartAtUtc.AddDays(1);
            var second = new ClassSession
            {
                BatchId = batch.Id,
                TeacherProfileId = session.TeacherProfileId,
                Status = SessionStatus.Scheduled,
                ScheduledStartAtUtc = secondStart,
                ScheduledEndAtUtc = secondStart.AddMinutes(45),
            };
            _db.Context.ClassSessions.Add(second);
            await _db.Context.SaveChangesAsync();

            await CreateSessionService().CompleteAsync(second.Id);
            var overridden = _db.Context.PayoutItems.Single(i => i.ClassSessionId == second.Id);
            Assert.Equal(1200m, overridden.Amount);
        }

        [Fact]
        public async Task SubmitLeave_WithinSixHoursOfClass_IsBlocked()
        {
            var (_, _, session) = await SeedBatchWithSessionAsync(totalSessions: 1, includeSession: false);
            var teacher = await _db.Context.TeacherProfiles.FirstAsync();
            // A class starting in 2 hours — inside the 6-hour cutoff.
            var soon = DateTime.UtcNow.AddHours(2);
            _db.Context.ClassSessions.Add(new ClassSession
            {
                BatchId = (await _db.Context.Batches.FirstAsync()).Id,
                TeacherProfileId = teacher.Id,
                Status = SessionStatus.Scheduled,
                ScheduledStartAtUtc = soon,
                ScheduledEndAtUtc = soon.AddMinutes(45),
            });
            await _db.Context.SaveChangesAsync();

            await Assert.ThrowsAsync<DomainValidationException>(() =>
                CreateAcademicOpsService().SubmitLeaveAsync(teacher.UserId, new SubmitLeaveRequest
                {
                    StartAtUtc = soon.AddMinutes(-30),
                    EndAtUtc = soon.AddHours(1),
                    Reason = "Sick",
                }));
        }

        [Fact]
        public async Task SubmitLeave_BeyondSixHours_Succeeds_AndAdminCanReject()
        {
            var (_, _, _) = await SeedBatchWithSessionAsync(totalSessions: 1, includeSession: false);
            var teacher = await _db.Context.TeacherProfiles.FirstAsync();
            var ops = CreateAcademicOpsService();

            // Leave well beyond the 6-hour cutoff and clear of any class.
            var leave = await ops.SubmitLeaveAsync(teacher.UserId, new SubmitLeaveRequest
            {
                StartAtUtc = DateTime.UtcNow.AddDays(10),
                EndAtUtc = DateTime.UtcNow.AddDays(10).AddHours(2),
                Reason = "Family event",
            });
            Assert.Equal(LeaveStatus.Pending, leave.Status);

            // Simulate a fresh request/scope so the review re-loads cleanly (per-request context in prod).
            _db.Context.ChangeTracker.Clear();

            var reviewed = await ops.ReviewLeaveAsync(leave.Id, new ReviewLeaveRequest { Approve = false, ReviewNote = "Clash" });
            Assert.Equal(LeaveStatus.Rejected, reviewed.Status);
        }

        [Fact]
        public async Task CaptureAttendance_Rejoin_UpdatesRow_NeverDuplicates()
        {
            var (_, _, session) = await SeedBatchWithSessionAsync(totalSessions: 1);
            var teacher = await _db.Context.TeacherProfiles.FirstAsync();
            var ops = CreateAcademicOpsService();

            await ops.CaptureAttendanceAsync(session.Id, new CaptureAttendanceRequest
            {
                Entries = [new AttendanceEntryDto { TeacherProfileId = teacher.Id, Status = AttendanceStatus.Present }],
            });
            // A network drop + rejoin sends the same participant again.
            await ops.CaptureAttendanceAsync(session.Id, new CaptureAttendanceRequest
            {
                Entries = [new AttendanceEntryDto { TeacherProfileId = teacher.Id, Status = AttendanceStatus.Late }],
            });

            var rows = _db.Context.SessionAttendances.Where(a => a.ClassSessionId == session.Id).ToList();
            Assert.Single(rows);
            Assert.Equal(AttendanceStatus.Late, rows[0].Status);
        }

        [Fact]
        public async Task CaptureAttendance_RejectsEntryWithBothChildAndTeacher()
        {
            var (_, _, session) = await SeedBatchWithSessionAsync(totalSessions: 1);
            var teacher = await _db.Context.TeacherProfiles.FirstAsync();

            await Assert.ThrowsAsync<DomainValidationException>(() =>
                CreateAcademicOpsService().CaptureAttendanceAsync(session.Id, new CaptureAttendanceRequest
                {
                    Entries = [new AttendanceEntryDto { ChildId = Guid.NewGuid(), TeacherProfileId = teacher.Id, Status = AttendanceStatus.Present }],
                }));
        }

        [Fact]
        public async Task AddRecording_SetsFifteenDayParentExpiry()
        {
            var (_, _, session) = await SeedBatchWithSessionAsync(totalSessions: 1);

            var recording = await CreateSessionService().AddRecordingAsync(session.Id, new RegisterRecordingRequest
            {
                StorageUrl = "https://cdn.test/rec.mp4",
                DurationSeconds = 2700,
            });

            var stored = await _db.Context.SessionRecordings.FindAsync(recording.Id);
            Assert.NotNull(stored!.ExpiresAtUtc);
            var days = (stored.ExpiresAtUtc!.Value - DateTime.UtcNow).TotalDays;
            Assert.InRange(days, 14.9, 15.1);
        }

        [Fact]
        public async Task CreateInvoice_RoutesToMatchingDepartmentAccount_ByDefault()
        {
            var parentUser = await _db.SeedUserAsync($"dept-{Guid.NewGuid():N}@test.com", "x", UserRole.Parent);
            var parentProfile = new ParentProfile { UserId = parentUser.Id };
            var phonics = new PaymentAccount { Name = "Phonics", Department = Department.Phonics, GatewayProvider = "razorpay", GatewayAccountRef = "ph" };
            var maths = new PaymentAccount { Name = "Maths", Department = Department.Maths, GatewayProvider = "cashfree", GatewayAccountRef = "ma" };
            _db.Context.AddRange(parentProfile, phonics, maths);
            await _db.Context.SaveChangesAsync();

            var invoice = await CreateBillingService().CreateInvoiceAsync(new CreateInvoiceRequest
            {
                ParentProfileId = parentProfile.Id,
                Department = Department.Maths,
                Amount = 500,
                DueDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(7)),
            });

            var stored = await _db.Context.Invoices.FirstAsync(i => i.Id == invoice.Id);
            Assert.Equal(maths.Id, stored.PaymentAccountId); // Maths course → Maths account
        }

        [Fact]
        public async Task PartialThenFullPayment_TransitionsInvoiceStatus()
        {
            var parentUser = await _db.SeedUserAsync($"part-{Guid.NewGuid():N}@test.com", "x", UserRole.Parent);
            var parentProfile = new ParentProfile { UserId = parentUser.Id };
            _db.Context.AddRange(parentProfile,
                new PaymentAccount { Name = "P", Department = Department.Phonics, GatewayProvider = "t", GatewayAccountRef = "p" });
            await _db.Context.SaveChangesAsync();
            var billing = CreateBillingService();
            var invoice = await billing.CreateInvoiceAsync(new CreateInvoiceRequest
            {
                ParentProfileId = parentProfile.Id, Department = Department.Phonics, Amount = 1000,
                DueDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(7)),
            });

            var partial = await billing.RecordPaymentAsync(invoice.Id, new RecordPaymentRequest { Amount = 400 });
            Assert.Equal(InvoiceStatus.PartiallyPaid, partial.Status);

            var full = await billing.RecordPaymentAsync(invoice.Id, new RecordPaymentRequest { Amount = 600 });
            Assert.Equal(InvoiceStatus.Paid, full.Status);
        }

        [Fact]
        public async Task InlineCheckout_SettlesOnlyWithVerifiedSignature()
        {
            var parentUser = await _db.SeedUserAsync($"inline-{Guid.NewGuid():N}@test.com", "x", UserRole.Parent);
            var parentProfile = new ParentProfile { UserId = parentUser.Id };
            _db.Context.AddRange(parentProfile,
                new PaymentAccount { Name = "P", Department = Department.Phonics, GatewayProvider = "razorpay", GatewayAccountRef = "p" });
            await _db.Context.SaveChangesAsync();
            var billing = CreateBillingService();
            var invoice = await billing.CreateInvoiceAsync(new CreateInvoiceRequest
            {
                ParentProfileId = parentProfile.Id, Department = Department.Phonics, Amount = 1000,
                DueDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(7)),
            });

            var checkout = await billing.StartParentInlineCheckoutAsync(
                parentUser.Id, invoice.Id, new InitiateParentPaymentRequest { MethodKey = "razorpay" });
            Assert.Equal("inline", checkout.Mode);
            Assert.NotNull(checkout.OrderId);
            Assert.Equal(100_000, checkout.Amount); // minor units: 1000.00 → paise

            // A forged/failed signature must not settle the invoice.
            await Assert.ThrowsAsync<DomainValidationException>(() => billing.VerifyParentInlineCheckoutAsync(
                parentUser.Id, invoice.Id,
                new VerifyInlineCheckoutRequest { OrderId = checkout.OrderId!, PaymentId = "pay_1", Signature = "forged" }));
            Assert.Equal(InvoiceStatus.Pending, (await _db.Context.Invoices.FindAsync(invoice.Id))!.Status);

            // An order belonging to a different invoice must not settle this one either.
            await Assert.ThrowsAsync<NotFoundException>(() => billing.VerifyParentInlineCheckoutAsync(
                parentUser.Id, invoice.Id,
                new VerifyInlineCheckoutRequest { OrderId = "order_someone_elses", PaymentId = "pay_1", Signature = "valid" }));

            var settled = await billing.VerifyParentInlineCheckoutAsync(
                parentUser.Id, invoice.Id,
                new VerifyInlineCheckoutRequest { OrderId = checkout.OrderId!, PaymentId = "pay_1", Signature = "valid" });
            Assert.Equal(InvoiceStatus.Paid, settled.Status);
        }

        [Fact]
        public async Task FullPayment_AutoLiftsActiveFeeSuspension()
        {
            var parentUser = await _db.SeedUserAsync($"susp-{Guid.NewGuid():N}@test.com", "x", UserRole.Parent);
            var parentProfile = new ParentProfile { UserId = parentUser.Id };
            _db.Context.AddRange(parentProfile,
                new PaymentAccount { Name = "P", Department = Department.Phonics, GatewayProvider = "t", GatewayAccountRef = "p" });
            await _db.Context.SaveChangesAsync();
            var billing = CreateBillingService();
            var invoice = await billing.CreateInvoiceAsync(new CreateInvoiceRequest
            {
                ParentProfileId = parentProfile.Id, Department = Department.Phonics, Amount = 800,
                DueDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1)),
            });
            _db.Context.FeeSuspensions.Add(new FeeSuspension
            {
                ParentProfileId = parentProfile.Id, InvoiceId = invoice.Id,
                Status = SuspensionStatus.Active, SuspendedAtUtc = DateTime.UtcNow,
            });
            await _db.Context.SaveChangesAsync();

            await billing.RecordPaymentAsync(invoice.Id, new RecordPaymentRequest { Amount = 800 });

            var suspension = await _db.Context.FeeSuspensions.FirstAsync(s => s.ParentProfileId == parentProfile.Id);
            Assert.Equal(SuspensionStatus.Lifted, suspension.Status);
            Assert.True(suspension.AutoRestored);
        }

        [Fact]
        public async Task Refund_RequestThenApprove_IsRecorded()
        {
            var parentUser = await _db.SeedUserAsync($"ref-{Guid.NewGuid():N}@test.com", "x", UserRole.Parent);
            var parentProfile = new ParentProfile { UserId = parentUser.Id };
            _db.Context.AddRange(parentProfile,
                new PaymentAccount { Name = "P", Department = Department.Phonics, GatewayProvider = "t", GatewayAccountRef = "p" });
            await _db.Context.SaveChangesAsync();
            var billing = CreateBillingService();
            var invoice = await billing.CreateInvoiceAsync(new CreateInvoiceRequest
            {
                ParentProfileId = parentProfile.Id, Department = Department.Phonics, Amount = 1000,
                DueDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(7)),
            });
            await billing.RecordPaymentAsync(invoice.Id, new RecordPaymentRequest { Amount = 1000 });
            var txn = await _db.Context.PaymentTransactions.FirstAsync();

            var refund = await billing.RequestRefundAsync(new RequestRefundRequest
            {
                PaymentTransactionId = txn.Id, Amount = 250, Reason = "Partial goodwill",
            });
            var reviewed = await billing.ReviewRefundAsync(refund.Id, new ReviewRefundRequest { Approve = true });

            Assert.Equal(RefundStatus.Processed, reviewed.Status);
            Assert.Equal(250, reviewed.Amount);
            // Persistence check (the AsNoTracking-mutation bug this caught): re-read from the DB.
            Assert.Equal(RefundStatus.Processed, (await _db.Context.Refunds.FirstAsync(r => r.Id == refund.Id)).Status);
        }

        [Fact]
        public async Task RenewSubscription_ReactivatesLapsedSubscription()
        {
            var parentUser = await _db.SeedUserAsync($"sub-{Guid.NewGuid():N}@test.com", "x", UserRole.Parent);
            var parentProfile = new ParentProfile { UserId = parentUser.Id };
            var plan = new PackagePlan { Name = "Monthly", BillingType = BillingType.Subscription, BillingCycle = BillingCycle.Monthly, Price = 2000 };
            // Starting/renewing a subscription now issues its first invoice immediately,
            // which routes through the department's payment account.
            var account = new PaymentAccount { Name = "Phonics", Department = Department.Phonics, GatewayProvider = "simulated", GatewayAccountRef = "ph" };
            _db.Context.AddRange(parentProfile, plan, account);
            await _db.Context.SaveChangesAsync();
            var child = new Child { ParentProfileId = parentProfile.Id, FirstName = "Kid", LastName = "One", IsActive = true };
            _db.Context.Children.Add(child);
            await _db.Context.SaveChangesAsync();
            var billing = CreateBillingService();
            var sub = await billing.CreateSubscriptionAsync(new CreateSubscriptionRequest
            {
                ParentProfileId = parentProfile.Id, ChildId = child.Id, PackagePlanId = plan.Id,
                StartDate = DateOnly.FromDateTime(DateTime.UtcNow),
            });
            await billing.CancelSubscriptionAsync(sub.Id);

            var renewed = await billing.RenewSubscriptionAsync(sub.Id);
            Assert.Equal(SubscriptionStatus.Active, renewed.Status);
        }

        [Fact]
        public async Task ScheduleSession_OnHoliday_IsBlocked()
        {
            var (batch, _, _) = await SeedBatchWithSessionAsync(totalSessions: 1, includeSession: false);
            var teacher = await _db.Context.TeacherProfiles.FirstAsync();
            var holiday = new DateOnly(2026, 8, 15);
            _db.Context.Holidays.Add(new Holiday { Name = "Independence Day", Date = holiday });
            await _db.Context.SaveChangesAsync();

            var start = holiday.ToDateTime(new TimeOnly(10, 0), DateTimeKind.Utc);
            await Assert.ThrowsAsync<DomainValidationException>(() =>
                CreateSessionService().ScheduleAsync(new ScheduleSessionRequest
                {
                    BatchId = batch.Id,
                    TeacherProfileId = teacher.Id,
                    Type = SessionType.Regular,
                    ScheduledStartAtUtc = start,
                    ScheduledEndAtUtc = start.AddMinutes(45),
                }));
        }

        [Fact]
        public async Task CreateHoliday_CarriesForwardClashingSessions()
        {
            var (_, _, session) = await SeedBatchWithSessionAsync(totalSessions: 2);
            var holidayDate = DateOnly.FromDateTime(session.ScheduledStartAtUtc);
            _db.Context.ChangeTracker.Clear();

            await CreateAcademicOpsService().CreateHolidayAsync(new SaveHolidayRequest
            {
                Name = "Surprise Holiday",
                Date = holidayDate,
            });

            var original = await _db.Context.ClassSessions.FirstAsync(s => s.Id == session.Id);
            Assert.Equal(SessionStatus.Cancelled, original.Status); // freed from the holiday
            var carried = await _db.Context.ClassSessions
                .FirstAsync(s => s.CarriedForwardFromSessionId == session.Id);
            Assert.Equal(SessionStatus.CarriedForward, carried.Status);
            Assert.Equal(session.ScheduledStartAtUtc.AddDays(7), carried.ScheduledStartAtUtc); // next available week
        }

        [Fact]
        public async Task FinalizeAndMarkPaid_PersistStatus_AndEmailSalarySlip()
        {
            var (_, _, session) = await SeedBatchWithSessionAsync(totalSessions: 2);
            var payoutService = CreatePayoutService();
            await payoutService.SetRateAsync(new SavePayoutRateRequest
            {
                TeacherProfileId = session.TeacherProfileId,
                DurationMinutes = 45,
                RatePerSession = 900,
                EffectiveFrom = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-30)),
            });
            await CreateSessionService().CompleteAsync(session.Id); // accrues the earning
            var payout = await _db.Context.Payouts.FirstAsync();
            _db.Context.ChangeTracker.Clear(); // fresh request scope

            await payoutService.FinalizeAsync(payout.Id);
            _db.Context.ChangeTracker.Clear();

            // Persistence check (the AsNoTracking-mutation bug this caught): re-read from the DB.
            var finalized = await _db.Context.Payouts.FirstAsync(p => p.Id == payout.Id);
            Assert.Equal(PayoutStatus.Finalized, finalized.Status);
            Assert.Equal(900, finalized.TotalAmount);
            _db.Context.ChangeTracker.Clear();

            await payoutService.MarkPaidAsync(payout.Id);
            _db.Context.ChangeTracker.Clear();
            var paid = await _db.Context.Payouts.FirstAsync(p => p.Id == payout.Id);
            Assert.Equal(PayoutStatus.Paid, paid.Status);

            // Salary slip auto-emailed on payment processing (client feedback #5)
            Assert.Contains(_emailSender.Sent, m => m.Subject.Contains("Salary slip"));
        }

        [Fact]
        public async Task ApproveLeave_Persists_AndNotifiesCoreTeamAndAffectedParents()
        {
            var (batch, _, _) = await SeedBatchWithSessionAsync(totalSessions: 1, includeSession: false);
            var teacher = await _db.Context.TeacherProfiles.FirstAsync();

            // A core-team RM + a parent with an actively enrolled child in the teacher's batch
            await _db.SeedUserAsync($"rm-{Guid.NewGuid():N}@test.com", "x", UserRole.SubAdmin);
            var parentUser = await _db.SeedUserAsync($"lp-{Guid.NewGuid():N}@test.com", "x", UserRole.Parent);
            var parentProfile = new ParentProfile { UserId = parentUser.Id };
            var child = new Child { ParentProfile = parentProfile, FirstName = "Kid", LastName = "L", IsActive = true };
            _db.Context.AddRange(parentProfile, child,
                new BatchEnrollment { BatchId = batch.Id, Child = child, Status = EnrollmentStatus.Active });
            await _db.Context.SaveChangesAsync();

            var ops = CreateAcademicOpsService();
            var leave = await ops.SubmitLeaveAsync(teacher.UserId, new SubmitLeaveRequest
            {
                StartAtUtc = DateTime.UtcNow.AddDays(12),
                EndAtUtc = DateTime.UtcNow.AddDays(12).AddHours(3),
                Reason = "Conference",
            });
            _db.Context.ChangeTracker.Clear();
            _emailSender.Sent.Clear();

            await ops.ReviewLeaveAsync(leave.Id, new ReviewLeaveRequest { Approve = true });
            _db.Context.ChangeTracker.Clear();

            // Persistence check (the AsNoTracking-mutation bug this caught)
            var stored = await _db.Context.LeaveRequests.FirstAsync(l => l.Id == leave.Id);
            Assert.Equal(LeaveStatus.Approved, stored.Status);

            // Client feedback #10: core team + affected parents are notified
            Assert.Contains(_emailSender.Sent, m => m.Subject.StartsWith("Teacher on leave"));
            Assert.Contains(_emailSender.Sent, m => m.To == parentUser.Email && m.Subject.StartsWith("Class update"));
        }

        [Fact]
        public async Task Gamification_StarGrant_AutoAwardsMilestone_AtThreshold()
        {
            var gamification = CreateGamificationService();
            // A real session id — StudentAward.ClassSessionId is a FK.
            var (_, _, session) = await SeedBatchWithSessionAsync(totalSessions: 1);
            var sessionId = session.Id;

            await gamification.GrantAsync(new GrantAwardRequest { SessionId = sessionId, ParticipantName = "Aarav", Points = 2 });
            var afterTwo = await gamification.GetLeaderboardAsync(sessionId, 10);
            Assert.Equal(2, afterTwo.Single().Stars);
            Assert.Empty(afterTwo.Single().Badges);

            // Crossing 3 stars auto-grants the "Rising Star" milestone.
            var granted = await gamification.GrantAsync(new GrantAwardRequest { SessionId = sessionId, ParticipantName = "Aarav", Points = 1 });
            Assert.Contains(granted, a => a.Kind == AwardKind.Milestone);

            var afterThree = await gamification.GetLeaderboardAsync(sessionId, 10);
            Assert.Equal(3, afterThree.Single().Stars);
            Assert.NotEmpty(afterThree.Single().Badges);
        }

        [Fact]
        public async Task Menu_ForUser_FiltersItemsByRolePermission()
        {
            var subAdmin = await _db.SeedUserAsync($"menu-{Guid.NewGuid():N}@test.com", "x", UserRole.SubAdmin);
            _db.Context.MenuItems.AddRange(
                new domain.Entities.Navigation.MenuItem
                {
                    Portal = "subadmin", Label = "Dashboard", Path = "/subadmin", Icon = "LayoutDashboard",
                    SectionOrder = 0, SortOrder = 0, IsActive = true, RequiredModule = null,
                },
                new domain.Entities.Navigation.MenuItem
                {
                    Portal = "subadmin", Label = "Billing", Path = "/subadmin/billing", Icon = "Receipt",
                    SectionOrder = 1, SortOrder = 0, IsActive = true, RequiredModule = PermissionModule.BillingFinance,
                });
            await _db.Context.SaveChangesAsync();

            var service = CreateMenuService();

            // Role grants no BillingFinance view → the gated item is hidden, the ungated one stays.
            var withoutBilling = await service.GetForUserAsync(subAdmin.Id, UserRole.SubAdmin, []);
            Assert.Contains(withoutBilling, m => m.Path == "/subadmin");
            Assert.DoesNotContain(withoutBilling, m => m.Path == "/subadmin/billing");

            // Grant BillingFinance view → the gated item appears.
            var withBilling = await service.GetForUserAsync(subAdmin.Id, UserRole.SubAdmin, [PermissionModule.BillingFinance]);
            Assert.Contains(withBilling, m => m.Path == "/subadmin/billing");

            // Admin bypasses the gate entirely.
            var adminUser = await _db.SeedUserAsync($"menuadmin-{Guid.NewGuid():N}@test.com", "x", UserRole.Admin);
            _db.Context.MenuItems.Add(new domain.Entities.Navigation.MenuItem
            {
                Portal = "admin", Label = "Billing", Path = "/admin/billing", Icon = "Receipt",
                SectionOrder = 0, SortOrder = 0, IsActive = true, RequiredModule = PermissionModule.BillingFinance,
            });
            await _db.Context.SaveChangesAsync();
            var adminMenu = await service.GetForUserAsync(adminUser.Id, UserRole.Admin, []);
            Assert.Contains(adminMenu, m => m.Path == "/admin/billing");
        }

        [Fact]
        public async Task Login_Succeeds_WithValidCredentials()
        {
            await _db.SeedUserAsync("admin@test.com", _hasher.Hash("Secret@123"), UserRole.Admin);

            var response = await CreateAuthService().LoginAsync(
                new LoginRequest { Email = "admin@test.com", Password = "Secret@123" });

            Assert.Equal("test-token", response.AccessToken);
            Assert.Equal(UserRole.Admin, response.User.Role);
        }

        [Fact]
        public async Task Login_Fails_WithWrongPassword()
        {
            await _db.SeedUserAsync("admin@test.com", _hasher.Hash("Secret@123"), UserRole.Admin);

            await Assert.ThrowsAsync<UnauthorizedException>(() =>
                CreateAuthService().LoginAsync(new LoginRequest { Email = "admin@test.com", Password = "nope" }));
        }

        [Fact]
        public async Task Login_Blocks_InactiveUser()
        {
            await _db.SeedUserAsync("gone@test.com", _hasher.Hash("Secret@123"), status: UserStatus.Inactive);

            await Assert.ThrowsAsync<UnauthorizedException>(() =>
                CreateAuthService().LoginAsync(new LoginRequest { Email = "gone@test.com", Password = "Secret@123" }));
        }

        [Fact]
        public async Task CreateUser_Parent_CreatesProfile_AndEmailsCredentials()
        {
            var dto = await CreateUserService().CreateAsync(new CreateUserRequest
            {
                Email = "Parent@Example.com",
                FirstName = "Rhea",
                LastName = "Kapoor",
                Role = UserRole.Parent,
            });

            Assert.Equal("parent@example.com", dto.Email);
            Assert.Single(_db.Context.ParentProfiles);
            var email = Assert.Single(_emailSender.Sent);
            Assert.Contains("Temporary password", email.Body);
        }

        [Fact]
        public async Task CreateUser_DuplicateEmail_Throws()
        {
            var service = CreateUserService();
            var request = new CreateUserRequest
            {
                Email = "dup@test.com",
                FirstName = "A",
                LastName = "B",
                Role = UserRole.Teacher,
            };
            await service.CreateAsync(request);

            await Assert.ThrowsAsync<ConflictException>(() => service.CreateAsync(request));
        }

        [Fact]
        public async Task CreateCourse_RejectsInvalidDuration()
        {
            var courseService = CreateCourseService();
            var category = await courseService.CreateCategoryAsync(
                new CreateCourseCategoryRequest { Name = "Phonics", Department = Department.Phonics });

            await Assert.ThrowsAsync<DomainValidationException>(() => courseService.CreateAsync(new SaveCourseRequest
            {
                CourseCategoryId = category.Id,
                Name = "Bad",
                Type = CourseType.Group,
                DurationMinutes = 50,
                Price = 1,
                TotalSessions = 1,
                Department = Department.Phonics,
            }));
        }

        [Fact]
        public async Task Reschedule_LinksReplacementToOriginal_AndMarksOriginal()
        {
            var (_, _, session) = await SeedBatchWithSessionAsync(totalSessions: 2);
            var sessionService = CreateSessionService();

            var replacement = await sessionService.RescheduleAsync(session.Id, new RescheduleSessionRequest
            {
                ScheduledStartAtUtc = session.ScheduledStartAtUtc.AddDays(1),
                ScheduledEndAtUtc = session.ScheduledEndAtUtc.AddDays(1),
            });

            Assert.Equal(session.Id, replacement.RescheduledFromSessionId);
            var original = await _db.Context.ClassSessions.FindAsync(session.Id);
            Assert.Equal(SessionStatus.Rescheduled, original!.Status);
        }

        [Fact]
        public async Task CompleteSession_MovesBatchToDormant_WhenCourseFinishes()
        {
            var (batch, _, session) = await SeedBatchWithSessionAsync(totalSessions: 1);

            await CreateSessionService().CompleteAsync(session.Id);

            var reloaded = await _db.Context.Batches.FindAsync(batch.Id);
            Assert.Equal(BatchStatus.Dormant, reloaded!.Status);
            Assert.NotNull(reloaded.CompletedAtUtc);
        }

        [Fact]
        public async Task RecordPayment_MarksInvoicePaid_AndGeneratesReceipt()
        {
            var parentUser = await _db.SeedUserAsync("p@test.com", "x", UserRole.Parent);
            var parentProfile = new ParentProfile { UserId = parentUser.Id };
            _db.Context.ParentProfiles.Add(parentProfile);
            _db.Context.PaymentAccounts.Add(new PaymentAccount
            {
                Name = "Phonics",
                Department = Department.Phonics,
                GatewayProvider = "test",
                GatewayAccountRef = "acc-1",
            });
            await _db.Context.SaveChangesAsync();

            var billing = CreateBillingService();
            var invoice = await billing.CreateInvoiceAsync(new CreateInvoiceRequest
            {
                ParentProfileId = parentProfile.Id,
                Department = Department.Phonics,
                Amount = 1000,
                DueDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(7)),
            });

            var paid = await billing.RecordPaymentAsync(invoice.Id, new RecordPaymentRequest { Amount = 1000 });

            Assert.Equal(InvoiceStatus.Paid, paid.Status);
            var transaction = Assert.Single(_db.Context.PaymentTransactions.ToList());
            Assert.StartsWith("RCP-", transaction.ReceiptNumber);
        }

        [Fact]
        public async Task CreateInvoice_RoutesThroughParentAccountOverride_WhenSet()
        {
            var parentUser = await _db.SeedUserAsync($"map-{Guid.NewGuid():N}@test.com", "x", UserRole.Parent);
            var phonics = new PaymentAccount { Name = "Phonics", Department = Department.Phonics, GatewayProvider = "t", GatewayAccountRef = "ph" };
            var maths = new PaymentAccount { Name = "Maths", Department = Department.Maths, GatewayProvider = "t", GatewayAccountRef = "ma" };
            // Parent is pinned to the Maths account even though the invoice is a Phonics one.
            var parentProfile = new ParentProfile { UserId = parentUser.Id, PaymentAccount = maths };
            _db.Context.AddRange(phonics, maths, parentProfile);
            await _db.Context.SaveChangesAsync();

            var invoice = await CreateBillingService().CreateInvoiceAsync(new CreateInvoiceRequest
            {
                ParentProfileId = parentProfile.Id,
                Department = Department.Phonics,
                Amount = 500,
                DueDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(7)),
            });

            var stored = await _db.Context.Invoices.FirstAsync(i => i.Id == invoice.Id);
            Assert.Equal(maths.Id, stored.PaymentAccountId); // override wins over the department account
            Assert.Equal(Department.Phonics, stored.Department); // department still reflects the course
        }

        [Fact]
        public async Task CreatePaymentLink_ReturnsShareableUrl_ForOpenInvoice()
        {
            var parentUser = await _db.SeedUserAsync("link@test.com", "x", UserRole.Parent);
            var parentProfile = new ParentProfile { UserId = parentUser.Id };
            _db.Context.ParentProfiles.Add(parentProfile);
            _db.Context.PaymentAccounts.Add(new PaymentAccount
            {
                Name = "Maths",
                Department = Department.Maths,
                GatewayProvider = "test",
                GatewayAccountRef = "acc-2",
            });
            await _db.Context.SaveChangesAsync();

            var billing = CreateBillingService();
            var invoice = await billing.CreateInvoiceAsync(new CreateInvoiceRequest
            {
                ParentProfileId = parentProfile.Id,
                Department = Department.Maths,
                Amount = 2500,
                DueDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(7)),
            });

            var link = await billing.CreatePaymentLinkAsync(invoice.Id);

            Assert.Contains(invoice.Id.ToString(), link.Url);
            Assert.Equal(2500, link.AmountDue);
            Assert.StartsWith("TEST-", link.GatewayReference);
        }

        [Fact]
        public async Task ParentPayNow_GatewayCheckout_SettlesViaWebhook_Idempotently()
        {
            var parentUser = await _db.SeedUserAsync($"paynow-{Guid.NewGuid():N}@test.com", "x", UserRole.Parent);
            var parentProfile = new ParentProfile { UserId = parentUser.Id };
            _db.Context.ParentProfiles.Add(parentProfile);
            _db.Context.PaymentAccounts.Add(new PaymentAccount
            {
                Name = "Phonics",
                Department = Department.Phonics,
                GatewayProvider = "razorpay",
                GatewayAccountRef = "acc-1",
            });
            await _db.Context.SaveChangesAsync();

            var billing = CreateBillingService();
            var invoice = await billing.CreateInvoiceAsync(new CreateInvoiceRequest
            {
                ParentProfileId = parentProfile.Id,
                Department = Department.Phonics,
                Amount = 800,
                DueDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(7)),
            });

            // Parent initiates checkout: a pending transaction carries the link reference
            var result = await billing.InitiateParentPaymentAsync(
                parentUser.Id, invoice.Id, new InitiateParentPaymentRequest { MethodKey = "razorpay" });

            Assert.Equal("redirect", result.Mode);
            Assert.NotNull(result.Url);
            var pending = await _db.Context.PaymentTransactions
                .SingleAsync(t => t.GatewayTransactionId == result.GatewayReference);
            Assert.Equal(TransactionStatus.Pending, pending.Status);

            // Webhook settles the reference; a retry of the same event is a no-op
            await billing.SettleGatewayTransactionAsync(result.GatewayReference!, true, "pay_123", null);
            await billing.SettleGatewayTransactionAsync(result.GatewayReference!, true, "pay_123", null);

            var storedInvoice = await _db.Context.Invoices.FirstAsync(i => i.Id == invoice.Id);
            Assert.Equal(InvoiceStatus.Paid, storedInvoice.Status);
            Assert.Equal(800, storedInvoice.AmountPaid);
            var settled = await _db.Context.PaymentTransactions.SingleAsync(t => t.InvoiceId == invoice.Id);
            Assert.Equal(TransactionStatus.Success, settled.Status);
            Assert.StartsWith("RCP-", settled.ReceiptNumber);
            Assert.Contains("pay_123", settled.GatewayTransactionId);
        }

        [Fact]
        public async Task ReconcileInvoicePayment_SettlesFromGatewayStatus_WithoutWebhook()
        {
            var parentUser = await _db.SeedUserAsync($"reconcile-{Guid.NewGuid():N}@test.com", "x", UserRole.Parent);
            var parentProfile = new ParentProfile { UserId = parentUser.Id };
            _db.Context.ParentProfiles.Add(parentProfile);
            _db.Context.PaymentAccounts.Add(new PaymentAccount
            {
                Name = "Phonics",
                Department = Department.Phonics,
                GatewayProvider = "razorpay",
                GatewayAccountRef = "acc-1",
            });
            await _db.Context.SaveChangesAsync();

            var gateway = new FakePaymentGateway();
            var billing = CreateBillingService(gateway);
            var invoice = await billing.CreateInvoiceAsync(new CreateInvoiceRequest
            {
                ParentProfileId = parentProfile.Id,
                Department = Department.Phonics,
                Amount = 950,
                DueDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(7)),
            });

            var result = await billing.InitiateParentPaymentAsync(
                parentUser.Id, invoice.Id, new InitiateParentPaymentRequest { MethodKey = "razorpay" });

            // Before reconcile: the invoice is still unpaid (no webhook arrived).
            var before = await _db.Context.Invoices.FirstAsync(i => i.Id == invoice.Id);
            Assert.NotEqual(InvoiceStatus.Paid, before.Status);

            // The gateway now reports the link paid; a pull-based reconcile settles it.
            gateway.PaidReferences.Add(result.GatewayReference!);
            _db.Context.ChangeTracker.Clear();

            var refreshed = await billing.ReconcileInvoicePaymentAsync(parentUser.Id, invoice.Id);

            Assert.Equal(InvoiceStatus.Paid, refreshed.Status);
            var storedInvoice = await _db.Context.Invoices.FirstAsync(i => i.Id == invoice.Id);
            Assert.Equal(InvoiceStatus.Paid, storedInvoice.Status);
            Assert.Equal(950, storedInvoice.AmountPaid);
            var settled = await _db.Context.PaymentTransactions.SingleAsync(t => t.InvoiceId == invoice.Id);
            Assert.Equal(TransactionStatus.Success, settled.Status);
            Assert.StartsWith("RCP-", settled.ReceiptNumber);
        }

        [Fact]
        public async Task ParentPayNow_Cash_RecordsPendingIntent_WithoutTouchingInvoice()
        {
            var parentUser = await _db.SeedUserAsync($"cash-{Guid.NewGuid():N}@test.com", "x", UserRole.Parent);
            var parentProfile = new ParentProfile { UserId = parentUser.Id };
            _db.Context.ParentProfiles.Add(parentProfile);
            _db.Context.PaymentAccounts.Add(new PaymentAccount
            {
                Name = "Maths",
                Department = Department.Maths,
                GatewayProvider = "cashfree",
                GatewayAccountRef = "acc-2",
            });
            await _db.Context.SaveChangesAsync();

            var billing = CreateBillingService();
            var invoice = await billing.CreateInvoiceAsync(new CreateInvoiceRequest
            {
                ParentProfileId = parentProfile.Id,
                Department = Department.Maths,
                Amount = 1200,
                DueDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(7)),
            });

            var result = await billing.InitiateParentPaymentAsync(
                parentUser.Id, invoice.Id, new InitiateParentPaymentRequest { MethodKey = "cash" });

            Assert.Equal("cash", result.Mode);
            var intent = await _db.Context.PaymentTransactions.SingleAsync(t => t.InvoiceId == invoice.Id);
            Assert.Equal(TransactionStatus.Pending, intent.Status);
            Assert.Equal(PaymentMethod.Cash, intent.Method);
            Assert.Equal(1200, intent.Amount);

            // The invoice only changes once an admin records the collected cash
            var storedInvoice = await _db.Context.Invoices.FirstAsync(i => i.Id == invoice.Id);
            Assert.NotEqual(InvoiceStatus.Paid, storedInvoice.Status);
            Assert.Equal(0, storedInvoice.AmountPaid);
        }

        [Fact]
        public async Task GenerateSchedule_CreatesAllCourseSessions_SkippingHolidays()
        {
            var (batch, _, _) = await SeedBatchWithSessionAsync(totalSessions: 4, includeSession: false);
            _db.Context.Holidays.Add(new Holiday { Name = "Holiday", Date = new DateOnly(2026, 8, 3) });
            await _db.Context.SaveChangesAsync();

            var sessions = await CreateSessionService().GenerateScheduleAsync(batch.Id, new GenerateScheduleRequest
            {
                StartDate = new DateOnly(2026, 8, 3), // a Monday that is a holiday
                DaysOfWeek = [DayOfWeek.Monday],
                StartTimeUtc = new TimeOnly(4, 30),
            });

            Assert.Equal(4, sessions.Count);
            Assert.DoesNotContain(sessions, s => DateOnly.FromDateTime(s.ScheduledStartAtUtc) == new DateOnly(2026, 8, 3));
        }

        [Fact]
        public async Task CompleteSession_AccruesPayoutEarning_AtConfiguredRate()
        {
            var (_, _, session) = await SeedBatchWithSessionAsync(totalSessions: 2);
            await CreatePayoutService().SetRateAsync(new SavePayoutRateRequest
            {
                TeacherProfileId = session.TeacherProfileId,
                DurationMinutes = 45,
                RatePerSession = 1100,
                EffectiveFrom = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-30)),
            });

            await CreateSessionService().CompleteAsync(session.Id);

            var payout = Assert.Single(_db.Context.Payouts.ToList());
            var item = Assert.Single(_db.Context.PayoutItems.ToList());
            Assert.Equal(PayoutItemType.SessionEarning, item.Type);
            Assert.Equal(1100, item.Amount);
            Assert.Equal(1100, payout.TotalAmount);
            Assert.Equal(PayoutStatus.Pending, payout.Status);
        }

        [Fact]
        public async Task StudentNoShow_AddsWaitingAmount_AndCarriesSessionForward()
        {
            var (_, _, session) = await SeedBatchWithSessionAsync(totalSessions: 2);
            await CreatePayoutService().SetRateAsync(new SavePayoutRateRequest
            {
                TeacherProfileId = session.TeacherProfileId,
                DurationMinutes = 45,
                RatePerSession = 1100,
                EffectiveFrom = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-30)),
            });

            var carried = await CreateSessionService().MarkNoShowAsync(
                session.Id, new MarkNoShowRequest { Party = NoShowParty.Student });

            var original = await _db.Context.ClassSessions.FindAsync(session.Id);
            Assert.Equal(SessionStatus.StudentNoShow, original!.Status);
            Assert.Equal(session.ScheduledStartAtUtc.AddDays(7), carried.ScheduledStartAtUtc);
            var item = Assert.Single(_db.Context.PayoutItems.ToList());
            Assert.Equal(PayoutItemType.StudentNoShowWaiting, item.Type);
            Assert.Equal(1100, item.Amount);
        }

        [Fact]
        public async Task RecordEngagement_Allows_AssignedTeacher()
        {
            var (_, _, session) = await SeedBatchWithSessionAsync(totalSessions: 1);
            var teacherProfile = await _db.Context.TeacherProfiles.FindAsync(session.TeacherProfileId);
            _db.CurrentUser.UserId = teacherProfile!.UserId;

            await CreateSessionService().RecordEngagementAsync(session.Id, EngagementRequest());

            Assert.Single(_db.Context.EngagementEvents.ToList());
        }

        [Fact]
        public async Task RecordEngagement_Rejects_UnrelatedTeacher()
        {
            var (_, _, session) = await SeedBatchWithSessionAsync(totalSessions: 1);
            var otherTeacherUser = await _db.SeedUserAsync($"t2-{Guid.NewGuid():N}@test.com", "x", UserRole.Teacher);
            _db.Context.TeacherProfiles.Add(new TeacherProfile { UserId = otherTeacherUser.Id });
            await _db.Context.SaveChangesAsync();
            _db.CurrentUser.UserId = otherTeacherUser.Id;

            await Assert.ThrowsAsync<ForbiddenException>(
                () => CreateSessionService().RecordEngagementAsync(session.Id, EngagementRequest()));
        }

        [Fact]
        public async Task RecordEngagement_Allows_ParentWithChildInBatch()
        {
            var (batch, _, session) = await SeedBatchWithSessionAsync(totalSessions: 1);
            var parentUser = await _db.SeedUserAsync($"p-{Guid.NewGuid():N}@test.com", "x", UserRole.Parent);
            var parentProfile = new ParentProfile { UserId = parentUser.Id };
            var child = new Child { ParentProfile = parentProfile, FirstName = "Kid", LastName = "One" };
            _db.Context.AddRange(parentProfile, child);
            await _db.Context.SaveChangesAsync();
            _db.Context.Add(new BatchEnrollment { BatchId = batch.Id, ChildId = child.Id });
            await _db.Context.SaveChangesAsync();
            _db.CurrentUser.UserId = parentUser.Id;

            await CreateSessionService().RecordEngagementAsync(session.Id, EngagementRequest());

            Assert.Single(_db.Context.EngagementEvents.ToList());
        }

        [Fact]
        public async Task RecordEngagement_Rejects_ParentWithoutChildInBatch()
        {
            var (_, _, session) = await SeedBatchWithSessionAsync(totalSessions: 1);
            var parentUser = await _db.SeedUserAsync($"p2-{Guid.NewGuid():N}@test.com", "x", UserRole.Parent);
            var parentProfile = new ParentProfile { UserId = parentUser.Id };
            var child = new Child { ParentProfile = parentProfile, FirstName = "Kid", LastName = "Two" };
            _db.Context.AddRange(parentProfile, child);
            await _db.Context.SaveChangesAsync();
            _db.CurrentUser.UserId = parentUser.Id;

            await Assert.ThrowsAsync<ForbiddenException>(
                () => CreateSessionService().RecordEngagementAsync(session.Id, EngagementRequest()));
        }

        [Fact]
        public async Task ApproveEnrollment_PersistsStatus_UnlocksParent_AndCreatesChild()
        {
            var parentUser = await _db.SeedUserAsync($"p-{Guid.NewGuid():N}@test.com", "x", UserRole.Parent);
            var parentProfile = new ParentProfile { UserId = parentUser.Id };
            _db.Context.ParentProfiles.Add(parentProfile);
            await _db.Context.SaveChangesAsync();

            var service = CreateEnrollmentService();
            await service.SubmitAsync(parentUser.Id, new SubmitEnrollmentFormRequest { FormDataJson = "{\"childName\":\"Kid One\"}" });
            var formId = (await service.ListAsync(null)).Single().Id;

            var result = await service.ReviewAsync(formId, new ReviewEnrollmentFormRequest
            {
                Approve = true,
                ChildFirstName = "Kid",
                ChildLastName = "One",
            });

            Assert.Equal(EnrollmentFormStatus.Approved, result.Status);
            var refreshedParent = await _db.Context.ParentProfiles.FirstAsync(p => p.Id == parentProfile.Id);
            Assert.True(refreshedParent.EnrollmentFormCompleted);
            Assert.Single(_db.Context.Children.ToList());
        }

        [Fact]
        public async Task ApproveEnrollment_WithPackagePlan_StartsSubscription_AndIssuesFirstInvoice()
        {
            var parentUser = await _db.SeedUserAsync($"p-{Guid.NewGuid():N}@test.com", "x", UserRole.Parent);
            var parentProfile = new ParentProfile { UserId = parentUser.Id };
            var plan = new PackagePlan { Name = "Phonics Monthly", BillingType = BillingType.Subscription, BillingCycle = BillingCycle.Monthly, Price = 2500 };
            _db.Context.AddRange(parentProfile, plan,
                new PaymentAccount { Name = "P", Department = Department.Phonics, GatewayProvider = "t", GatewayAccountRef = "p" });
            await _db.Context.SaveChangesAsync();

            var service = CreateEnrollmentService();
            await service.SubmitAsync(parentUser.Id, new SubmitEnrollmentFormRequest { FormDataJson = "{\"childName\":\"Kid One\"}" });
            var formId = (await service.ListAsync(null)).Single().Id;

            var result = await service.ReviewAsync(formId, new ReviewEnrollmentFormRequest
            {
                Approve = true,
                ChildFirstName = "Kid",
                ChildLastName = "One",
                PackagePlanId = plan.Id,
            });

            Assert.Equal(EnrollmentFormStatus.Approved, result.Status);
            var child = Assert.Single(_db.Context.Children.ToList());
            var subscription = Assert.Single(_db.Context.Subscriptions.ToList());
            Assert.Equal(child.Id, subscription.ChildId);
            Assert.Equal(plan.Id, subscription.PackagePlanId);
            Assert.Equal(SubscriptionStatus.Active, subscription.Status);
            var invoice = Assert.Single(_db.Context.Invoices.ToList());
            Assert.Equal(plan.Price, invoice.Amount);
            Assert.Equal(child.Id, invoice.ChildId);
        }

        [Fact]
        public async Task ApproveEnrollment_WithPlanButNoPaymentAccount_FailsWithoutApproving()
        {
            var parentUser = await _db.SeedUserAsync($"p-{Guid.NewGuid():N}@test.com", "x", UserRole.Parent);
            var parentProfile = new ParentProfile { UserId = parentUser.Id };
            var plan = new PackagePlan { Name = "Unroutable", BillingType = BillingType.Subscription, BillingCycle = BillingCycle.Monthly, Price = 900 };
            _db.Context.AddRange(parentProfile, plan);
            await _db.Context.SaveChangesAsync();

            var service = CreateEnrollmentService();
            await service.SubmitAsync(parentUser.Id, new SubmitEnrollmentFormRequest { FormDataJson = "{\"childName\":\"Kid One\"}" });
            var formId = (await service.ListAsync(null)).Single().Id;

            await Assert.ThrowsAsync<DomainValidationException>(() => service.ReviewAsync(formId, new ReviewEnrollmentFormRequest
            {
                Approve = true,
                ChildFirstName = "Kid",
                PackagePlanId = plan.Id,
            }));

            // The bad billing pick must not leave a half-approved form behind.
            Assert.Equal(EnrollmentFormStatus.Submitted, (await service.GetAsync(formId)).Status);
            Assert.Empty(_db.Context.Children.ToList());
            Assert.Empty(_db.Context.Subscriptions.ToList());
        }

        [Fact]
        public async Task UpdateEnrollmentForm_PersistsEditedAnswers_AndRejectsApprovedForms()
        {
            var parentUser = await _db.SeedUserAsync($"p-{Guid.NewGuid():N}@test.com", "x", UserRole.Parent);
            _db.Context.ParentProfiles.Add(new ParentProfile { UserId = parentUser.Id });
            await _db.Context.SaveChangesAsync();

            var service = CreateEnrollmentService();
            await service.SubmitAsync(parentUser.Id, new SubmitEnrollmentFormRequest { FormDataJson = "{\"childName\":\"Old Name\",\"grade\":\"1\"}" });
            var formId = (await service.ListAsync(null)).Single().Id;

            var edited = await service.UpdateFormDataAsync(formId, new SubmitEnrollmentFormRequest
            {
                FormDataJson = "{\"childName\":\"New Name\",\"grade\":\"2\"}",
            });
            Assert.Contains("New Name", edited.FormDataJson);
            var reloaded = await service.GetAsync(formId);
            Assert.Contains("New Name", reloaded.FormDataJson);

            // Once approved, the form is immutable.
            await service.ReviewAsync(formId, new ReviewEnrollmentFormRequest { Approve = true, ChildFirstName = "New", ChildLastName = "Name" });
            await Assert.ThrowsAsync<ConflictException>(
                () => service.UpdateFormDataAsync(formId, new SubmitEnrollmentFormRequest { FormDataJson = "{\"childName\":\"Later\"}" }));
        }

        private static RecordEngagementRequest EngagementRequest() => new()
        {
            Events = [new EngagementEntryDto { ParticipantName = "Tester", Type = EngagementEventType.HandRaise }],
        };

        private async Task<(Batch Batch, Course Course, ClassSession Session)> SeedBatchWithSessionAsync(
            int totalSessions,
            bool includeSession = true)
        {
            var teacherUser = await _db.SeedUserAsync($"t-{Guid.NewGuid():N}@test.com", "x", UserRole.Teacher);
            var teacher = new TeacherProfile { UserId = teacherUser.Id };
            var category = new CourseCategory { Name = $"Cat-{Guid.NewGuid():N}", Department = Department.Phonics };
            var course = new Course
            {
                CourseCategory = category,
                Name = "Course",
                Type = CourseType.Group,
                DurationMinutes = 45,
                Price = 100,
                TotalSessions = totalSessions,
                Department = Department.Phonics,
            };
            var batch = new Batch { Course = course, TeacherProfile = teacher, Name = "Batch", Capacity = 5 };
            _db.Context.AddRange(teacher, category, course, batch);

            ClassSession session = null!;
            if (includeSession)
            {
                session = new ClassSession
                {
                    Batch = batch,
                    TeacherProfile = teacher,
                    ScheduledStartAtUtc = DateTime.UtcNow.AddDays(1),
                    ScheduledEndAtUtc = DateTime.UtcNow.AddDays(1).AddMinutes(45),
                };
                _db.Context.Add(session);
            }

            await _db.Context.SaveChangesAsync();
            return (batch, course, session);
        }

        public void Dispose() => _db.Dispose();
    }
}
