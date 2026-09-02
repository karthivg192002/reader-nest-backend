using iucs.readernest.application.Common;
using iucs.readernest.application.Common.Exceptions;
using iucs.readernest.application.Dto.Academics;
using iucs.readernest.application.Dto.Admission;
using iucs.readernest.application.Dto.Auth;
using iucs.readernest.application.Dto.Batches;
using iucs.readernest.application.Dto.Billing;
using iucs.readernest.application.Dto.Communication;
using iucs.readernest.application.Dto.Courses;
using iucs.readernest.application.Dto.Enrollment;
using iucs.readernest.application.Dto.Integrations;
using iucs.readernest.application.Dto.Navigation;
using iucs.readernest.application.Dto.Payouts;
using iucs.readernest.application.Dto.Resources;
using iucs.readernest.application.Dto.Portal;
using iucs.readernest.application.Dto.Quizzes;
using iucs.readernest.application.Dto.Reports;
using iucs.readernest.application.Dto.Sessions;
using iucs.readernest.application.Dto.Settings;
using iucs.readernest.application.Dto.Users;
using iucs.readernest.application.Helper;
using iucs.readernest.application.Services;
using iucs.readernest.domain.Entities.Academics;
using iucs.readernest.domain.Entities.Admission;
using iucs.readernest.domain.Entities.Auditing;
using iucs.readernest.domain.Entities.Billing;
using iucs.readernest.domain.Entities.Communication;
using iucs.readernest.domain.Entities.Payouts;
using iucs.readernest.domain.Entities.Quizzes;
using iucs.readernest.domain.Entities.Sessions;
using iucs.readernest.domain.Entities.Settings;
using iucs.readernest.domain.Entities.Users;
using iucs.readernest.domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
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
        private readonly EmailTemplateService _emailTemplates;
        private readonly NotificationService _notifications;

        public SmokeTests()
        {
            _auditLog = new AuditLogService(_db.UnitOfWork, _db.CurrentUser);
            _emailTemplates = new EmailTemplateService(_db.UnitOfWork, _auditLog, new MemoryCache(new MemoryCacheOptions()));
            _notifications = new NotificationService(_db.UnitOfWork, _emailSender, _emailTemplates, NullLogger<NotificationService>.Instance);
        }

        private AuthService CreateAuthService() =>
            new(_db.UnitOfWork, _hasher, new FakeTokenService(), _auditLog, _notifications, new ConfigurationBuilder().Build(), CreateMenuService());

        private readonly FakeWhatsAppSender _whatsAppSender = new();

        private readonly FakeSmsSender _smsSender = new();

        private readonly FakeBulkFileReader _bulkFileReader = new();

        private readonly FakeInvoicePdfGenerator _invoicePdfGenerator = new();

        private UserService CreateUserService() => new(_db.UnitOfWork, _hasher, _notifications, _emailTemplates, _auditLog, _emailSender, _whatsAppSender, _smsSender, _bulkFileReader, NullLogger<UserService>.Instance);

        private CourseService CreateCourseService() => new(_db.UnitOfWork, _auditLog, _bulkFileReader);

        private DepartmentService CreateDepartmentService() => new(_db.UnitOfWork, _auditLog, _bulkFileReader);

        private BatchService CreateBatchService() => new(_db.UnitOfWork, _auditLog, _notifications);

        private PayoutService CreatePayoutService() => new(_db.UnitOfWork, _auditLog, _notifications);

        private ProgressReportService CreateProgressReportService() => new(_db.UnitOfWork, _auditLog, _notifications);

        private StoreService CreateStoreService() => new(_db.UnitOfWork, _auditLog, CreateDemoBookingService());

        private SessionService CreateSessionService() => new(_db.UnitOfWork, _auditLog, CreatePayoutService(), _notifications, _db.CurrentUser, new FakeJitsiTokenService());

        private SessionService CreateSessionService(FakeJitsiTokenService jitsiTokens) =>
            new(_db.UnitOfWork, _auditLog, CreatePayoutService(), _notifications, _db.CurrentUser, jitsiTokens);

        private BillingService CreateBillingService() =>
            new(_db.UnitOfWork, _auditLog, new FakePaymentGateway(), _notifications, _db.CurrentUser, _bulkFileReader, _invoicePdfGenerator);

        private BillingService CreateBillingService(FakePaymentGateway gateway) =>
            new(_db.UnitOfWork, _auditLog, gateway, _notifications, _db.CurrentUser, _bulkFileReader, _invoicePdfGenerator);

        private EnrollmentService CreateEnrollmentService() => new(_db.UnitOfWork, _auditLog, CreateBillingService(), CreateBatchService(), _bulkFileReader);

        private MenuService CreateMenuService() => new(_db.UnitOfWork, _auditLog);

        private MenuPermissionService CreateMenuPermissionService() => new(_db.UnitOfWork, _auditLog);

        private AcademicOpsService CreateAcademicOpsService() =>
            new(_db.UnitOfWork, _auditLog, _notifications, _db.CurrentUser, CreateSessionService());

        private GamificationService CreateGamificationService() => new(_db.UnitOfWork, CreateSessionService());

        private ResourceService CreateResourceService() => new(_db.UnitOfWork, _auditLog);

        private DemoBookingService CreateDemoBookingService() =>
            new(_db.UnitOfWork, _auditLog, _emailSender, _emailTemplates, new FakeCrmNotifier(), new FakeJitsiTokenService(), _notifications, CreateUserService(), NullLogger<DemoBookingService>.Instance);

        private QuizQuestionService CreateQuizQuestionService() => new(_db.UnitOfWork, _auditLog, CreateSessionService(), _bulkFileReader);

        private AccessRequestService CreateAccessRequestService() => new(_db.UnitOfWork, _auditLog, _notifications);

        // ---- WBS business-rule coverage (Reader_Nest_LMS.pdf pp.28–32) ----

        [Fact]
        public async Task TeacherNoShow_AppliesDeduction_AndCarriesForward()
        {
            var (_, _, session) = await SeedBatchWithSessionAsync(totalSessions: 2);
            await CreatePayoutService().SetRateAsync(new SavePayoutRateRequest
            {
                TeacherProfileId = session.TeacherProfileId,
                RatePerMinute = 1000,
                EffectiveFrom = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-30)),
            });

            var carried = await CreateSessionService().MarkNoShowAsync(
                session.Id, new MarkNoShowRequest { Party = NoShowParty.Teacher });

            var original = await _db.Context.ClassSessions.FindAsync(session.Id);
            Assert.Equal(SessionStatus.TeacherNoShow, original!.Status);
            Assert.Equal(SessionStatus.CarriedForward, (await _db.Context.ClassSessions.FindAsync(carried.Id))!.Status);
            var item = Assert.Single(_db.Context.PayoutItems.ToList());
            Assert.Equal(PayoutItemType.TeacherNoShowDeduction, item.Type);
            Assert.Equal(-45000m, item.Amount); // default penalty: 100% of the session rate (1000/min * 45 min)
        }

        [Fact]
        public async Task GetJitsiJoin_RejectsAFarFutureOrAlreadyEndedSession_EvenForAValidParticipant()
        {
            // The UI's Join button only disables itself outside the window (parent/utils.ts
            // isJoinable: 10 min before start until the scheduled end) — confirmed live that
            // GET /api/sessions/{id}/jitsi-join itself had no equivalent check, so a real,
            // usable room + token was one direct request away regardless of what the button
            // showed, for a session weeks out or long over.
            var teacherUser = await _db.SeedUserAsync($"t-{Guid.NewGuid():N}@test.com", "x", UserRole.Teacher);
            var teacher = new TeacherProfile { UserId = teacherUser.Id };
            _db.Context.TeacherProfiles.Add(teacher);
            await _db.Context.SaveChangesAsync();

            var service = CreateSessionService();

            var farFuture = new ClassSession
            {
                TeacherProfileId = teacher.Id,
                ScheduledStartAtUtc = DateTime.UtcNow.AddDays(21),
                ScheduledEndAtUtc = DateTime.UtcNow.AddDays(21).AddMinutes(45),
                Status = SessionStatus.Scheduled,
                MeetingRoomId = "trn-far-future",
            };
            var alreadyEnded = new ClassSession
            {
                TeacherProfileId = teacher.Id,
                ScheduledStartAtUtc = DateTime.UtcNow.AddDays(-1),
                ScheduledEndAtUtc = DateTime.UtcNow.AddDays(-1).AddMinutes(45),
                Status = SessionStatus.Scheduled,
                MeetingRoomId = "trn-already-ended",
            };
            var withinWindow = new ClassSession
            {
                TeacherProfileId = teacher.Id,
                ScheduledStartAtUtc = DateTime.UtcNow.AddMinutes(5),
                ScheduledEndAtUtc = DateTime.UtcNow.AddMinutes(50),
                Status = SessionStatus.Scheduled,
                MeetingRoomId = "trn-within-window",
            };
            _db.Context.AddRange(farFuture, alreadyEnded, withinWindow);
            await _db.Context.SaveChangesAsync();

            await Assert.ThrowsAsync<DomainValidationException>(
                () => service.GetJitsiJoinAsync(farFuture.Id, teacherUser.Id));
            await Assert.ThrowsAsync<DomainValidationException>(
                () => service.GetJitsiJoinAsync(alreadyEnded.Id, teacherUser.Id));

            var join = await service.GetJitsiJoinAsync(withinWindow.Id, teacherUser.Id);
            Assert.Equal("trn-within-window", join.Room);
        }

        [Fact]
        public async Task GetJitsiJoin_AllowsACoordinator_ButNotAPlainSubAdminWithoutTheGrant()
        {
            // coordinator/Calendar.tsx documents this as deliberate: "the coordinator can drop
            // into any ongoing/upcoming class or demo" — not scoped to a specific batch/session
            // the way Parent/Teacher access is, since coordinating means being able to check any
            // of them. Gated on the same SessionCalendarManagement:Edit grant the "coordinator"
            // preset carries, not the SubAdmin role generally — a Sub Admin without that specific
            // grant must still be refused.
            var otherTeacherUser = await _db.SeedUserAsync($"t-{Guid.NewGuid():N}@test.com", "x", UserRole.Teacher);
            var teacher = new TeacherProfile { UserId = otherTeacherUser.Id };
            _db.Context.TeacherProfiles.Add(teacher);
            await _db.Context.SaveChangesAsync();

            var session = new ClassSession
            {
                TeacherProfileId = teacher.Id,
                ScheduledStartAtUtc = DateTime.UtcNow.AddMinutes(5),
                ScheduledEndAtUtc = DateTime.UtcNow.AddMinutes(50),
                Status = SessionStatus.Scheduled,
                MeetingRoomId = "trn-coordinator-check",
            };
            _db.Context.ClassSessions.Add(session);

            var coordinator = await _db.SeedUserAsync($"co-{Guid.NewGuid():N}@test.com", "x", UserRole.SubAdmin);
            _db.Context.SubAdminPermissions.Add(new SubAdminPermission
            {
                UserId = coordinator.Id,
                Module = PermissionModule.SessionCalendarManagement,
                CanView = true,
                CanEdit = true,
            });

            var billingOnlySubAdmin = await _db.SeedUserAsync($"sa-{Guid.NewGuid():N}@test.com", "x", UserRole.SubAdmin);
            _db.Context.SubAdminPermissions.Add(new SubAdminPermission
            {
                UserId = billingOnlySubAdmin.Id,
                Module = PermissionModule.BillingFinance,
                CanView = true,
            });
            await _db.Context.SaveChangesAsync();

            var service = CreateSessionService();
            var join = await service.GetJitsiJoinAsync(session.Id, coordinator.Id);
            Assert.Equal("trn-coordinator-check", join.Room);

            await Assert.ThrowsAsync<ForbiddenException>(
                () => service.GetJitsiJoinAsync(session.Id, billingOnlySubAdmin.Id));
        }

        [Fact]
        public async Task TeacherNoShow_AppliesConfiguredPenaltyPercent()
        {
            var (_, _, session) = await SeedBatchWithSessionAsync(totalSessions: 2);
            await CreatePayoutService().SetRateAsync(new SavePayoutRateRequest
            {
                TeacherProfileId = session.TeacherProfileId,
                RatePerMinute = 1000,
                TeacherNoShowPenaltyPercent = 150, // WBS p.31 "Penalty configuration"
                EffectiveFrom = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-30)),
            });

            await CreateSessionService().MarkNoShowAsync(
                session.Id, new MarkNoShowRequest { Party = NoShowParty.Teacher });

            var item = Assert.Single(_db.Context.PayoutItems.ToList());
            Assert.Equal(PayoutItemType.TeacherNoShowDeduction, item.Type);
            Assert.Equal(-67500m, item.Amount); // 1000/min * 45 min * 150%
            Assert.Contains("150% of session rate", item.Note);
        }

        /// <summary>
        /// Caught live, checking edge cases beyond the "attended some but not enough" case: a
        /// session can be marked Completed (by an admin, or the teacher via a direct API call)
        /// with ZERO SessionAttendance ever recorded -- Complete doesn't require having joined
        /// the live classroom hub at all. That is at least as worth a human's attention as
        /// attendance that merely fell short; before this test it silently paid the full rate
        /// with no flag at all, arguably a worse gap than the short-attendance case since here
        /// there is no evidence of attendance to weigh.
        /// </summary>
        [Fact]
        public async Task Complete_FlagsPayoutItemForReview_WhenNoAttendanceWasEverRecorded()
        {
            var (_, _, session) = await SeedBatchWithSessionAsync(totalSessions: 1); // 45-minute session
            await CreatePayoutService().SetRateAsync(new SavePayoutRateRequest
            {
                TeacherProfileId = session.TeacherProfileId,
                RatePerMinute = 1000,
                EffectiveFrom = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-30)),
            });
            // Deliberately no SessionAttendance row at all for this session.

            await CreateSessionService().CompleteAsync(session.Id);

            var item = Assert.Single(_db.Context.PayoutItems.ToList());
            Assert.Equal(PayoutItemType.SessionEarning, item.Type);
            Assert.Equal(45000m, item.Amount); // still the full scheduled-duration rate (1000/min * 45 min) -- no proration
            Assert.True(item.RequiresReview);
            Assert.Contains("No attendance was ever recorded", item.Note);
        }

        /// <summary>
        /// Caught live: a teacher who joins a live class and leaves after a few minutes was
        /// indistinguishable from one who taught the whole thing -- no-show detection only checks
        /// whether the teacher ever joined at all, and the payout amount is computed purely from
        /// the session's scheduled duration. Attendance well short of the scheduled class must
        /// still accrue full pay (no automatic proration -- see PayoutItem.RequiresReview's own
        /// doc comment for why) but flag the item so an admin sees it before finalizing.
        /// </summary>
        [Fact]
        public async Task Complete_FlagsPayoutItemForReview_WhenTeacherAttendedWellUnderScheduledDuration()
        {
            var (_, _, session) = await SeedBatchWithSessionAsync(totalSessions: 1); // 45-minute session
            await CreatePayoutService().SetRateAsync(new SavePayoutRateRequest
            {
                TeacherProfileId = session.TeacherProfileId,
                RatePerMinute = 1000,
                EffectiveFrom = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-30)),
            });
            var joinedAt = DateTime.UtcNow.AddMinutes(-10);
            _db.Context.SessionAttendances.Add(new SessionAttendance
            {
                ClassSessionId = session.Id,
                ParticipantType = ParticipantType.Teacher,
                TeacherProfileId = session.TeacherProfileId,
                Status = AttendanceStatus.Present,
                JoinedAtUtc = joinedAt,
                LeftAtUtc = joinedAt.AddMinutes(10), // 10 of 45 scheduled minutes
            });
            await _db.Context.SaveChangesAsync();

            await CreateSessionService().CompleteAsync(session.Id);

            var item = Assert.Single(_db.Context.PayoutItems.ToList());
            Assert.Equal(PayoutItemType.SessionEarning, item.Type);
            Assert.Equal(45000m, item.Amount); // full scheduled-duration rate (1000/min * 45 min) -- no proration
            Assert.True(item.RequiresReview);
            Assert.Contains("attended only 10 of 45 scheduled minutes", item.Note);
        }

        [Fact]
        public async Task Complete_DoesNotFlag_WhenTeacherAttendedMostOfTheScheduledClass()
        {
            var (_, _, session) = await SeedBatchWithSessionAsync(totalSessions: 1); // 45-minute session
            await CreatePayoutService().SetRateAsync(new SavePayoutRateRequest
            {
                TeacherProfileId = session.TeacherProfileId,
                RatePerMinute = 1000,
                EffectiveFrom = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-30)),
            });
            var joinedAt = DateTime.UtcNow.AddMinutes(-40);
            _db.Context.SessionAttendances.Add(new SessionAttendance
            {
                ClassSessionId = session.Id,
                ParticipantType = ParticipantType.Teacher,
                TeacherProfileId = session.TeacherProfileId,
                Status = AttendanceStatus.Present,
                JoinedAtUtc = joinedAt,
                LeftAtUtc = joinedAt.AddMinutes(40), // 40 of 45 scheduled minutes -- ordinary lateness/early wrap-up
            });
            await _db.Context.SaveChangesAsync();

            await CreateSessionService().CompleteAsync(session.Id);

            var item = Assert.Single(_db.Context.PayoutItems.ToList());
            Assert.False(item.RequiresReview);
        }

        /// <summary>
        /// The 50%/20-minute defaults (PayrollSettings) used to be fixed constants with no way
        /// for a centre to tune either without a code change and redeploy. Proves the actual
        /// AppSetting value is read, not just the fallback: 40 of 45 minutes (89%) sits above the
        /// default 50% threshold (would NOT flag) but below an admin-tightened 90% threshold
        /// (WOULD flag) -- same attendance, different outcome purely from Settings → Payroll.
        /// </summary>
        [Fact]
        public async Task Complete_UsesConfiguredMinAttendancePercent_InsteadOfHardcodedDefault()
        {
            var (_, _, session) = await SeedBatchWithSessionAsync(totalSessions: 1); // 45-minute session
            await CreatePayoutService().SetRateAsync(new SavePayoutRateRequest
            {
                TeacherProfileId = session.TeacherProfileId,
                RatePerMinute = 1000,
                EffectiveFrom = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-30)),
            });
            _db.Context.AppSettings.Add(new AppSetting
            {
                Category = SettingCategory.General,
                Key = PayrollSettings.MinAttendancePercentForReviewKey,
                Value = "90",
            });
            var joinedAt = DateTime.UtcNow.AddMinutes(-40);
            _db.Context.SessionAttendances.Add(new SessionAttendance
            {
                ClassSessionId = session.Id,
                ParticipantType = ParticipantType.Teacher,
                TeacherProfileId = session.TeacherProfileId,
                Status = AttendanceStatus.Present,
                JoinedAtUtc = joinedAt,
                LeftAtUtc = joinedAt.AddMinutes(40), // 40 of 45 = 89%, below a 90% configured threshold
            });
            await _db.Context.SaveChangesAsync();

            await CreateSessionService().CompleteAsync(session.Id);

            var item = Assert.Single(_db.Context.PayoutItems.ToList());
            Assert.True(item.RequiresReview);
        }

        /// <summary>
        /// End-to-end version of CaptureJoinAttendance_TeacherRejoinAfterDrop_...: a brief network
        /// drop and automatic reconnect partway through an otherwise fully-taught class must not
        /// falsely flag the payout, which is exactly what the stale-LeftAtUtc bug did before the
        /// join-capture fix (a rejoin used to bump JoinedAtUtc forward while leaving the old
        /// disconnect's LeftAtUtc in place, producing a leave-before-join row and a negative
        /// "attended minutes" that tripped the review threshold).
        /// </summary>
        [Fact]
        public async Task Complete_DoesNotFlag_WhenTeacherHadABriefReconnectButTaughtTheWholeClass()
        {
            var (_, _, session) = await SeedBatchWithSessionAsync(totalSessions: 1); // 45-minute session
            await CreatePayoutService().SetRateAsync(new SavePayoutRateRequest
            {
                TeacherProfileId = session.TeacherProfileId,
                RatePerMinute = 1000,
                EffectiveFrom = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-30)),
            });
            var teacherProfile = await _db.Context.TeacherProfiles.FindAsync(session.TeacherProfileId);
            var ops = CreateAcademicOpsService();

            // Seeded directly (back-dated), rather than via CaptureJoinAttendanceAsync, which
            // always stamps "now" -- the test needs the class to have genuinely run its full 45
            // scheduled minutes by the time Complete is called below, not collapse to the
            // milliseconds these statements actually take to execute.
            _db.Context.SessionAttendances.Add(new SessionAttendance
            {
                ClassSessionId = session.Id,
                ParticipantType = ParticipantType.Teacher,
                TeacherProfileId = session.TeacherProfileId,
                Status = AttendanceStatus.Present,
                JoinedAtUtc = DateTime.UtcNow.AddMinutes(-45),
            });
            await _db.Context.SaveChangesAsync();

            await ops.CaptureLeaveAttendanceAsync(session.Id, teacherProfile!.UserId); // brief network drop, partway through
            await ops.CaptureJoinAttendanceAsync(session.Id, teacherProfile.UserId); // automatic reconnect moments later

            await CreateSessionService().CompleteAsync(session.Id); // taught essentially the whole class

            var item = Assert.Single(_db.Context.PayoutItems.ToList());
            Assert.False(item.RequiresReview);
            Assert.Equal(45000m, item.Amount); // 1000/min * 45 min
        }

        /// <summary>
        /// A flag nobody can act on is decoration. AdjustItemAsync is the only way to clear
        /// RequiresReview, and FinalizeAsync must refuse while any item is still flagged --
        /// otherwise a shortened class's full pay would go out anyway with nobody ever forced to
        /// look at it.
        /// </summary>
        [Fact]
        public async Task AdjustItemAsync_CorrectsAmountAndClearsReviewFlag_AndFinalizeRefusesUntilThen()
        {
            var (_, _, session) = await SeedBatchWithSessionAsync(totalSessions: 1); // 45-minute session
            var payouts = CreatePayoutService();
            await payouts.SetRateAsync(new SavePayoutRateRequest
            {
                TeacherProfileId = session.TeacherProfileId,
                RatePerMinute = 1000,
                EffectiveFrom = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-30)),
            });
            var joinedAt = DateTime.UtcNow.AddMinutes(-10);
            _db.Context.SessionAttendances.Add(new SessionAttendance
            {
                ClassSessionId = session.Id,
                ParticipantType = ParticipantType.Teacher,
                TeacherProfileId = session.TeacherProfileId,
                Status = AttendanceStatus.Present,
                JoinedAtUtc = joinedAt,
                LeftAtUtc = joinedAt.AddMinutes(10),
            });
            await _db.Context.SaveChangesAsync();
            await CreateSessionService().CompleteAsync(session.Id);

            var payout = await _db.Context.Payouts.AsNoTracking().FirstAsync();
            var flaggedItem = Assert.Single(_db.Context.PayoutItems.ToList());
            Assert.True(flaggedItem.RequiresReview);

            await Assert.ThrowsAsync<DomainValidationException>(() => payouts.FinalizeAsync(payout.Id));

            var adjusted = await payouts.AdjustItemAsync(payout.Id, flaggedItem.Id, new AdjustPayoutItemRequest
            {
                NewAmount = 10000m, // exactly proportional to the 10 of 45 minutes actually taught (45000 * 10/45)
                Reason = "Teacher left after 10 minutes; prorated by hand.",
            });
            Assert.Equal(10000m, adjusted.TotalAmount);
            var adjustedItem = Assert.Single(adjusted.Items);
            Assert.False(adjustedItem.RequiresReview);
            Assert.Contains("Adjusted from 45000.00 to 10000.00", adjustedItem.Note);

            var finalized = await payouts.FinalizeAsync(payout.Id);
            Assert.Equal(10000m, finalized.TotalAmount);
        }

        /// <summary>
        /// TeacherNoShowPenaltyPercent is deliberately allowed above 100% (see the test above),
        /// so a teacher whose only accrued item this period is one heavily-penalized no-show
        /// finalizes to a genuinely negative raw sum. FinalizeAsync must floor the payout's
        /// bottom-line TotalAmount at zero — that exact value is the "Total" token in the
        /// payout-statement email, so an unfloored negative total would read to the teacher as
        /// "you owe us money," never the intent of a deduction. The line item itself stays the
        /// true, unfloored -1500 — only the finalized total is floored, so the detail an admin
        /// or teacher can audit still shows the real math.
        /// </summary>
        [Fact]
        public async Task Payout_Finalize_FloorsAHeavyNoShowPenaltyAtZero_NotNegative()
        {
            var (_, _, session) = await SeedBatchWithSessionAsync(totalSessions: 1);
            var payouts = CreatePayoutService();
            await payouts.SetRateAsync(new SavePayoutRateRequest
            {
                TeacherProfileId = session.TeacherProfileId,
                RatePerMinute = 1000,
                TeacherNoShowPenaltyPercent = 150,
                EffectiveFrom = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-30)),
            });

            await CreateSessionService().MarkNoShowAsync(
                session.Id, new MarkNoShowRequest { Party = NoShowParty.Teacher });

            var item = Assert.Single(_db.Context.PayoutItems.ToList());
            Assert.Equal(-67500m, item.Amount); // the raw, honest line-item deduction (1000/min * 45 min * 150%)

            var payout = await _db.Context.Payouts.AsNoTracking().FirstAsync();
            var finalized = await payouts.FinalizeAsync(payout.Id);

            Assert.Equal(0m, finalized.TotalAmount); // floored, never emailed as a debt
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
                RatePerMinute = 800,
                EffectiveFrom = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-30)),
            });

            await CreateSessionService().CompleteAsync(session.Id);
            var defaultPaid = Assert.Single(_db.Context.PayoutItems.ToList());
            Assert.Equal(PayoutItemType.SessionEarning, defaultPaid.Type);
            Assert.Equal(36000m, defaultPaid.Amount); // paid from the default card (800/min * 45 min)

            // The teacher's own rate takes precedence over the default from then on
            await payoutService.SetRateAsync(new SavePayoutRateRequest
            {
                TeacherProfileId = session.TeacherProfileId,
                RatePerMinute = 1200,
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
            Assert.Equal(54000m, overridden.Amount); // 1200/min * 45 min
        }

        [Fact]
        public async Task CompleteSession_RollsPayoutForward_WhenCurrentAndNextMonthAreBothAlreadyFinalized()
        {
            // Finance can finalize payroll before every session for the month is actually
            // done. This must never permanently block completing a late session — it used to
            // throw here when BOTH the session's own month and the next one were finalized.
            var (_, _, session) = await SeedBatchWithSessionAsync(totalSessions: 1);
            var period = new DateTime(session.ScheduledStartAtUtc.Year, session.ScheduledStartAtUtc.Month, 1, 0, 0, 0, DateTimeKind.Utc);
            var next = period.AddMonths(1);
            _db.Context.Payouts.AddRange(
                new Payout { TeacherProfileId = session.TeacherProfileId, PeriodYear = period.Year, PeriodMonth = period.Month, Status = PayoutStatus.Finalized },
                new Payout { TeacherProfileId = session.TeacherProfileId, PeriodYear = next.Year, PeriodMonth = next.Month, Status = PayoutStatus.Finalized });
            await _db.Context.SaveChangesAsync();

            var completed = await CreateSessionService().CompleteAsync(session.Id); // must not throw

            Assert.Equal(SessionStatus.Completed, completed.Status);
            var item = await _db.Context.PayoutItems.Include(i => i.Payout).FirstAsync(i => i.ClassSessionId == session.Id);
            var rolledTo = period.AddMonths(2);
            Assert.Equal(rolledTo.Year, item.Payout.PeriodYear);
            Assert.Equal(rolledTo.Month, item.Payout.PeriodMonth);
            Assert.Equal(PayoutStatus.Pending, item.Payout.Status); // the new period, still open
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
        public async Task SubmitLeave_OverlappingExistingPendingOrApproved_IsRejected()
        {
            var (_, _, _) = await SeedBatchWithSessionAsync(totalSessions: 1, includeSession: false);
            var teacher = await _db.Context.TeacherProfiles.FirstAsync();
            var ops = CreateAcademicOpsService();

            var start = DateTime.UtcNow.AddDays(10);
            var first = await ops.SubmitLeaveAsync(teacher.UserId, new SubmitLeaveRequest
            {
                StartAtUtc = start,
                EndAtUtc = start.AddHours(2),
                Reason = "First",
            });
            Assert.Equal(LeaveStatus.Pending, first.Status);

            // Overlaps only partially (starts an hour into the first request's window) --
            // still a conflict, not just an exact-match duplicate.
            var ex = await Assert.ThrowsAsync<ConflictException>(() =>
                ops.SubmitLeaveAsync(teacher.UserId, new SubmitLeaveRequest
                {
                    StartAtUtc = start.AddHours(1),
                    EndAtUtc = start.AddHours(3),
                    Reason = "Second, overlapping",
                }));
            Assert.Contains("already have a pending leave request", ex.Message);

            // Approve the first -- still blocks a new overlapping request the same way.
            _db.Context.ChangeTracker.Clear();
            await ops.ReviewLeaveAsync(first.Id, new ReviewLeaveRequest { Approve = true });
            _db.Context.ChangeTracker.Clear();
            await Assert.ThrowsAsync<ConflictException>(() =>
                ops.SubmitLeaveAsync(teacher.UserId, new SubmitLeaveRequest
                {
                    StartAtUtc = start.AddHours(1),
                    EndAtUtc = start.AddHours(3),
                    Reason = "Third, overlapping the now-approved one",
                }));
        }

        [Fact]
        public async Task SubmitLeave_SameWindowAsRejectedOrCancelled_Succeeds()
        {
            var (_, _, _) = await SeedBatchWithSessionAsync(totalSessions: 1, includeSession: false);
            var teacher = await _db.Context.TeacherProfiles.FirstAsync();
            var ops = CreateAcademicOpsService();
            var start = DateTime.UtcNow.AddDays(10);

            var rejected = await ops.SubmitLeaveAsync(teacher.UserId, new SubmitLeaveRequest
            {
                StartAtUtc = start,
                EndAtUtc = start.AddHours(2),
                Reason = "First",
            });
            _db.Context.ChangeTracker.Clear();
            await ops.ReviewLeaveAsync(rejected.Id, new ReviewLeaveRequest { Approve = false, ReviewNote = "No" });

            // Rejected no longer "holds" the window -- the same time can be re-requested.
            _db.Context.ChangeTracker.Clear();
            var resubmitted = await ops.SubmitLeaveAsync(teacher.UserId, new SubmitLeaveRequest
            {
                StartAtUtc = start,
                EndAtUtc = start.AddHours(2),
                Reason = "Trying again",
            });
            Assert.Equal(LeaveStatus.Pending, resubmitted.Status);

            // Cancel that one too -- also no longer holds the window.
            _db.Context.ChangeTracker.Clear();
            await ops.CancelLeaveAsync(teacher.UserId, resubmitted.Id);
            _db.Context.ChangeTracker.Clear();
            var thirdTry = await ops.SubmitLeaveAsync(teacher.UserId, new SubmitLeaveRequest
            {
                StartAtUtc = start,
                EndAtUtc = start.AddHours(2),
                Reason = "Third time's the charm",
            });
            Assert.Equal(LeaveStatus.Pending, thirdTry.Status);
        }

        [Fact]
        public async Task CancelLeave_OwnPendingRequest_Succeeds()
        {
            var (_, _, _) = await SeedBatchWithSessionAsync(totalSessions: 1, includeSession: false);
            var teacher = await _db.Context.TeacherProfiles.FirstAsync();
            var ops = CreateAcademicOpsService();
            var leave = await ops.SubmitLeaveAsync(teacher.UserId, new SubmitLeaveRequest
            {
                StartAtUtc = DateTime.UtcNow.AddDays(10),
                EndAtUtc = DateTime.UtcNow.AddDays(10).AddHours(2),
                Reason = "Changed my mind later",
            });

            _db.Context.ChangeTracker.Clear();
            await ops.CancelLeaveAsync(teacher.UserId, leave.Id);

            var stored = await _db.Context.LeaveRequests.FindAsync(leave.Id);
            Assert.Equal(LeaveStatus.Cancelled, stored!.Status);
        }

        [Fact]
        public async Task CancelLeave_AlreadyReviewed_Throws()
        {
            var (_, _, _) = await SeedBatchWithSessionAsync(totalSessions: 1, includeSession: false);
            var teacher = await _db.Context.TeacherProfiles.FirstAsync();
            var ops = CreateAcademicOpsService();
            var leave = await ops.SubmitLeaveAsync(teacher.UserId, new SubmitLeaveRequest
            {
                StartAtUtc = DateTime.UtcNow.AddDays(10),
                EndAtUtc = DateTime.UtcNow.AddDays(10).AddHours(2),
                Reason = "x",
            });
            _db.Context.ChangeTracker.Clear();
            await ops.ReviewLeaveAsync(leave.Id, new ReviewLeaveRequest { Approve = true });

            _db.Context.ChangeTracker.Clear();
            await Assert.ThrowsAsync<DomainValidationException>(() => ops.CancelLeaveAsync(teacher.UserId, leave.Id));
        }

        [Fact]
        public async Task CancelLeave_AnotherTeachersRequest_ThrowsNotFound()
        {
            var (_, _, _) = await SeedBatchWithSessionAsync(totalSessions: 1, includeSession: false);
            var owner = await _db.Context.TeacherProfiles.FirstAsync();
            var ops = CreateAcademicOpsService();
            var leave = await ops.SubmitLeaveAsync(owner.UserId, new SubmitLeaveRequest
            {
                StartAtUtc = DateTime.UtcNow.AddDays(10),
                EndAtUtc = DateTime.UtcNow.AddDays(10).AddHours(2),
                Reason = "x",
            });

            _db.Context.ChangeTracker.Clear();
            var otherTeacherUser = await _db.SeedUserAsync($"t-{Guid.NewGuid():N}@test.com", "x", UserRole.Teacher);
            _db.Context.TeacherProfiles.Add(new TeacherProfile { UserId = otherTeacherUser.Id });
            await _db.Context.SaveChangesAsync();

            _db.Context.ChangeTracker.Clear();
            await Assert.ThrowsAsync<NotFoundException>(() => ops.CancelLeaveAsync(otherTeacherUser.Id, leave.Id));
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
        public async Task CaptureJoinAttendance_Teacher_RecordsPresentAgainstOwnTeacherProfile()
        {
            var (_, _, session) = await SeedBatchWithSessionAsync(totalSessions: 1);
            var teacherProfile = await _db.Context.TeacherProfiles.FindAsync(session.TeacherProfileId);

            await CreateAcademicOpsService().CaptureJoinAttendanceAsync(session.Id, teacherProfile!.UserId);

            var row = Assert.Single(_db.Context.SessionAttendances.Where(a => a.ClassSessionId == session.Id));
            Assert.Equal(teacherProfile.Id, row.TeacherProfileId);
            Assert.Equal(AttendanceStatus.Present, row.Status);
        }

        /// <summary>
        /// Caught by reasoning through "what if the teacher's network drops mid-class": SignalR's
        /// automatic reconnect calls JoinSession again after a brief drop, which used to
        /// overwrite JoinedAtUtc with the reconnect time while leaving the disconnect's LeftAtUtc
        /// stale -- producing a leave time BEFORE the join time and a negative "attended minutes"
        /// that falsely flagged a teacher who taught almost the whole class for review, purely
        /// because of a network blip. A rejoin must keep the original join time and clear the
        /// now-stale leave time instead.
        /// </summary>
        [Fact]
        public async Task CaptureJoinAttendance_TeacherRejoinAfterDrop_KeepsOriginalJoinTime_AndClearsStaleLeave()
        {
            var (_, _, session) = await SeedBatchWithSessionAsync(totalSessions: 1);
            var teacherProfile = await _db.Context.TeacherProfiles.FindAsync(session.TeacherProfileId);
            var ops = CreateAcademicOpsService();

            await ops.CaptureJoinAttendanceAsync(session.Id, teacherProfile!.UserId);
            var originalJoin = _db.Context.SessionAttendances.Single(a => a.ClassSessionId == session.Id).JoinedAtUtc;

            // Network drop: ClassroomHub.OnDisconnectedAsync writes a real leave time.
            await ops.CaptureLeaveAttendanceAsync(session.Id, teacherProfile.UserId);
            Assert.NotNull(_db.Context.SessionAttendances.AsNoTracking().Single(a => a.ClassSessionId == session.Id).LeftAtUtc);

            // Automatic reconnect: SignalR calls JoinSession again for the same class.
            await ops.CaptureJoinAttendanceAsync(session.Id, teacherProfile.UserId);

            var row = _db.Context.SessionAttendances.AsNoTracking().Single(a => a.ClassSessionId == session.Id);
            Assert.Equal(originalJoin, row.JoinedAtUtc); // not bumped forward by the reconnect
            Assert.Null(row.LeftAtUtc); // no longer looks like they've left
        }

        [Fact]
        public async Task CaptureJoinAttendance_ParentWithEnrolledChild_RecordsPresentAgainstThatChild()
        {
            var (batch, _, session) = await SeedBatchWithSessionAsync(totalSessions: 1);
            var parentUser = await _db.SeedUserAsync($"p-{Guid.NewGuid():N}@test.com", "x", UserRole.Parent);
            var parentProfile = new ParentProfile { UserId = parentUser.Id };
            var child = new Child { ParentProfile = parentProfile, FirstName = "Kid", LastName = "One" };
            _db.Context.AddRange(parentProfile, child);
            await _db.Context.SaveChangesAsync();
            _db.Context.Add(new BatchEnrollment { BatchId = batch.Id, ChildId = child.Id });
            await _db.Context.SaveChangesAsync();

            await CreateAcademicOpsService().CaptureJoinAttendanceAsync(session.Id, parentUser.Id);

            var row = Assert.Single(_db.Context.SessionAttendances.Where(a => a.ClassSessionId == session.Id));
            Assert.Equal(child.Id, row.ChildId);
            Assert.Equal(AttendanceStatus.Present, row.Status);
        }

        [Fact]
        public async Task CaptureJoinAttendance_ParentWithoutEnrolledChildInThisBatch_RecordsNothing()
        {
            var (_, _, session) = await SeedBatchWithSessionAsync(totalSessions: 1);
            var parentUser = await _db.SeedUserAsync($"p2-{Guid.NewGuid():N}@test.com", "x", UserRole.Parent);
            var parentProfile = new ParentProfile { UserId = parentUser.Id };
            var child = new Child { ParentProfile = parentProfile, FirstName = "Kid", LastName = "Two" };
            _db.Context.AddRange(parentProfile, child);
            await _db.Context.SaveChangesAsync();
            // Deliberately no BatchEnrollment for this child in this session's batch.

            await CreateAcademicOpsService().CaptureJoinAttendanceAsync(session.Id, parentUser.Id);

            Assert.Empty(_db.Context.SessionAttendances.Where(a => a.ClassSessionId == session.Id));
        }

        [Fact]
        public async Task CaptureJoinAttendance_UnrelatedTeacher_RecordsNothing_AndNeverThrows()
        {
            var (_, _, session) = await SeedBatchWithSessionAsync(totalSessions: 1);
            var otherTeacherUser = await _db.SeedUserAsync($"t2-{Guid.NewGuid():N}@test.com", "x", UserRole.Teacher);
            _db.Context.TeacherProfiles.Add(new TeacherProfile { UserId = otherTeacherUser.Id });
            await _db.Context.SaveChangesAsync();

            // A teacher who isn't assigned to this session resolves a TeacherProfile, but it
            // doesn't match session.TeacherProfileId, so the entry is never built — and even if
            // it somehow were, CaptureAttendanceCoreAsync has no ownership check to catch it, so
            // this also proves the ownership filtering lives in CaptureJoinAttendanceAsync itself.
            await CreateAcademicOpsService().CaptureJoinAttendanceAsync(session.Id, otherTeacherUser.Id);

            Assert.Empty(_db.Context.SessionAttendances.Where(a => a.ClassSessionId == session.Id));
        }

        /// <summary>Seeds a Demo (no batch) session + its DemoBooking, mirroring how the store
        /// flow and admission team create one — used by the demo-join tests below.</summary>
        private async Task<(ClassSession Session, DemoBooking Booking)> SeedDemoSessionAsync(
            string parentEmail, string? participantEmail = null, DateTime? startAtUtc = null)
        {
            var teacherUser = await _db.SeedUserAsync($"t-{Guid.NewGuid():N}@test.com", "x", UserRole.Teacher);
            var teacher = new TeacherProfile { UserId = teacherUser.Id };
            _db.Context.TeacherProfiles.Add(teacher);
            await _db.Context.SaveChangesAsync();

            var demoStart = startAtUtc ?? DateTime.UtcNow.AddDays(1);
            var session = new ClassSession
            {
                BatchId = null,
                TeacherProfile = teacher,
                Type = SessionType.Demo,
                Status = SessionStatus.Scheduled,
                ScheduledStartAtUtc = demoStart,
                ScheduledEndAtUtc = demoStart.AddMinutes(30),
            };
            _db.Context.ClassSessions.Add(session);

            var booking = new DemoBooking
            {
                ClassSession = session,
                ParentName = "Lead Parent",
                ParentEmail = parentEmail,
                ChildName = "Prospective Kid",
            };
            if (participantEmail is not null)
            {
                booking.Participants.Add(new DemoParticipant { Name = "Invited Guardian", Email = participantEmail });
            }
            _db.Context.DemoBookings.Add(booking);
            await _db.Context.SaveChangesAsync();

            _db.CurrentUser.UserId = teacherUser.Id;
            return (session, booking);
        }

        [Fact]
        public async Task CaptureJoinAttendance_ParentOnDemoSession_PrimaryContactEmailMatch_SetsParentJoinedAtUtc()
        {
            // Regression: a demo lead has no Child/BatchEnrollment row, so the regular
            // batch-based capture branch had nothing to do for a demo session — this parent's
            // join was silently dropped even though they are a registered, signed-in account.
            var parentEmail = $"lead-{Guid.NewGuid():N}@test.com";
            var (session, booking) = await SeedDemoSessionAsync(parentEmail);
            var parentUser = await _db.SeedUserAsync(parentEmail, "x", UserRole.Parent);
            _db.Context.ParentProfiles.Add(new ParentProfile { UserId = parentUser.Id });
            await _db.Context.SaveChangesAsync();

            await CreateAcademicOpsService().CaptureJoinAttendanceAsync(session.Id, parentUser.Id);

            var reloaded = await _db.Context.DemoBookings.FindAsync(booking.Id);
            Assert.NotNull(reloaded!.ParentJoinedAtUtc);
            // No SessionAttendance row is created (there is no Child to attach it to) —
            // the demo join is tracked entirely on the booking itself.
            Assert.Empty(_db.Context.SessionAttendances.Where(a => a.ClassSessionId == session.Id));
        }

        [Fact]
        public async Task CaptureJoinAttendance_ParentOnDemoSession_InvitedParticipantEmailMatch_SetsHasJoined()
        {
            var primaryEmail = $"lead-{Guid.NewGuid():N}@test.com";
            var participantEmail = $"guardian-{Guid.NewGuid():N}@test.com";
            var (session, booking) = await SeedDemoSessionAsync(primaryEmail, participantEmail);
            var participantUser = await _db.SeedUserAsync(participantEmail, "x", UserRole.Parent);
            _db.Context.ParentProfiles.Add(new ParentProfile { UserId = participantUser.Id });
            await _db.Context.SaveChangesAsync();

            await CreateAcademicOpsService().CaptureJoinAttendanceAsync(session.Id, participantUser.Id);

            var reloaded = await _db.Context.DemoBookings.Include(b => b.Participants).FirstAsync(b => b.Id == booking.Id);
            Assert.Null(reloaded.ParentJoinedAtUtc); // primary contact never joined
            Assert.True(Assert.Single(reloaded.Participants).HasJoined);
        }

        [Fact]
        public async Task CaptureJoinAttendance_ParentOnDemoSession_NoMatchingEmail_RecordsNothing_AndNeverThrows()
        {
            var (session, booking) = await SeedDemoSessionAsync($"lead-{Guid.NewGuid():N}@test.com");
            var unrelatedParent = await _db.SeedUserAsync($"other-{Guid.NewGuid():N}@test.com", "x", UserRole.Parent);
            _db.Context.ParentProfiles.Add(new ParentProfile { UserId = unrelatedParent.Id });
            await _db.Context.SaveChangesAsync();

            await CreateAcademicOpsService().CaptureJoinAttendanceAsync(session.Id, unrelatedParent.Id);

            Assert.Null((await _db.Context.DemoBookings.FindAsync(booking.Id))!.ParentJoinedAtUtc);
        }

        [Fact]
        public async Task IsSessionParticipant_ParentOnDemoSession_MatchedByEmail_CanReachTheJitsiJoinEndpoint()
        {
            // The same email match gates the classroom hub's JoinSession/GetJitsiJoin path —
            // without it, a registered parent joining their own demo got "You do not have
            // access to this session" from the hub even though the raw Jitsi call still
            // connected (JitsiLive.tsx's route-state fallback), so the interactive layer
            // (and this attendance capture) never engaged at all.
            var parentEmail = $"lead-{Guid.NewGuid():N}@test.com";
            var (session, _) = await SeedDemoSessionAsync(parentEmail, startAtUtc: DateTime.UtcNow.AddMinutes(5));
            session.MeetingRoomId = "demo-room";
            var parentUser = await _db.SeedUserAsync(parentEmail, "x", UserRole.Parent);
            _db.Context.ParentProfiles.Add(new ParentProfile { UserId = parentUser.Id });
            await _db.Context.SaveChangesAsync();

            var join = await CreateSessionService().GetJitsiJoinAsync(session.Id, parentUser.Id);
            Assert.Equal("demo-room", join.Room);
        }

        [Fact]
        public async Task IsSessionParticipant_ParentWithWithdrawnEnrollment_Rejected()
        {
            // Consistency/security fix: the participant gate used to admit ANY enrollment
            // status for this batch, while attendance capture already required Active — a
            // withdrawn parent could still get into the live room even though their join was
            // never going to be recorded as attendance.
            var (batch, _, session) = await SeedBatchWithSessionAsync(totalSessions: 1);
            var parentUser = await _db.SeedUserAsync($"p-{Guid.NewGuid():N}@test.com", "x", UserRole.Parent);
            var parentProfile = new ParentProfile { UserId = parentUser.Id };
            var child = new Child { ParentProfile = parentProfile, FirstName = "Kid", LastName = "Withdrawn" };
            _db.Context.AddRange(parentProfile, child);
            await _db.Context.SaveChangesAsync();
            _db.Context.Add(new BatchEnrollment { BatchId = batch.Id, ChildId = child.Id, Status = EnrollmentStatus.Withdrawn });
            await _db.Context.SaveChangesAsync();

            var isParticipant = await CreateSessionService().IsSessionParticipantAsync(session.Id, parentUser.Id);
            Assert.False(isParticipant);
        }

        [Fact]
        public async Task MarkNoShowSystemAsync_AppliesSameCarryForwardAndPayout_ButSkipsTheOwnershipCheck()
        {
            // The background no-show detector has no signed-in caller to check — this is the
            // method it calls instead of the human-facing MarkNoShowAsync.
            var (_, _, session) = await SeedBatchWithSessionAsync(totalSessions: 2);
            _db.CurrentUser.UserId = null; // no "current user" at all, as in a background job

            var carried = await CreateSessionService().MarkNoShowSystemAsync(
                session.Id, NoShowParty.Student, "Auto-detected: no student/parent joined.");

            var original = await _db.Context.ClassSessions.FindAsync(session.Id);
            Assert.Equal(SessionStatus.StudentNoShow, original!.Status);
            Assert.Equal(SessionStatus.CarriedForward, (await _db.Context.ClassSessions.FindAsync(carried.Id))!.Status);
            var item = Assert.Single(_db.Context.PayoutItems.ToList());
            Assert.Equal(PayoutItemType.StudentNoShowWaiting, item.Type);
        }

        [Fact]
        public async Task CreateQuizQuestion_RequiresExactlyOneCorrectOption()
        {
            var service = CreateQuizQuestionService();

            await Assert.ThrowsAsync<DomainValidationException>(() => service.CreateAsync(new SaveQuizQuestionRequest
            {
                DepartmentId = WellKnownDepartments.Phonics,
                Prompt = "Which letter is silent in 'knee'?",
                Options = [new() { Text = "K", IsCorrect = true }, new() { Text = "N", IsCorrect = true }],
            }));

            await Assert.ThrowsAsync<DomainValidationException>(() => service.CreateAsync(new SaveQuizQuestionRequest
            {
                DepartmentId = WellKnownDepartments.Phonics,
                Prompt = "Which letter is silent in 'knee'?",
                Options = [new() { Text = "K", IsCorrect = false }, new() { Text = "N", IsCorrect = false }],
            }));

            Assert.Empty(_db.Context.QuizQuestions.ToList());
        }

        [Fact]
        public async Task CreateQuizQuestion_WithCourseId_DerivesDepartmentFromTheCourse_IgnoringAMismatchedClientValue()
        {
            var (_, course, _) = await SeedBatchWithSessionAsync(totalSessions: 1); // course's real department is Phonics

            var question = await CreateQuizQuestionService().CreateAsync(new SaveQuizQuestionRequest
            {
                CourseId = course.Id,
                DepartmentId = WellKnownDepartments.Maths, // must be ignored/overridden, not trusted
                Prompt = "Which word rhymes with 'cat'?",
                Options = [new() { Text = "Hat", IsCorrect = true }, new() { Text = "Dog", IsCorrect = false }],
            });

            Assert.Equal(WellKnownDepartments.Phonics, question.DepartmentId);
            Assert.Equal(course.Id, question.CourseId);
        }

        [Fact]
        public async Task GetForSession_RegularBatchSession_ReturnsThisCoursesQuestionsBeforeDepartmentWideOnes_AndExcludesOtherCourses()
        {
            var (_, course, session) = await SeedBatchWithSessionAsync(totalSessions: 1);
            var quizService = CreateQuizQuestionService();

            var departmentWide = await quizService.CreateAsync(new SaveQuizQuestionRequest
            {
                DepartmentId = WellKnownDepartments.Phonics,
                Prompt = "Department-wide question",
                DisplayOrder = 1,
                Options = [new() { Text = "A", IsCorrect = true }, new() { Text = "B", IsCorrect = false }],
            });
            var courseSpecific = await quizService.CreateAsync(new SaveQuizQuestionRequest
            {
                CourseId = course.Id,
                Prompt = "Course-specific question",
                DisplayOrder = 1,
                Options = [new() { Text = "A", IsCorrect = true }, new() { Text = "B", IsCorrect = false }],
            });
            // A different department's question must never leak into this session's set.
            await quizService.CreateAsync(new SaveQuizQuestionRequest
            {
                DepartmentId = WellKnownDepartments.Maths,
                Prompt = "Unrelated maths question",
                Options = [new() { Text = "A", IsCorrect = true }, new() { Text = "B", IsCorrect = false }],
            });

            var resolved = await quizService.GetForSessionAsync(session.Id, _db.CurrentUser.UserId!.Value);

            Assert.Equal(2, resolved.Count);
            Assert.Equal(courseSpecific.Id, resolved[0].Id); // this course's own question first
            Assert.Equal(departmentWide.Id, resolved[1].Id);
        }

        [Fact]
        public async Task GetForSession_DemoSession_ReturnsOnlyDepartmentWideQuestions()
        {
            var (session, booking) = await SeedDemoSessionAsync($"lead-{Guid.NewGuid():N}@test.com");
            booking.DepartmentId = WellKnownDepartments.Phonics;
            await _db.Context.SaveChangesAsync();
            var demoTeacherUserId = _db.CurrentUser.UserId!.Value; // SeedBatchWithSessionAsync below reassigns this

            var quizService = CreateQuizQuestionService();
            var departmentWide = await quizService.CreateAsync(new SaveQuizQuestionRequest
            {
                DepartmentId = WellKnownDepartments.Phonics,
                Prompt = "Department-wide question",
                Options = [new() { Text = "A", IsCorrect = true }, new() { Text = "B", IsCorrect = false }],
            });
            // A real course's own question must not leak into a demo (which has no course).
            var (_, course, _) = await SeedBatchWithSessionAsync(totalSessions: 1);
            await quizService.CreateAsync(new SaveQuizQuestionRequest
            {
                CourseId = course.Id,
                Prompt = "Course-specific question",
                Options = [new() { Text = "A", IsCorrect = true }, new() { Text = "B", IsCorrect = false }],
            });

            var resolved = await quizService.GetForSessionAsync(session.Id, demoTeacherUserId);

            var only = Assert.Single(resolved);
            Assert.Equal(departmentWide.Id, only.Id);
        }

        [Fact]
        public async Task GetForSession_RejectsNonParticipant()
        {
            var (_, _, session) = await SeedBatchWithSessionAsync(totalSessions: 1);
            await BecomeUnrelatedTeacherAsync();

            await Assert.ThrowsAsync<ForbiddenException>(
                () => CreateQuizQuestionService().GetForSessionAsync(session.Id, _db.CurrentUser.UserId!.Value));
        }

        [Fact]
        public async Task UpdateQuizQuestion_ReplacesTheOptionSetWholesale()
        {
            var question = await CreateQuizQuestionService().CreateAsync(new SaveQuizQuestionRequest
            {
                DepartmentId = WellKnownDepartments.Phonics,
                Prompt = "Original prompt",
                Options = [new() { Text = "A", IsCorrect = true }, new() { Text = "B", IsCorrect = false }],
            });

            var updated = await CreateQuizQuestionService().UpdateAsync(question.Id, new SaveQuizQuestionRequest
            {
                DepartmentId = WellKnownDepartments.Phonics,
                Prompt = "Edited prompt",
                Options = [new() { Text = "X", IsCorrect = false }, new() { Text = "Y", IsCorrect = true }, new() { Text = "Z", IsCorrect = false }],
            });

            Assert.Equal("Edited prompt", updated.Prompt);
            Assert.Equal(3, updated.Options.Count);
            Assert.Equal(["X", "Y", "Z"], updated.Options.Select(o => o.Text));
            Assert.True(updated.Options.Single(o => o.Text == "Y").IsCorrect);
            // The old two-option set is gone, not left dangling alongside the new three.
            Assert.Equal(3, _db.Context.QuizQuestionOptions.Where(o => o.QuizQuestionId == question.Id).Count());
        }

        [Fact]
        public async Task DeleteQuizQuestion_SoftDeletes_AndNoLongerResolvesForSession()
        {
            var (_, course, session) = await SeedBatchWithSessionAsync(totalSessions: 1);
            var quizService = CreateQuizQuestionService();
            var question = await quizService.CreateAsync(new SaveQuizQuestionRequest
            {
                CourseId = course.Id,
                Prompt = "Will be deleted",
                Options = [new() { Text = "A", IsCorrect = true }, new() { Text = "B", IsCorrect = false }],
            });

            await quizService.DeleteAsync(question.Id);

            Assert.Empty(await quizService.GetForSessionAsync(session.Id, _db.CurrentUser.UserId!.Value));
            await Assert.ThrowsAsync<NotFoundException>(() => quizService.UpdateAsync(question.Id, new SaveQuizQuestionRequest
            {
                CourseId = course.Id,
                Prompt = "x",
                Options = [new() { Text = "A", IsCorrect = true }, new() { Text = "B", IsCorrect = false }],
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
        public async Task ListAsync_MarksHasRecording_WhenAnActiveRecordingExists()
        {
            var (_, _, session) = await SeedBatchWithSessionAsync(totalSessions: 1);
            var sessionService = CreateSessionService();
            await sessionService.AddRecordingAsync(session.Id, new RegisterRecordingRequest
            {
                StorageUrl = "https://cdn.test/rec.mp4",
                DurationSeconds = 2700,
            });

            var listed = await sessionService.ListAsync(
                session.ScheduledStartAtUtc.AddDays(-1), session.ScheduledStartAtUtc.AddDays(1), null, null);
            var dto = Assert.Single(listed, d => d.Id == session.Id);

            Assert.True(dto.HasRecording);
            Assert.NotNull(dto.RecordingExpiresAtUtc);

            var single = await sessionService.GetAsync(session.Id);
            Assert.True(single.HasRecording);
            Assert.NotNull(single.RecordingExpiresAtUtc);
        }

        [Fact]
        public async Task ListAsync_DoesNotMarkHasRecording_WhenOnlyExpiredRecordingsExist()
        {
            var (_, _, session) = await SeedBatchWithSessionAsync(totalSessions: 1);
            _db.Context.SessionRecordings.Add(new SessionRecording
            {
                ClassSessionId = session.Id,
                StorageUrl = "https://cdn.test/old.mp4",
                ExpiresAtUtc = DateTime.UtcNow.AddDays(-1),
            });
            await _db.Context.SaveChangesAsync();

            var listed = await CreateSessionService().ListAsync(
                session.ScheduledStartAtUtc.AddDays(-1), session.ScheduledStartAtUtc.AddDays(1), null, null);
            var dto = Assert.Single(listed, d => d.Id == session.Id);

            Assert.False(dto.HasRecording);
            Assert.Null(dto.RecordingExpiresAtUtc);
        }

        [Fact]
        public async Task GenerateInvoicePdf_UsesNotConfiguredPlaceholder_WhenNoInvoiceSettingsConfigured()
        {
            var (billing, invoice) = await SeedInvoiceAsync(amount: 1000);

            await billing.GenerateInvoicePdfAsync(invoice.Id);

            var request = _invoicePdfGenerator.LastRequest;
            Assert.NotNull(request);
            Assert.Equal("Not configured", request!.AccountNumber);
            Assert.Equal("Not configured", request.GstNumber);
            Assert.Equal("Not configured", request.SignatoryName);
        }

        [Fact]
        public async Task GenerateInvoicePdf_PopulatesSessionsAndFee_FromTheDirectlyLinkedCourse()
        {
            // The PDF's SESSIONS/FEE columns used to always render blank -- InvoicePdfData had
            // no fields for them at all. A course-linked invoice with no subscription at all
            // (no plan to prefer) should price them off that course.
            var (_, course, _) = await SeedBatchWithSessionAsync(totalSessions: 36, includeSession: false);
            var parentUser = await _db.SeedUserAsync($"inv-{Guid.NewGuid():N}@test.com", "x", UserRole.Parent);
            var parentProfile = new ParentProfile { UserId = parentUser.Id };
            _db.Context.AddRange(parentProfile,
                new PaymentAccount { Name = "P", DepartmentId = WellKnownDepartments.Phonics, GatewayProvider = "t", GatewayAccountRef = "p" });
            await _db.Context.SaveChangesAsync();

            var billing = CreateBillingService();
            var invoiceDto = await billing.CreateInvoiceAsync(new CreateInvoiceRequest
            {
                ParentProfileId = parentProfile.Id, DepartmentId = WellKnownDepartments.Phonics, CourseId = course.Id,
                Amount = 6500, DueDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(7)),
            });

            await billing.GenerateInvoicePdfAsync(invoiceDto.Id);

            var request = _invoicePdfGenerator.LastRequest;
            Assert.NotNull(request);
            Assert.Equal(36, request!.Sessions);
            Assert.Equal(course.Price, request.Fee); // the course's own listed fee, not the invoice's Amount
            Assert.Equal(6500m, request.Amount); // Amount is what's actually charged -- can legitimately differ from Fee
        }

        [Fact]
        public async Task GenerateInvoicePdf_PopulatesSessionsAndFee_FromTheSubscriptionsPackagePlan_WhenNoCourseIsDirectlyLinked()
        {
            // Simplest subscription case: no CourseId at all on the invoice, so there's nothing
            // to prefer over -- SESSIONS/FEE come from the subscription's package plan,
            // SessionsIncluded and Price, not the plan's underlying course's own TotalSessions/
            // Price (a plan can legitimately include fewer sessions than the full course, e.g. a
            // trial or partial package). The more realistic "both linked" shape is covered next.
            var parentUser = await _db.SeedUserAsync($"inv-{Guid.NewGuid():N}@test.com", "x", UserRole.Parent);
            var parentProfile = new ParentProfile { UserId = parentUser.Id };
            var child = new Child { ParentProfile = parentProfile, FirstName = "Kid", LastName = "One" };
            var category = new CourseCategory { Name = $"Cat-{Guid.NewGuid():N}", DepartmentId = WellKnownDepartments.Phonics };
            var course = new Course
            {
                CourseCategory = category, Name = "Course", Type = CourseType.Group,
                DurationMinutes = 45, Price = 7500, TotalSessions = 36, DepartmentId = WellKnownDepartments.Phonics,
            };
            var plan = new PackagePlan
            {
                Name = "Trial Pack", Course = course, BillingType = BillingType.Subscription,
                BillingCycle = BillingCycle.Monthly, Price = 2000, SessionsIncluded = 8,
            };
            var subscription = new Subscription
            {
                ParentProfile = parentProfile, Child = child, PackagePlan = plan,
                StartDate = DateOnly.FromDateTime(DateTime.UtcNow),
            };
            _db.Context.AddRange(parentProfile, child, category, course, plan, subscription,
                new PaymentAccount { Name = "P", DepartmentId = WellKnownDepartments.Phonics, GatewayProvider = "t", GatewayAccountRef = "p" });
            await _db.Context.SaveChangesAsync();

            var billing = CreateBillingService();
            var invoiceDto = await billing.CreateInvoiceAsync(new CreateInvoiceRequest
            {
                ParentProfileId = parentProfile.Id, DepartmentId = WellKnownDepartments.Phonics, SubscriptionId = subscription.Id,
                Amount = 2000, DueDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(7)),
            });

            await billing.GenerateInvoicePdfAsync(invoiceDto.Id);

            var request = _invoicePdfGenerator.LastRequest;
            Assert.NotNull(request);
            Assert.Equal(8, request!.Sessions); // the plan's own SessionsIncluded, not the course's TotalSessions (36)
            Assert.Equal(2000m, request.Fee); // the plan's own Price, not the course's Price (7500)
        }

        [Fact]
        public async Task GenerateInvoicePdf_PrefersTheSubscriptionsPlanOverTheCourse_WhenBothAreLinkedOnTheSameInvoice()
        {
            // The realistic shape, not just the "no course at all" fallback above: per
            // Invoice.CourseId's own doc comment, a subscription-driven invoice typically has
            // CourseId ALSO set (copied from the plan's course at creation time) -- so this is
            // what actually happens on a real subscription invoice, and is the case a
            // Course-first precedence would get wrong: that student's own plan (a discounted
            // or trial package, priced differently from the course's generic list price) must
            // still win over the course's own Price/TotalSessions.
            var parentUser = await _db.SeedUserAsync($"inv-{Guid.NewGuid():N}@test.com", "x", UserRole.Parent);
            var parentProfile = new ParentProfile { UserId = parentUser.Id };
            var child = new Child { ParentProfile = parentProfile, FirstName = "Kid", LastName = "One" };
            var category = new CourseCategory { Name = $"Cat-{Guid.NewGuid():N}", DepartmentId = WellKnownDepartments.Phonics };
            var course = new Course
            {
                CourseCategory = category, Name = "Course", Type = CourseType.Group,
                DurationMinutes = 45, Price = 7500, TotalSessions = 36, DepartmentId = WellKnownDepartments.Phonics,
            };
            var plan = new PackagePlan
            {
                Name = "Discounted Pack", Course = course, BillingType = BillingType.Subscription,
                BillingCycle = BillingCycle.Monthly, Price = 6500, SessionsIncluded = 30,
            };
            var subscription = new Subscription
            {
                ParentProfile = parentProfile, Child = child, PackagePlan = plan,
                StartDate = DateOnly.FromDateTime(DateTime.UtcNow),
            };
            _db.Context.AddRange(parentProfile, child, category, course, plan, subscription,
                new PaymentAccount { Name = "P", DepartmentId = WellKnownDepartments.Phonics, GatewayProvider = "t", GatewayAccountRef = "p" });
            await _db.Context.SaveChangesAsync();

            var billing = CreateBillingService();
            var invoiceDto = await billing.CreateInvoiceAsync(new CreateInvoiceRequest
            {
                ParentProfileId = parentProfile.Id, DepartmentId = WellKnownDepartments.Phonics,
                SubscriptionId = subscription.Id, CourseId = course.Id, // both linked, as a real subscription invoice would be
                Amount = 6500, DueDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(7)),
            });

            await billing.GenerateInvoicePdfAsync(invoiceDto.Id);

            var request = _invoicePdfGenerator.LastRequest;
            Assert.NotNull(request);
            Assert.Equal(30, request!.Sessions); // this parent's own plan (30), not the course's full total (36)
            Assert.Equal(6500m, request.Fee); // this parent's own plan price (6500), not the course's list price (7500)
        }

        [Fact]
        public async Task GenerateInvoicePdf_LeavesSessionsAndFeeBlank_WhenNeitherCourseNorSubscriptionIsLinked()
        {
            // A manually-created admin invoice with no course/plan link at all -- Sessions/Fee
            // must stay null (rendered as blank cells) rather than defaulting to 0, which would
            // read as "this course has zero sessions" on the printed PDF.
            var (billing, invoice) = await SeedInvoiceAsync(amount: 1000);

            await billing.GenerateInvoicePdfAsync(invoice.Id);

            var request = _invoicePdfGenerator.LastRequest;
            Assert.NotNull(request);
            Assert.Null(request!.Sessions);
            Assert.Null(request.Fee);
        }

        [Fact]
        public async Task GenerateInvoicePdf_UsesConfiguredSettings_WhenPresent()
        {
            var (billing, invoice) = await SeedInvoiceAsync(amount: 1000);
            _db.Context.AppSettings.Add(new AppSetting
            {
                Category = SettingCategory.General,
                Key = "invoice.accountName",
                Value = "A DIFFERENT ACCOUNT NAME",
            });
            await _db.Context.SaveChangesAsync();

            await billing.GenerateInvoicePdfAsync(invoice.Id);

            var request = _invoicePdfGenerator.LastRequest;
            Assert.NotNull(request);
            Assert.Equal("A DIFFERENT ACCOUNT NAME", request!.AccountName);
            // Untouched keys still fall back to the placeholder, not blank/null.
            Assert.Equal("Not configured", request.AccountNumber);
        }

        [Fact]
        public async Task FinalizeJibriRecording_RegistersAgainstMatchingRoom_WhenTokenValid()
        {
            var (_, _, session) = await SeedBatchWithSessionAsync(totalSessions: 1);
            var jitsiTokens = new FakeJitsiTokenService { ValidateFinalizeTokenResult = true };

            var recording = await CreateSessionService(jitsiTokens).FinalizeJibriRecordingAsync(
                session.MeetingRoomId!, "irrelevant-under-the-fake", "https://jitsi.test/recordings/abc/rec.mp4", 900);

            Assert.NotNull(recording);
            Assert.Equal(session.Id, recording!.ClassSessionId);
            var stored = await _db.Context.SessionRecordings.FindAsync(recording.Id);
            Assert.NotNull(stored);
        }

        [Fact]
        public async Task FinalizeJibriRecording_Rejects_WhenTokenInvalid()
        {
            // Default FakeJitsiTokenService (ValidateFinalizeTokenResult unset) mirrors an
            // invalid/missing/wrong-room token — the finalize hook has no session, so this
            // must be a hard refusal, not a silent no-op that could be mistaken for "room not
            // a class session" (the actual no-op case, covered below).
            var (_, _, session) = await SeedBatchWithSessionAsync(totalSessions: 1);

            await Assert.ThrowsAsync<UnauthorizedException>(() => CreateSessionService().FinalizeJibriRecordingAsync(
                session.MeetingRoomId!, "bad-token", "https://jitsi.test/recordings/abc/rec.mp4", 900));
        }

        [Fact]
        public async Task FinalizeJibriRecording_NoOps_WhenRoomMatchesNoClassSession()
        {
            // A personal/demo room Jibri also records — nothing in the data model to attach
            // it to, so this is a deliberate no-op (null), not a NotFoundException.
            var jitsiTokens = new FakeJitsiTokenService { ValidateFinalizeTokenResult = true };

            var recording = await CreateSessionService(jitsiTokens).FinalizeJibriRecordingAsync(
                "trn-personal-doesnotexist", "irrelevant-under-the-fake", "https://jitsi.test/recordings/abc/rec.mp4", 900);

            Assert.Null(recording);
        }

        [Fact]
        public async Task CreateInvoice_RoutesToMatchingDepartmentAccount_ByDefault()
        {
            var parentUser = await _db.SeedUserAsync($"dept-{Guid.NewGuid():N}@test.com", "x", UserRole.Parent);
            var parentProfile = new ParentProfile { UserId = parentUser.Id };
            var phonics = new PaymentAccount { Name = "Phonics", DepartmentId = WellKnownDepartments.Phonics, GatewayProvider = "razorpay", GatewayAccountRef = "ph" };
            var maths = new PaymentAccount { Name = "Maths", DepartmentId = WellKnownDepartments.Maths, GatewayProvider = "cashfree", GatewayAccountRef = "ma" };
            _db.Context.AddRange(parentProfile, phonics, maths);
            await _db.Context.SaveChangesAsync();

            var invoice = await CreateBillingService().CreateInvoiceAsync(new CreateInvoiceRequest
            {
                ParentProfileId = parentProfile.Id,
                DepartmentId = WellKnownDepartments.Maths,
                Amount = 500,
                DueDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(7)),
            });

            var stored = await _db.Context.Invoices.FirstAsync(i => i.Id == invoice.Id);
            Assert.Equal(maths.Id, stored.PaymentAccountId); // Maths course → Maths account
        }

        [Fact]
        public async Task ListInvoices_PagesNewestFirst_AndClampsAnOversizedPageSize()
        {
            var parentUser = await _db.SeedUserAsync($"page-{Guid.NewGuid():N}@test.com", "x", UserRole.Parent);
            var parentProfile = new ParentProfile { UserId = parentUser.Id };
            var phonics = new PaymentAccount { Name = "Phonics", DepartmentId = WellKnownDepartments.Phonics, GatewayProvider = "razorpay", GatewayAccountRef = "ph" };
            _db.Context.AddRange(parentProfile, phonics);
            await _db.Context.SaveChangesAsync();

            var billing = CreateBillingService();
            for (var i = 0; i < 5; i++)
            {
                await billing.CreateInvoiceAsync(new CreateInvoiceRequest
                {
                    ParentProfileId = parentProfile.Id,
                    DepartmentId = WellKnownDepartments.Phonics,
                    Amount = 100 + i,
                    DueDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(7)),
                });
            }

            var first = await billing.ListInvoicesAsync(null, parentProfile.Id, page: 1, pageSize: 2);
            Assert.Equal(5, first.TotalCount);
            Assert.Equal(2, first.Items.Count);
            Assert.Equal(1, first.Page);

            var second = await billing.ListInvoicesAsync(null, parentProfile.Id, page: 2, pageSize: 2);
            Assert.Equal(2, second.Items.Count);
            // Pages must not overlap — IssuedAtUtc alone ties on rows created in the same tick,
            // so the ordering carries an Id tiebreaker to keep Skip/Take deterministic.
            Assert.Empty(first.Items.Select(i => i.Id).Intersect(second.Items.Select(i => i.Id)));

            var third = await billing.ListInvoicesAsync(null, parentProfile.Id, page: 3, pageSize: 2);
            Assert.Single(third.Items);

            // A caller asking for the whole table gets a bounded page back, not a table scan.
            var greedy = await billing.ListInvoicesAsync(null, parentProfile.Id, page: 1, pageSize: 100_000);
            Assert.Equal(200, greedy.PageSize);
        }

        [Fact]
        public async Task AuditLog_ListAsync_PagesDeterministically_EvenWhenEntriesShareATimestamp()
        {
            // The audit interceptor stamps CreatedAtUtc once per SaveChanges call and applies
            // it to every entity in that batch, so adding all 5 rows in one AddRange +
            // SaveChangesAsync forces a genuine tie on the sort column — exactly the case
            // OrderByDescending(CreatedAtUtc) alone would resolve arbitrarily, letting
            // Skip/Take repeat or drop a row across pages.
            var actor = await _db.SeedUserAsync($"al-page-{Guid.NewGuid():N}@test.com", "x", UserRole.Admin);
            var entries = Enumerable.Range(0, 5)
                .Select(i => new AuditLog { ActorUserId = actor.Id, Action = AuditAction.Update, EntityName = $"PagingTest-{i}" })
                .ToList();
            _db.Context.AuditLogs.AddRange(entries);
            await _db.Context.SaveChangesAsync();
            Assert.Single(entries.Select(e => e.CreatedAtUtc).Distinct()); // the tie really happened

            var first = await _auditLog.ListAsync(entityName: null, action: null, page: 1, pageSize: 2);
            var second = await _auditLog.ListAsync(entityName: null, action: null, page: 2, pageSize: 2);
            var third = await _auditLog.ListAsync(entityName: null, action: null, page: 3, pageSize: 2);

            Assert.Equal(2, first.Items.Count);
            Assert.Equal(2, second.Items.Count);
            var allIds = first.Items.Concat(second.Items).Concat(third.Items).Select(e => e.Id).ToList();
            Assert.Equal(allIds.Count, allIds.Distinct().Count()); // nothing repeated across pages
            Assert.True(allIds.Count >= 5); // and nothing from this batch is missing
        }

        [Fact]
        public async Task PartialThenFullPayment_TransitionsInvoiceStatus()
        {
            var parentUser = await _db.SeedUserAsync($"part-{Guid.NewGuid():N}@test.com", "x", UserRole.Parent);
            var parentProfile = new ParentProfile { UserId = parentUser.Id };
            _db.Context.AddRange(parentProfile,
                new PaymentAccount { Name = "P", DepartmentId = WellKnownDepartments.Phonics, GatewayProvider = "t", GatewayAccountRef = "p" });
            await _db.Context.SaveChangesAsync();
            var billing = CreateBillingService();
            var invoice = await billing.CreateInvoiceAsync(new CreateInvoiceRequest
            {
                ParentProfileId = parentProfile.Id, DepartmentId = WellKnownDepartments.Phonics, Amount = 1000,
                DueDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(7)),
            });

            var partial = await billing.RecordPaymentAsync(invoice.Id, new RecordPaymentRequest { Amount = 400 });
            Assert.Equal(InvoiceStatus.PartiallyPaid, partial.Status);

            var full = await billing.RecordPaymentAsync(invoice.Id, new RecordPaymentRequest { Amount = 600 });
            Assert.Equal(InvoiceStatus.Paid, full.Status);
        }

        /// <summary>
        /// The two-step case above only exercises a single AmountPaid increment. Real parents
        /// pay in dribs and drabs (a UPI part-payment, then cash, then a top-up), so this
        /// chains three, checking AmountPaid accumulates correctly — not just overwritten by
        /// the last call — and that status only flips to Paid once the sum truly clears Amount.
        /// </summary>
        [Fact]
        public async Task ThreeStepPartialPayment_AccumulatesAmountPaidCorrectly_AndOnlyFinalStepPays()
        {
            var parentUser = await _db.SeedUserAsync($"part3-{Guid.NewGuid():N}@test.com", "x", UserRole.Parent);
            var parentProfile = new ParentProfile { UserId = parentUser.Id };
            _db.Context.AddRange(parentProfile,
                new PaymentAccount { Name = "P", DepartmentId = WellKnownDepartments.Phonics, GatewayProvider = "t", GatewayAccountRef = "p" });
            await _db.Context.SaveChangesAsync();
            var billing = CreateBillingService();
            var invoice = await billing.CreateInvoiceAsync(new CreateInvoiceRequest
            {
                ParentProfileId = parentProfile.Id, DepartmentId = WellKnownDepartments.Phonics, Amount = 1000,
                DueDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(7)),
            });

            var step1 = await billing.RecordPaymentAsync(invoice.Id, new RecordPaymentRequest { Amount = 250 });
            Assert.Equal(InvoiceStatus.PartiallyPaid, step1.Status);
            Assert.Equal(250, step1.AmountPaid);

            var step2 = await billing.RecordPaymentAsync(invoice.Id, new RecordPaymentRequest { Amount = 350 });
            Assert.Equal(InvoiceStatus.PartiallyPaid, step2.Status);
            Assert.Equal(600, step2.AmountPaid);

            var step3 = await billing.RecordPaymentAsync(invoice.Id, new RecordPaymentRequest { Amount = 400 });
            Assert.Equal(InvoiceStatus.Paid, step3.Status);
            Assert.Equal(1000, step3.AmountPaid);

            // Three separate transactions were recorded, not one overwritten total.
            var transactionCount = await _db.Context.PaymentTransactions.CountAsync(t => t.InvoiceId == invoice.Id);
            Assert.Equal(3, transactionCount);
        }

        [Fact]
        public async Task InlineCheckout_SettlesOnlyWithVerifiedSignature()
        {
            var parentUser = await _db.SeedUserAsync($"inline-{Guid.NewGuid():N}@test.com", "x", UserRole.Parent);
            var parentProfile = new ParentProfile { UserId = parentUser.Id };
            _db.Context.AddRange(parentProfile,
                new PaymentAccount { Name = "P", DepartmentId = WellKnownDepartments.Phonics, GatewayProvider = "razorpay", GatewayAccountRef = "p" });
            await _db.Context.SaveChangesAsync();
            var billing = CreateBillingService();
            var invoice = await billing.CreateInvoiceAsync(new CreateInvoiceRequest
            {
                ParentProfileId = parentProfile.Id, DepartmentId = WellKnownDepartments.Phonics, Amount = 1000,
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
                new PaymentAccount { Name = "P", DepartmentId = WellKnownDepartments.Phonics, GatewayProvider = "t", GatewayAccountRef = "p" });
            await _db.Context.SaveChangesAsync();
            var billing = CreateBillingService();
            var invoice = await billing.CreateInvoiceAsync(new CreateInvoiceRequest
            {
                ParentProfileId = parentProfile.Id, DepartmentId = WellKnownDepartments.Phonics, Amount = 800,
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
        public async Task FullPayment_DoesNotLiftSuspension_WhileAnotherInvoiceIsStillOverdue()
        {
            // A single suspension row can cover several overdue invoices at once
            // (BillingBackgroundService groups by parent) — paying off just one of them
            // must not restore access while another is still unpaid.
            var parentUser = await _db.SeedUserAsync($"multi-susp-{Guid.NewGuid():N}@test.com", "x", UserRole.Parent);
            var parentProfile = new ParentProfile { UserId = parentUser.Id };
            _db.Context.AddRange(parentProfile,
                new PaymentAccount { Name = "P", DepartmentId = WellKnownDepartments.Phonics, GatewayProvider = "t", GatewayAccountRef = "p" });
            await _db.Context.SaveChangesAsync();
            var billing = CreateBillingService();

            var invoiceA = await billing.CreateInvoiceAsync(new CreateInvoiceRequest
            {
                ParentProfileId = parentProfile.Id, DepartmentId = WellKnownDepartments.Phonics, Amount = 500,
                DueDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-3)),
            });
            var invoiceB = await billing.CreateInvoiceAsync(new CreateInvoiceRequest
            {
                ParentProfileId = parentProfile.Id, DepartmentId = WellKnownDepartments.Phonics, Amount = 300,
                DueDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-3)),
            });
            // Simulates BillingBackgroundService's overdue sweep + suspension, without
            // running the background service itself.
            var trackedB = await _db.Context.Invoices.FirstAsync(i => i.Id == invoiceB.Id);
            trackedB.Status = InvoiceStatus.Overdue;
            _db.Context.FeeSuspensions.Add(new FeeSuspension
            {
                ParentProfileId = parentProfile.Id, InvoiceId = invoiceB.Id,
                Status = SuspensionStatus.Active, SuspendedAtUtc = DateTime.UtcNow,
            });
            await _db.Context.SaveChangesAsync();

            await billing.RecordPaymentAsync(invoiceA.Id, new RecordPaymentRequest { Amount = 500 });

            var paidA = await _db.Context.Invoices.FirstAsync(i => i.Id == invoiceA.Id);
            Assert.Equal(InvoiceStatus.Paid, paidA.Status);
            var suspension = await _db.Context.FeeSuspensions.FirstAsync(s => s.ParentProfileId == parentProfile.Id);
            Assert.Equal(SuspensionStatus.Active, suspension.Status); // invoice B is still overdue
        }

        [Fact]
        public async Task Refund_RequestThenApprove_IsRecorded()
        {
            var parentUser = await _db.SeedUserAsync($"ref-{Guid.NewGuid():N}@test.com", "x", UserRole.Parent);
            var parentProfile = new ParentProfile { UserId = parentUser.Id };
            _db.Context.AddRange(parentProfile,
                new PaymentAccount { Name = "P", DepartmentId = WellKnownDepartments.Phonics, GatewayProvider = "t", GatewayAccountRef = "p" });
            await _db.Context.SaveChangesAsync();
            var billing = CreateBillingService();
            var invoice = await billing.CreateInvoiceAsync(new CreateInvoiceRequest
            {
                ParentProfileId = parentProfile.Id, DepartmentId = WellKnownDepartments.Phonics, Amount = 1000,
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

        /// <summary>
        /// Caught live: the entire refund lifecycle had zero communication -- billing staff had no
        /// way to notice a refund needed review short of checking the screen, and the parent never
        /// learned whether theirs was rejected or actually paid out.
        /// </summary>
        [Fact]
        public async Task Refund_NotifiesBillingStaffOnRequest_AndParentOnRejectOrProcess()
        {
            var adminUser = await _db.SeedUserAsync($"admin-{Guid.NewGuid():N}@test.com", "x", UserRole.Admin);
            var parentUser = await _db.SeedUserAsync($"ref-notify-{Guid.NewGuid():N}@test.com", "x", UserRole.Parent);
            var parentProfile = new ParentProfile { UserId = parentUser.Id };
            _db.Context.AddRange(parentProfile,
                new PaymentAccount { Name = "P", DepartmentId = WellKnownDepartments.Phonics, GatewayProvider = "t", GatewayAccountRef = "p" });
            await _db.Context.SaveChangesAsync();
            var billing = CreateBillingService();
            var invoice = await billing.CreateInvoiceAsync(new CreateInvoiceRequest
            {
                ParentProfileId = parentProfile.Id, DepartmentId = WellKnownDepartments.Phonics, Amount = 1000,
                DueDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(7)),
            });
            await billing.RecordPaymentAsync(invoice.Id, new RecordPaymentRequest { Amount = 1000 });
            var txn = await _db.Context.PaymentTransactions.FirstAsync();
            _emailSender.Sent.Clear();

            var toReject = await billing.RequestRefundAsync(new RequestRefundRequest
            {
                PaymentTransactionId = txn.Id, Amount = 100, Reason = "Wrong course",
            });
            Assert.Contains(_emailSender.Sent, m => m.To == adminUser.Email && m.Subject.StartsWith("Refund requested"));

            await billing.ReviewRefundAsync(toReject.Id, new ReviewRefundRequest { Approve = false });
            Assert.Contains(_emailSender.Sent, m => m.To == parentUser.Email && m.Subject.Contains("not approved"));

            var toApprove = await billing.RequestRefundAsync(new RequestRefundRequest
            {
                PaymentTransactionId = txn.Id, Amount = 150, Reason = "Overcharged",
            });
            await billing.ReviewRefundAsync(toApprove.Id, new ReviewRefundRequest { Approve = true });
            Assert.Contains(_emailSender.Sent, m => m.To == parentUser.Email && m.Subject.StartsWith("Your refund has been processed"));
        }

        /// <summary>A paid invoice with a Requested refund on its transaction, ready to review.</summary>
        private async Task<RefundDto> SeedRequestedRefundAsync(FakePaymentGateway gateway)
        {
            var parentUser = await _db.SeedUserAsync($"ref-race-{Guid.NewGuid():N}@test.com", "x", UserRole.Parent);
            var parentProfile = new ParentProfile { UserId = parentUser.Id };
            _db.Context.AddRange(parentProfile,
                new PaymentAccount { Name = "P", DepartmentId = WellKnownDepartments.Phonics, GatewayProvider = "t", GatewayAccountRef = "p" });
            await _db.Context.SaveChangesAsync();

            var billing = CreateBillingService(gateway);
            var invoice = await billing.CreateInvoiceAsync(new CreateInvoiceRequest
            {
                ParentProfileId = parentProfile.Id, DepartmentId = WellKnownDepartments.Phonics, Amount = 1000,
                DueDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(7)),
            });
            await billing.RecordPaymentAsync(invoice.Id, new RecordPaymentRequest { Amount = 1000 });
            var txn = await _db.Context.PaymentTransactions.FirstAsync(t => t.InvoiceId == invoice.Id);
            return await billing.RequestRefundAsync(new RequestRefundRequest
            {
                PaymentTransactionId = txn.Id, Amount = 250, Reason = "Race test",
            });
        }

        [Fact]
        public async Task Refund_ReviewedConcurrently_MustNotDoubleProcessTheSameRefund()
        {
            // SCOPE NOTE: SQLite cannot run two writers at once — both DbContexts here share one
            // SqliteConnection, which serializes command execution at the ADO.NET level — so this
            // test does not race anything. It instead *stages* the exact interleaving the race
            // depends on (request 2 reads "still Requested" before request 1 commits) and asserts
            // the fix rejects it. That is the whole substance of the bug: on Postgres the two
            // requests reach this state by timing, here they reach it by construction, and from
            // that state onwards the code path is identical. Against the pre-fix code this test
            // fails — request 2 would call the gateway a second time and refund the money twice.
            var gateway = new FakePaymentGateway();
            var refund = await SeedRequestedRefundAsync(gateway);

            // Request 2's own service graph on its own DbContext, exactly as ASP.NET Core would
            // hand a second concurrent caller (an admin double-click, or two admins working the
            // same queue). The shared FakePaymentGateway counts disbursements across both.
            var (context2, uow2) = _db.CreateConcurrentSession();
            var auditLog2 = new AuditLogService(uow2, _db.CurrentUser);
            var billing2 = new BillingService(uow2, auditLog2, gateway, _notifications, _db.CurrentUser, _bulkFileReader, _invoicePdfGenerator);

            // Request 2 reads the refund while it is genuinely still Requested. EF returns this
            // same tracked instance from any later lookup on context2 rather than refreshing it,
            // so when ReviewRefundAsync runs below it sees precisely the stale "still Requested"
            // view a Postgres READ COMMITTED snapshot would have given it mid-race — sailing past
            // the in-memory status check and leaving the conditional UPDATE as the only guard.
            var staleRead = await uow2.Repository<Refund>().GetByIdAsync(refund.Id);
            Assert.Equal(RefundStatus.Requested, staleRead!.Status);

            // Request 1 wins the race and disburses.
            var reviewed = await CreateBillingService(gateway)
                .ReviewRefundAsync(refund.Id, new ReviewRefundRequest { Approve = true });
            Assert.Equal(RefundStatus.Processed, reviewed.Status);
            Assert.Equal(1, gateway.RefundCallCount);

            // Request 2 now acts on its stale view. The UPDATE's WHERE clause no longer matches
            // (the row left Requested), so it affects 0 rows and the approval is refused before
            // the gateway is touched.
            var conflict = await Assert.ThrowsAsync<ConflictException>(
                () => billing2.ReviewRefundAsync(refund.Id, new ReviewRefundRequest { Approve = true }));
            Assert.Equal(409, conflict.StatusCode);
            Assert.Equal(RefundStatus.Requested, staleRead.Status); // it really was working off the stale read

            Assert.Equal(1, gateway.RefundCallCount); // the money moved exactly once

            var (verifyContext, _) = _db.CreateConcurrentSession();
            var stored = await verifyContext.Refunds.FirstAsync(r => r.Id == refund.Id);
            Assert.Equal(RefundStatus.Processed, stored.Status);
            Assert.NotNull(stored.GatewayRefundId);

            context2.Dispose();
            verifyContext.Dispose();
        }

        [Fact]
        public async Task RecordPayment_ConcurrentPaymentsOnSameInvoice_MustNotLoseEitherPayment()
        {
            // SCOPE NOTE — same limitation as Store_BookDemo_ConcurrentRequestsForSameSlot above:
            // both DbContexts here share one SqliteConnection, which serializes command execution
            // at the ADO.NET level, so this cannot force the genuinely-overlapping-transactions
            // case SSI is designed for. What it proves: the code path is correct end-to-end and
            // both payments land — before RecordPaymentAsync wrapped the balance read+write in
            // ExecuteInSerializableTransactionAsync (re-reading the invoice fresh inside instead
            // of closing over a value read before the transaction started), this exact scenario —
            // a gateway checkout and a cash payment both settling the same invoice within
            // moments of each other — would let whichever commits second silently overwrite the
            // first's AmountPaid with its own, losing ₹500 of real, collected money from the
            // invoice's balance despite both PaymentTransaction rows correctly showing Success.
            // The concurrent guarantee itself rests on Postgres SSI aborting a genuinely
            // overlapping second attempt with SQLSTATE 40001 and retrying it against the
            // committed balance — documented semantics, not observed here (see
            // UnitOfWork_SerializableTransaction_* for the retry machinery itself).
            var parentUser = await _db.SeedUserAsync($"pay-race-{Guid.NewGuid():N}@test.com", "x", UserRole.Parent);
            var parentProfile = new ParentProfile { UserId = parentUser.Id };
            _db.Context.AddRange(parentProfile,
                new PaymentAccount { Name = "P", DepartmentId = WellKnownDepartments.Phonics, GatewayProvider = "t", GatewayAccountRef = "p" });
            await _db.Context.SaveChangesAsync();

            var billing1 = CreateBillingService();
            var invoice = await billing1.CreateInvoiceAsync(new CreateInvoiceRequest
            {
                ParentProfileId = parentProfile.Id, DepartmentId = WellKnownDepartments.Phonics, Amount = 1000,
                DueDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(7)),
            });

            // Request 2's own service graph on its own DbContext, exactly as ASP.NET Core would
            // hand a second concurrent caller — here, a parent's gateway checkout settling around
            // the same time an admin confirms a cash payment on the same invoice.
            var (context2, uow2) = _db.CreateConcurrentSession();
            var auditLog2 = new AuditLogService(uow2, _db.CurrentUser);
            var emailTemplates2 = new EmailTemplateService(uow2, auditLog2, new MemoryCache(new MemoryCacheOptions()));
            var notifications2 = new NotificationService(uow2, _emailSender, emailTemplates2, NullLogger<NotificationService>.Instance);
            var billing2 = new BillingService(uow2, auditLog2, new FakePaymentGateway(), notifications2, _db.CurrentUser, _bulkFileReader, _invoicePdfGenerator);

            var task1 = billing1.RecordPaymentAsync(invoice.Id, new RecordPaymentRequest { Amount = 500, Method = PaymentMethod.Card });
            var task2 = billing2.RecordPaymentAsync(invoice.Id, new RecordPaymentRequest { Amount = 500, Method = PaymentMethod.Cash });

            await Task.WhenAll(task1, task2);

            var (verifyContext, _) = _db.CreateConcurrentSession();
            var stored = await verifyContext.Invoices.FirstAsync(i => i.Id == invoice.Id);
            Assert.Equal(1000m, stored.AmountPaid); // both ₹500 payments actually reflected, not just one
            Assert.Equal(InvoiceStatus.Paid, stored.Status);

            var successfulTotal = await verifyContext.PaymentTransactions
                .Where(t => t.InvoiceId == invoice.Id && t.Status == TransactionStatus.Success)
                .SumAsync(t => t.Amount);
            Assert.Equal(1000m, successfulTotal);

            context2.Dispose();
            verifyContext.Dispose();
        }

        [Fact]
        public async Task Refund_ApprovalFailingAtTheGateway_StaysClaimed_AndIsNotApprovableAgain()
        {
            // Fail-closed by design: the refund is claimed out of Requested BEFORE the gateway is
            // called, so a gateway error or timeout — where the disbursement may or may not have
            // actually happened — leaves the refund parked in Approved for an operator to
            // reconcile, never back in Requested where a retry could pay it a second time.
            var gateway = new FakePaymentGateway { RefundFailure = new TimeoutException("gateway timed out") };
            var refund = await SeedRequestedRefundAsync(gateway);

            await Assert.ThrowsAsync<TimeoutException>(
                () => CreateBillingService(gateway).ReviewRefundAsync(refund.Id, new ReviewRefundRequest { Approve = true }));

            var (verifyContext, verifyUow) = _db.CreateConcurrentSession();
            var claimed = await verifyContext.Refunds.FirstAsync(r => r.Id == refund.Id);
            Assert.Equal(RefundStatus.Approved, claimed.Status); // claimed, not rolled back to Requested
            Assert.Null(claimed.GatewayRefundId);
            Assert.Null(claimed.ProcessedAtUtc);
            Assert.NotNull(claimed.UpdatedAtUtc); // ExecuteUpdate bypasses the interceptor — stamped by hand

            // A second approver (fresh context, so it reads the claimed row) is turned away
            // without the gateway being asked to refund again.
            gateway.RefundFailure = null;
            var auditLog2 = new AuditLogService(verifyUow, _db.CurrentUser);
            var billing2 = new BillingService(verifyUow, auditLog2, gateway, _notifications, _db.CurrentUser, _bulkFileReader, _invoicePdfGenerator);
            var rejected = await Assert.ThrowsAsync<DomainValidationException>(
                () => billing2.ReviewRefundAsync(refund.Id, new ReviewRefundRequest { Approve = true }));
            Assert.Contains("already Approved", rejected.Message);
            Assert.Equal(1, gateway.RefundCallCount);

            verifyContext.Dispose();
        }

        [Fact]
        public async Task Repository_ExecuteUpdate_MatchesOnlyRowsStillInTheExpectedState_AndStampsAuditFieldsByHand()
        {
            // The primitive the refund fix is built on, tested directly: the guard lives in the
            // UPDATE's WHERE clause, so a row that has already left the expected state matches 0
            // rows instead of being overwritten. Also pins the audit stamping, which the
            // SaveChanges interceptor cannot do for a change-tracker-bypassing bulk update.
            var actor = Guid.NewGuid();
            _db.CurrentUser.UserId = actor;
            var refund = await SeedRequestedRefundAsync(new FakePaymentGateway());
            var refunds = _db.UnitOfWork.Repository<Refund>();
            var stampedAt = new DateTime(2026, 8, 10, 12, 0, 0, DateTimeKind.Utc);

            var won = await refunds.ExecuteUpdateAsync(
                r => r.Id == refund.Id && r.Status == RefundStatus.Requested,
                setters => setters
                    .SetProperty(r => r.Status, RefundStatus.Approved)
                    .SetProperty(r => r.UpdatedAtUtc, stampedAt)
                    .SetProperty(r => r.UpdatedBy, actor));
            Assert.Equal(1, won);

            // Same statement again: the row is no longer Requested, so nobody wins it twice.
            var lost = await refunds.ExecuteUpdateAsync(
                r => r.Id == refund.Id && r.Status == RefundStatus.Requested,
                setters => setters
                    .SetProperty(r => r.Status, RefundStatus.Rejected)
                    .SetProperty(r => r.UpdatedAtUtc, DateTime.UtcNow)
                    .SetProperty(r => r.UpdatedBy, actor));
            Assert.Equal(0, lost);

            var (verifyContext, _) = _db.CreateConcurrentSession();
            var stored = await verifyContext.Refunds.FirstAsync(r => r.Id == refund.Id);
            Assert.Equal(RefundStatus.Approved, stored.Status); // the loser changed nothing
            Assert.Equal(stampedAt, stored.UpdatedAtUtc);
            Assert.Equal(actor, stored.UpdatedBy);
            verifyContext.Dispose();
        }

        [Fact]
        public async Task ListInvoiceTransactions_ReportsAlreadyRefundedAndExcludesRejected()
        {
            var parentUser = await _db.SeedUserAsync($"txn-{Guid.NewGuid():N}@test.com", "x", UserRole.Parent);
            var parentProfile = new ParentProfile { UserId = parentUser.Id };
            _db.Context.AddRange(parentProfile,
                new PaymentAccount { Name = "P", DepartmentId = WellKnownDepartments.Phonics, GatewayProvider = "t", GatewayAccountRef = "p" });
            await _db.Context.SaveChangesAsync();
            var billing = CreateBillingService();
            var invoice = await billing.CreateInvoiceAsync(new CreateInvoiceRequest
            {
                ParentProfileId = parentProfile.Id, DepartmentId = WellKnownDepartments.Phonics, Amount = 1000,
                DueDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(7)),
            });
            await billing.RecordPaymentAsync(invoice.Id, new RecordPaymentRequest { Amount = 1000 });
            var txn = await _db.Context.PaymentTransactions.FirstAsync();

            // One rejected refund (must NOT count against the refundable balance) and one
            // still-pending request (must count) against the same transaction.
            var rejected = await billing.RequestRefundAsync(new RequestRefundRequest
            {
                PaymentTransactionId = txn.Id, Amount = 400, Reason = "Will be rejected",
            });
            await billing.ReviewRefundAsync(rejected.Id, new ReviewRefundRequest { Approve = false });
            await billing.RequestRefundAsync(new RequestRefundRequest
            {
                PaymentTransactionId = txn.Id, Amount = 300, Reason = "Still pending",
            });

            var rows = await billing.ListInvoiceTransactionsAsync(invoice.Id);

            var row = Assert.Single(rows);
            Assert.Equal(1000, row.Amount);
            Assert.Equal(300, row.AlreadyRefunded); // rejected 400 excluded, pending 300 included
        }

        [Fact]
        public async Task RenewSubscription_ReactivatesLapsedSubscription()
        {
            var parentUser = await _db.SeedUserAsync($"sub-{Guid.NewGuid():N}@test.com", "x", UserRole.Parent);
            var parentProfile = new ParentProfile { UserId = parentUser.Id };
            var plan = new PackagePlan { Name = "Monthly", BillingType = BillingType.Subscription, BillingCycle = BillingCycle.Monthly, Price = 2000 };
            // Starting/renewing a subscription now issues its first invoice immediately,
            // which routes through the department's payment account.
            var account = new PaymentAccount { Name = "Phonics", DepartmentId = WellKnownDepartments.Phonics, GatewayProvider = "simulated", GatewayAccountRef = "ph" };
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
        public async Task CreateSubscription_ComputesEndDate_FromPlanValidityDays()
        {
            var parentUser = await _db.SeedUserAsync($"sub-{Guid.NewGuid():N}@test.com", "x", UserRole.Parent);
            var parentProfile = new ParentProfile { UserId = parentUser.Id };
            var plan = new PackagePlan { Name = "10-Session Pack", BillingType = BillingType.SessionBased, BillingCycle = BillingCycle.OneTime, Price = 5000, ValidityDays = 60 };
            var account = new PaymentAccount { Name = "Phonics", DepartmentId = WellKnownDepartments.Phonics, GatewayProvider = "simulated", GatewayAccountRef = "ph" };
            _db.Context.AddRange(parentProfile, plan, account);
            await _db.Context.SaveChangesAsync();
            var child = new Child { ParentProfileId = parentProfile.Id, FirstName = "Kid", LastName = "One", IsActive = true };
            _db.Context.Children.Add(child);
            await _db.Context.SaveChangesAsync();
            var billing = CreateBillingService();
            var startDate = DateOnly.FromDateTime(DateTime.UtcNow);

            var sub = await billing.CreateSubscriptionAsync(new CreateSubscriptionRequest
            {
                ParentProfileId = parentProfile.Id, ChildId = child.Id, PackagePlanId = plan.Id, StartDate = startDate,
            });

            Assert.Equal(startDate.AddDays(60), sub.EndDate);
        }

        [Fact]
        public async Task CreateSubscription_LeavesEndDateNull_WhenPlanHasNoValidityDays()
        {
            var parentUser = await _db.SeedUserAsync($"sub-{Guid.NewGuid():N}@test.com", "x", UserRole.Parent);
            var parentProfile = new ParentProfile { UserId = parentUser.Id };
            var plan = new PackagePlan { Name = "Monthly", BillingType = BillingType.Subscription, BillingCycle = BillingCycle.Monthly, Price = 2000 };
            var account = new PaymentAccount { Name = "Phonics", DepartmentId = WellKnownDepartments.Phonics, GatewayProvider = "simulated", GatewayAccountRef = "ph" };
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

            Assert.Null(sub.EndDate);
        }

        [Fact]
        public async Task RenewSubscription_RecomputesEndDate_InsteadOfLeavingItStale()
        {
            var parentUser = await _db.SeedUserAsync($"sub-{Guid.NewGuid():N}@test.com", "x", UserRole.Parent);
            var parentProfile = new ParentProfile { UserId = parentUser.Id };
            var plan = new PackagePlan { Name = "10-Session Pack", BillingType = BillingType.SessionBased, BillingCycle = BillingCycle.OneTime, Price = 5000, ValidityDays = 30 };
            var account = new PaymentAccount { Name = "Phonics", DepartmentId = WellKnownDepartments.Phonics, GatewayProvider = "simulated", GatewayAccountRef = "ph" };
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
            // Simulate the subscription having lapsed well past its original EndDate before
            // being renewed -- exactly the case a stale, un-recomputed EndDate would break.
            var tracked = await _db.Context.Subscriptions.FirstAsync(s => s.Id == sub.Id);
            tracked.EndDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-5);
            await _db.Context.SaveChangesAsync();

            var renewed = await billing.RenewSubscriptionAsync(sub.Id);

            Assert.Equal(SubscriptionStatus.Active, renewed.Status);
            Assert.Equal(DateOnly.FromDateTime(DateTime.UtcNow).AddDays(30), renewed.EndDate);
        }

        [Fact]
        public async Task RenewSubscription_ReactivatesAnExpiredSubscription_NotJustACancelledOne()
        {
            // RenewSubscriptionAsync's guard only checks "not already Active" -- Expired is a
            // new-to-this-feature FROM-state (set by BillingBackgroundService's validity-days
            // sweep, never by an explicit cancel action) that deserves its own direct check
            // rather than trusting it behaves the same as the already-tested Cancelled path.
            var parentUser = await _db.SeedUserAsync($"sub-{Guid.NewGuid():N}@test.com", "x", UserRole.Parent);
            var parentProfile = new ParentProfile { UserId = parentUser.Id };
            var plan = new PackagePlan { Name = "10-Session Pack", BillingType = BillingType.SessionBased, BillingCycle = BillingCycle.OneTime, Price = 5000, ValidityDays = 30 };
            var account = new PaymentAccount { Name = "Phonics", DepartmentId = WellKnownDepartments.Phonics, GatewayProvider = "simulated", GatewayAccountRef = "ph" };
            _db.Context.AddRange(parentProfile, plan, account);
            await _db.Context.SaveChangesAsync();
            var child = new Child { ParentProfileId = parentProfile.Id, FirstName = "Kid", LastName = "One", IsActive = true };
            _db.Context.Children.Add(child);
            await _db.Context.SaveChangesAsync();
            var billing = CreateBillingService();
            var sub = await billing.CreateSubscriptionAsync(new CreateSubscriptionRequest
            {
                ParentProfileId = parentProfile.Id, ChildId = child.Id, PackagePlanId = plan.Id,
                StartDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-40),
            });
            // Simulate exactly what BillingBackgroundService's expiry sweep does to a lapsed
            // row -- flip Status to Expired and clear NextBillingAtUtc -- without running the
            // background service itself, same simulate-the-sweep's-outcome pattern the existing
            // overdue-invoice/suspension tests already use.
            var tracked = await _db.Context.Subscriptions.FirstAsync(s => s.Id == sub.Id);
            tracked.Status = SubscriptionStatus.Expired;
            tracked.NextBillingAtUtc = null;
            await _db.Context.SaveChangesAsync();

            var renewed = await billing.RenewSubscriptionAsync(sub.Id);

            Assert.Equal(SubscriptionStatus.Active, renewed.Status);
            Assert.Equal(DateOnly.FromDateTime(DateTime.UtcNow).AddDays(30), renewed.EndDate);
        }

        [Fact]
        public async Task CreateSubscription_AllowsFreshSubscription_WhenThePreviousOneOnThatPlanHasExpired()
        {
            // The one-active-subscription-per-child+plan duplicate check only blocks on
            // Status == Active -- confirms Expired (like Cancelled) correctly does NOT count
            // as still occupying that slot, so a family can re-subscribe to the same plan
            // after their previous pack lapsed.
            var parentUser = await _db.SeedUserAsync($"sub-{Guid.NewGuid():N}@test.com", "x", UserRole.Parent);
            var parentProfile = new ParentProfile { UserId = parentUser.Id };
            var plan = new PackagePlan { Name = "10-Session Pack", BillingType = BillingType.SessionBased, BillingCycle = BillingCycle.OneTime, Price = 5000, ValidityDays = 30 };
            var account = new PaymentAccount { Name = "Phonics", DepartmentId = WellKnownDepartments.Phonics, GatewayProvider = "simulated", GatewayAccountRef = "ph" };
            _db.Context.AddRange(parentProfile, plan, account);
            await _db.Context.SaveChangesAsync();
            var child = new Child { ParentProfileId = parentProfile.Id, FirstName = "Kid", LastName = "One", IsActive = true };
            _db.Context.Children.Add(child);
            await _db.Context.SaveChangesAsync();
            var billing = CreateBillingService();
            var firstSub = await billing.CreateSubscriptionAsync(new CreateSubscriptionRequest
            {
                ParentProfileId = parentProfile.Id, ChildId = child.Id, PackagePlanId = plan.Id,
                StartDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-40),
            });
            var trackedFirst = await _db.Context.Subscriptions.FirstAsync(s => s.Id == firstSub.Id);
            trackedFirst.Status = SubscriptionStatus.Expired;
            trackedFirst.NextBillingAtUtc = null;
            await _db.Context.SaveChangesAsync();

            var secondSub = await billing.CreateSubscriptionAsync(new CreateSubscriptionRequest
            {
                ParentProfileId = parentProfile.Id, ChildId = child.Id, PackagePlanId = plan.Id,
                StartDate = DateOnly.FromDateTime(DateTime.UtcNow),
            });

            Assert.NotEqual(firstSub.Id, secondSub.Id);
            Assert.Equal(SubscriptionStatus.Active, secondSub.Status);
        }

        [Fact]
        public async Task CancelSubscription_CancelsItsStillOpenInvoice_ButLeavesAPaidOneAlone()
        {
            var parentUser = await _db.SeedUserAsync($"sub-{Guid.NewGuid():N}@test.com", "x", UserRole.Parent);
            var parentProfile = new ParentProfile { UserId = parentUser.Id };
            var plan = new PackagePlan { Name = "Monthly", BillingType = BillingType.Subscription, BillingCycle = BillingCycle.Monthly, Price = 2000 };
            var account = new PaymentAccount { Name = "Phonics", DepartmentId = WellKnownDepartments.Phonics, GatewayProvider = "simulated", GatewayAccountRef = "ph" };
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

            // The subscription's first invoice is still Pending; pay it off, then add a second,
            // still-open one (mirrors a real second billing-cycle invoice) so both a settled and
            // an unsettled invoice exist on the same subscription before cancelling it.
            var firstInvoice = await _db.Context.Invoices.FirstAsync(i => i.SubscriptionId == sub.Id);
            await billing.RecordPaymentAsync(firstInvoice.Id, new RecordPaymentRequest { Amount = firstInvoice.Amount });
            var secondInvoice = await billing.CreateInvoiceAsync(new CreateInvoiceRequest
            {
                ParentProfileId = parentProfile.Id,
                ChildId = child.Id,
                SubscriptionId = sub.Id,
                DepartmentId = WellKnownDepartments.Phonics,
                Amount = 2000,
                DueDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(7)),
            });

            await billing.CancelSubscriptionAsync(sub.Id);

            Assert.Equal(InvoiceStatus.Paid, (await _db.Context.Invoices.FindAsync(firstInvoice.Id))!.Status);
            Assert.Equal(InvoiceStatus.Cancelled, (await _db.Context.Invoices.FindAsync(secondInvoice.Id))!.Status);
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

        /// <summary>
        /// Holiday.Date is a local (Asia/Kolkata) calendar date. A session at 02:00 IST is
        /// 20:30 UTC the PRIOR day — DateOnly.FromDateTime(startUtc) used to truncate the raw
        /// UTC instant instead of converting through the org's own timezone first, so a session
        /// genuinely scheduled during the holiday's early-morning IST hours computed the wrong
        /// (previous) calendar day and slipped past this check entirely.
        /// </summary>
        [Fact]
        public async Task ScheduleSession_OnHolidayInEarlyMorningIst_IsBlocked()
        {
            var (batch, _, _) = await SeedBatchWithSessionAsync(totalSessions: 1, includeSession: false);
            var teacher = await _db.Context.TeacherProfiles.FirstAsync();
            // Comfortably in the future regardless of exact current time, so ValidateWindow's
            // own "cannot be in the past" check never fires ahead of the holiday check below.
            var holiday = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(10));
            _db.Context.Holidays.Add(new Holiday { Name = "Independence Day", Date = holiday });
            await _db.Context.SaveChangesAsync();

            // 02:00 IST on the holiday itself == 20:30 UTC the day before.
            var start = holiday.AddDays(-1).ToDateTime(new TimeOnly(20, 30), DateTimeKind.Utc);
            var ex = await Assert.ThrowsAsync<DomainValidationException>(() =>
                CreateSessionService().ScheduleAsync(new ScheduleSessionRequest
                {
                    BatchId = batch.Id,
                    TeacherProfileId = teacher.Id,
                    Type = SessionType.Regular,
                    ScheduledStartAtUtc = start,
                    ScheduledEndAtUtc = start.AddMinutes(45),
                }));
            Assert.Contains("holiday", ex.Message);
        }

        [Fact]
        public async Task ScheduleSession_RejectsAStartTimeInThePast()
        {
            var (batch, _, _) = await SeedBatchWithSessionAsync(totalSessions: 1, includeSession: false);
            var teacher = await _db.Context.TeacherProfiles.FirstAsync();

            var start = new DateTime(2020, 1, 1, 10, 0, 0, DateTimeKind.Utc);
            var ex = await Assert.ThrowsAsync<DomainValidationException>(() =>
                CreateSessionService().ScheduleAsync(new ScheduleSessionRequest
                {
                    BatchId = batch.Id,
                    TeacherProfileId = teacher.Id,
                    Type = SessionType.Regular,
                    ScheduledStartAtUtc = start,
                    ScheduledEndAtUtc = start.AddMinutes(45),
                }));
            Assert.Contains("cannot be in the past", ex.Message);

            var stored = await _db.Context.ClassSessions.CountAsync(s => s.BatchId == batch.Id);
            Assert.Equal(0, stored); // nothing was persisted
        }

        [Fact]
        public async Task RescheduleSession_RejectsAStartTimeInThePast()
        {
            var (_, _, session) = await SeedBatchWithSessionAsync(totalSessions: 1);
            var start = new DateTime(2020, 1, 1, 10, 0, 0, DateTimeKind.Utc);

            var ex = await Assert.ThrowsAsync<DomainValidationException>(() => CreateSessionService().RescheduleAsync(
                session.Id,
                new RescheduleSessionRequest
                {
                    ScheduledStartAtUtc = start,
                    ScheduledEndAtUtc = start.AddMinutes(45),
                }));
            Assert.Contains("cannot be in the past", ex.Message);

            var untouched = await _db.Context.ClassSessions.FindAsync(session.Id);
            Assert.Equal(SessionStatus.Scheduled, untouched!.Status); // never rescheduled
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

        /// <summary>
        /// The clash window used to be [holidayDate 00:00 UTC, +1 day) — treating the holiday's
        /// own local calendar date as if it were already a UTC boundary. A session at 02:00 IST
        /// the day AFTER the holiday is 20:30 UTC ON the holiday's own date, which fell inside
        /// that UTC-naive window and was wrongly auto-cancelled and carried forward a week, even
        /// though in local (real) calendar terms it was never on the holiday at all.
        /// </summary>
        [Fact]
        public async Task CreateHoliday_DoesNotWronglyCancelTheFollowingDaysEarlyMorningSession()
        {
            var (batch, _, _) = await SeedBatchWithSessionAsync(totalSessions: 1, includeSession: false);
            var teacher = await _db.Context.TeacherProfiles.FirstAsync();
            var holidayDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(10));

            // 02:00 IST the day AFTER the holiday == 20:30 UTC on the holiday's own date.
            var sessionStart = holidayDate.ToDateTime(new TimeOnly(20, 30), DateTimeKind.Utc);
            await CreateSessionService().ScheduleAsync(new ScheduleSessionRequest
            {
                BatchId = batch.Id,
                TeacherProfileId = teacher.Id,
                Type = SessionType.Regular,
                ScheduledStartAtUtc = sessionStart,
                ScheduledEndAtUtc = sessionStart.AddMinutes(45),
            });
            var session = await _db.Context.ClassSessions.FirstAsync();
            _db.Context.ChangeTracker.Clear();

            await CreateAcademicOpsService().CreateHolidayAsync(new SaveHolidayRequest
            {
                Name = "Independence Day",
                Date = holidayDate,
            });

            var untouched = await _db.Context.ClassSessions.FirstAsync(s => s.Id == session.Id);
            Assert.Equal(SessionStatus.Scheduled, untouched.Status); // never touched — it was never actually on the holiday
        }

        [Fact]
        public async Task FinalizeAndMarkPaid_PersistStatus_AndEmailSalarySlip()
        {
            var (_, _, session) = await SeedBatchWithSessionAsync(totalSessions: 2);
            var payoutService = CreatePayoutService();
            await payoutService.SetRateAsync(new SavePayoutRateRequest
            {
                TeacherProfileId = session.TeacherProfileId,
                RatePerMinute = 900,
                EffectiveFrom = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-30)),
            });
            await SeedFullTeacherAttendanceAsync(session);
            await CreateSessionService().CompleteAsync(session.Id); // accrues the earning
            var payout = await _db.Context.Payouts.FirstAsync();
            _db.Context.ChangeTracker.Clear(); // fresh request scope

            await payoutService.FinalizeAsync(payout.Id);
            _db.Context.ChangeTracker.Clear();

            // Persistence check (the AsNoTracking-mutation bug this caught): re-read from the DB.
            var finalized = await _db.Context.Payouts.FirstAsync(p => p.Id == payout.Id);
            Assert.Equal(PayoutStatus.Finalized, finalized.Status);
            Assert.Equal(40500, finalized.TotalAmount); // 900/min * 45 min
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
            // GrantAsync now requires genuine session participation — the session's own
            // assigned teacher is a valid caller.
            var callerId = (await _db.Context.TeacherProfiles.FindAsync(session.TeacherProfileId))!.UserId;

            await gamification.GrantAsync(callerId, new GrantAwardRequest { SessionId = sessionId, ParticipantName = "Aarav", Points = 2 });
            var afterTwo = await gamification.GetLeaderboardAsync(sessionId, 10);
            Assert.Equal(2, afterTwo.Single().Stars);
            Assert.Empty(afterTwo.Single().Badges);

            // Crossing 3 stars auto-grants the "Rising Star" milestone.
            var granted = await gamification.GrantAsync(callerId, new GrantAwardRequest { SessionId = sessionId, ParticipantName = "Aarav", Points = 1 });
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

        /// <summary>
        /// Phase 3 of the menu/role redesign: once a RoleDefinition has an explicit
        /// MenuPermission row for a menu item, that grant is authoritative in both directions —
        /// it can hide an otherwise-always-visible (ungated) item or a module-permitted gated
        /// item, and it can show a gated item the role's module grants would otherwise hide.
        /// A menu item with no row at all must keep falling back to the pre-Phase-3 behavior.
        /// </summary>
        [Fact]
        public async Task Menu_ForUser_ExplicitMenuPermissionGrant_OverridesTheLegacyModuleGateInBothDirections()
        {
            var teacherRole = new RoleDefinition { Name = "teacher", DisplayName = "Teacher" };
            teacherRole.Permissions.Add(new RolePermission { Module = PermissionModule.ContentAccessManagement, CanView = true });
            var teacherUser = await _db.SeedUserAsync($"teacher-{Guid.NewGuid():N}@test.com", "x", UserRole.Teacher);

            var ungated = new domain.Entities.Navigation.MenuItem
            {
                Portal = "teacher", Label = "Dashboard", Path = "/teacher", Icon = "LayoutDashboard",
                SectionOrder = 0, SortOrder = 0, IsActive = true, RequiredModule = null,
            };
            var gatedAllowed = new domain.Entities.Navigation.MenuItem
            {
                Portal = "teacher", Label = "Resources", Path = "/teacher/resources", Icon = "FolderOpen",
                SectionOrder = 0, SortOrder = 1, IsActive = true, RequiredModule = PermissionModule.ContentAccessManagement,
            };
            var gatedDenied = new domain.Entities.Navigation.MenuItem
            {
                Portal = "teacher", Label = "Billing", Path = "/teacher/billing", Icon = "Receipt",
                SectionOrder = 0, SortOrder = 2, IsActive = true, RequiredModule = PermissionModule.BillingFinance,
            };
            _db.Context.AddRange(teacherRole, ungated, gatedAllowed, gatedDenied);
            await _db.Context.SaveChangesAsync();

            var service = CreateMenuService();
            var viewableModules = new[] { PermissionModule.ContentAccessManagement };

            // Sanity check: before any explicit grant, legacy behavior holds.
            var beforeOverrides = await service.GetForUserAsync(teacherUser.Id, UserRole.Teacher, viewableModules);
            Assert.Contains(beforeOverrides, m => m.Path == ungated.Path);
            Assert.Contains(beforeOverrides, m => m.Path == gatedAllowed.Path);
            Assert.DoesNotContain(beforeOverrides, m => m.Path == gatedDenied.Path);

            // Explicitly hide the two the legacy path would show, and show the one it would hide.
            var menuPermissions = CreateMenuPermissionService();
            await menuPermissions.SetForRoleAsync(teacherRole.Id,
            [
                new SaveMenuPermissionItem { MenuItemId = ungated.Id, CanView = false },
                new SaveMenuPermissionItem { MenuItemId = gatedAllowed.Id, CanView = false },
                new SaveMenuPermissionItem { MenuItemId = gatedDenied.Id, CanView = true },
            ]);

            var afterOverrides = await service.GetForUserAsync(teacherUser.Id, UserRole.Teacher, viewableModules);
            Assert.DoesNotContain(afterOverrides, m => m.Path == ungated.Path);
            Assert.DoesNotContain(afterOverrides, m => m.Path == gatedAllowed.Path);
            Assert.Contains(afterOverrides, m => m.Path == gatedDenied.Path);
        }

        /// <summary>
        /// Reproduces the live bug: RoleDefinitionId is only ever set by account creation with
        /// an explicit preset or "Apply preset…" — plain per-user permission edits and
        /// access-request approval never touch it, so a "plain" Relationship Manager with no
        /// preset applied is the common case, not an edge case. Before this fix,
        /// ResolvePortalAndRoleAsync returned a bare null RoleDefinitionId for that case, so
        /// Menu Access grants configured for "Parent Relationship Manager" (the sub-admin base
        /// preset) never applied to them. A Sub Admin WITH a different explicit preset
        /// (Coordinator here) must still use that preset's own grants, not the base one.
        /// </summary>
        [Fact]
        public async Task Menu_ForUser_SubAdminWithoutAnAppliedPreset_FallsBackToTheBaseSubAdminRoleDefinition()
        {
            var baseRole = new RoleDefinition { Name = "sub-admin", DisplayName = "Parent Relationship Manager" };
            var coordinatorRole = new RoleDefinition { Name = "coordinator", DisplayName = "Coordinator" };
            var plainRelationshipManager = await _db.SeedUserAsync($"rm-{Guid.NewGuid():N}@test.com", "x", UserRole.SubAdmin);
            var coordinatorUser = await _db.SeedUserAsync($"coord-{Guid.NewGuid():N}@test.com", "x", UserRole.SubAdmin);

            var reportsItem = new domain.Entities.Navigation.MenuItem
            {
                Portal = "subadmin", Label = "Reports", Path = "/subadmin/reports", Icon = "BarChart3",
                SectionOrder = 0, SortOrder = 0, IsActive = true, RequiredModule = null,
            };
            _db.Context.AddRange(baseRole, coordinatorRole, reportsItem);
            await _db.Context.SaveChangesAsync();
            coordinatorUser.RoleDefinitionId = coordinatorRole.Id;
            await _db.Context.SaveChangesAsync();

            var menuPermissions = CreateMenuPermissionService();
            // The base preset explicitly hides an otherwise-unrequired (always-visible) item.
            await menuPermissions.SetForRoleAsync(baseRole.Id, [new SaveMenuPermissionItem { MenuItemId = reportsItem.Id, CanView = false }]);

            var service = CreateMenuService();

            var plainUserMenu = await service.GetForUserAsync(plainRelationshipManager.Id, UserRole.SubAdmin, []);
            Assert.DoesNotContain(plainUserMenu, m => m.Path == reportsItem.Path);

            // Coordinator has an explicit preset with no grant configured for this item — must
            // fall back to the legacy "unrequired → visible" rule for THEIR preset, not borrow
            // the base preset's explicit hide.
            var coordinatorMenu = await service.GetForUserAsync(coordinatorUser.Id, UserRole.SubAdmin, []);
            Assert.Contains(coordinatorMenu, m => m.Path == reportsItem.Path);
        }

        /// <summary>
        /// A role can now be explicitly granted View on a menu item from a DIFFERENT portal
        /// than its own home — the cross-portal access feature. The legacy fallback (no
        /// explicit row) must stay scoped to the caller's home portal only, so a foreign-portal
        /// item nobody has granted stays invisible, and the caller's own home-portal items are
        /// unaffected either way.
        /// </summary>
        [Fact]
        public async Task Menu_ForUser_ExplicitGrant_MakesAForeignPortalItemVisible_ButOnlyWhenGranted()
        {
            var teacherRole = new RoleDefinition { Name = "teacher", DisplayName = "Teacher" };
            var teacherUser = await _db.SeedUserAsync($"teacher-{Guid.NewGuid():N}@test.com", "x", UserRole.Teacher);

            var homeItem = new domain.Entities.Navigation.MenuItem
            {
                Portal = "teacher", Label = "My Classes", Path = "/teacher", Icon = "LayoutDashboard",
                SectionOrder = 0, SortOrder = 0, IsActive = true, RequiredModule = null,
            };
            var grantedForeignItem = new domain.Entities.Navigation.MenuItem
            {
                Portal = "admin", Label = "Courses", Path = "/admin/courses", Icon = "BookOpen",
                SectionOrder = 0, SortOrder = 0, IsActive = true, RequiredModule = PermissionModule.CourseBatchManagement,
            };
            var ungrantedForeignItem = new domain.Entities.Navigation.MenuItem
            {
                Portal = "admin", Label = "Billing & Finance", Path = "/admin/billing", Icon = "Receipt",
                SectionOrder = 0, SortOrder = 1, IsActive = true, RequiredModule = null, // unrequired, but still a foreign portal
            };
            _db.Context.AddRange(teacherRole, homeItem, grantedForeignItem, ungrantedForeignItem);
            await _db.Context.SaveChangesAsync();

            var menuPermissions = CreateMenuPermissionService();
            await menuPermissions.SetForRoleAsync(teacherRole.Id, [new SaveMenuPermissionItem { MenuItemId = grantedForeignItem.Id, CanView = true }]);

            var service = CreateMenuService();
            var menu = await service.GetForUserAsync(teacherUser.Id, UserRole.Teacher, []);

            Assert.Contains(menu, m => m.Path == homeItem.Path);
            Assert.Contains(menu, m => m.Path == grantedForeignItem.Path);
            Assert.DoesNotContain(menu, m => m.Path == ungrantedForeignItem.Path);
        }

        [Fact]
        public async Task Login_Succeeds_WithValidCredentials()
        {
            await _db.SeedUserAsync("admin@test.com", _hasher.Hash("4821"), UserRole.Admin);

            var response = await CreateAuthService().LoginAsync(
                new LoginRequest { Email = "admin@test.com", Pin = "4821" });

            Assert.Equal("test-token", response.AccessToken);
            Assert.Equal(UserRole.Admin, response.User.Role);
        }

        [Fact]
        public async Task Login_Fails_WithWrongPin()
        {
            await _db.SeedUserAsync("admin@test.com", _hasher.Hash("4821"), UserRole.Admin);

            await Assert.ThrowsAsync<UnauthorizedException>(() =>
                CreateAuthService().LoginAsync(new LoginRequest { Email = "admin@test.com", Pin = "0000" }));
        }

        /// <summary>
        /// The login endpoint's rate limit (Program.cs) partitions by IP only — with no
        /// per-account counter, an attacker who knows one target's email and spreads attempts
        /// across a few source IPs could otherwise brute-force a 4-digit PIN's full 10,000-value
        /// keyspace against that one account with no server-side signal it's under attack.
        /// </summary>
        [Fact]
        public async Task Login_LocksAccount_AfterRepeatedWrongPin_EvenWithTheCorrectPinAfterward()
        {
            var email = $"lockout-{Guid.NewGuid():N}@test.com";
            await _db.SeedUserAsync(email, _hasher.Hash("4821"), UserRole.Admin);

            for (var i = 0; i < 5; i++)
            {
                await Assert.ThrowsAsync<UnauthorizedException>(() =>
                    CreateAuthService().LoginAsync(new LoginRequest { Email = email, Pin = "0000" }));
            }

            // The 5th wrong attempt above crossed the threshold — even the genuinely correct
            // PIN is now rejected until the lockout window passes.
            var ex = await Assert.ThrowsAsync<UnauthorizedException>(() =>
                CreateAuthService().LoginAsync(new LoginRequest { Email = email, Pin = "4821" }));
            Assert.Contains("Too many failed attempts", ex.Message);
        }

        [Fact]
        public async Task Login_Succeeding_ResetsThePriorFailedAttemptCount()
        {
            var email = $"reset-{Guid.NewGuid():N}@test.com";
            await _db.SeedUserAsync(email, _hasher.Hash("4821"), UserRole.Admin);

            // A few wrong attempts, but never enough to cross the lockout threshold.
            for (var i = 0; i < 3; i++)
            {
                await Assert.ThrowsAsync<UnauthorizedException>(() =>
                    CreateAuthService().LoginAsync(new LoginRequest { Email = email, Pin = "0000" }));
            }

            await CreateAuthService().LoginAsync(new LoginRequest { Email = email, Pin = "4821" });

            var user = await _db.Context.Users.SingleAsync(u => u.Email == email);
            Assert.Equal(0, user.FailedLoginAttempts);
            Assert.Null(user.LockoutEndUtc);
        }

        [Fact]
        public async Task ApproveAccessRequest_ActuallyGrantsTheRequestedModules_NotJustAStatusFlip()
        {
            // Regression: ReviewAsync used to only flip the request's Status to Approved and
            // email the requester — nothing ever touched SubAdminPermission, so an approved
            // request left the Sub Admin exactly as access-less as before, "No access" on
            // every module they'd asked for.
            var subAdmin = await _db.SeedUserAsync($"rm-{Guid.NewGuid():N}@test.com", "x", UserRole.SubAdmin);
            var admin = await _db.SeedUserAsync($"admin-{Guid.NewGuid():N}@test.com", "x", UserRole.Admin);
            var access = CreateAccessRequestService();

            var submitted = await access.SubmitAsync(subAdmin.Id, new SubmitAccessRequestRequest
            {
                RequestedModules = [PermissionModule.CourseBatchManagement, PermissionModule.SessionCalendarManagement],
            });

            await access.ReviewAsync(submitted.Id, admin.Id, new ReviewAccessRequestRequest { Approve = true });

            var grants = await _db.Context.SubAdminPermissions.AsNoTracking()
                .Where(p => p.UserId == subAdmin.Id)
                .ToDictionaryAsync(p => p.Module);
            Assert.True(grants[PermissionModule.CourseBatchManagement].CanView);
            Assert.True(grants[PermissionModule.SessionCalendarManagement].CanView);
        }

        [Fact]
        public async Task ApproveAccessRequest_NeverDowngradesAModuleAlreadyHeldAtAHigherLevel()
        {
            var subAdmin = await _db.SeedUserAsync($"rm-{Guid.NewGuid():N}@test.com", "x", UserRole.SubAdmin);
            var admin = await _db.SeedUserAsync($"admin-{Guid.NewGuid():N}@test.com", "x", UserRole.Admin);
            _db.Context.SubAdminPermissions.Add(new SubAdminPermission
            {
                UserId = subAdmin.Id, Module = PermissionModule.CourseBatchManagement,
                CanView = true, CanCreate = true, CanEdit = true,
            });
            await _db.Context.SaveChangesAsync();

            var access = CreateAccessRequestService();
            var submitted = await access.SubmitAsync(subAdmin.Id, new SubmitAccessRequestRequest
            {
                RequestedModules = [PermissionModule.CourseBatchManagement],
            });
            await access.ReviewAsync(submitted.Id, admin.Id, new ReviewAccessRequestRequest { Approve = true });

            var grant = await _db.Context.SubAdminPermissions.AsNoTracking()
                .SingleAsync(p => p.UserId == subAdmin.Id && p.Module == PermissionModule.CourseBatchManagement);
            Assert.True(grant.CanCreate); // untouched, not reset to View-only
            Assert.True(grant.CanEdit);
        }

        [Fact]
        public async Task RejectAccessRequest_GrantsNoPermissions()
        {
            var subAdmin = await _db.SeedUserAsync($"rm-{Guid.NewGuid():N}@test.com", "x", UserRole.SubAdmin);
            var admin = await _db.SeedUserAsync($"admin-{Guid.NewGuid():N}@test.com", "x", UserRole.Admin);
            var access = CreateAccessRequestService();

            var submitted = await access.SubmitAsync(subAdmin.Id, new SubmitAccessRequestRequest
            {
                RequestedModules = [PermissionModule.CourseBatchManagement],
            });
            await access.ReviewAsync(submitted.Id, admin.Id, new ReviewAccessRequestRequest { Approve = false });

            Assert.Empty(await _db.Context.SubAdminPermissions.Where(p => p.UserId == subAdmin.Id).ToListAsync());
        }

        [Fact]
        public async Task GetCurrentAccess_ReflectsAPermissionRevokedAfterLogin_WithoutANewLogin()
        {
            // Backs Program.cs's OnTokenValidated: a permission grant baked into a JWT at login
            // used to stay valid for that token's whole lifetime even after being revoked. This
            // proves the underlying read this fix relies on is genuinely live — same UnitOfWork
            // session throughout, no re-login, no new token — the way a real request's DB round
            // trip would see it after the app itself changed the same rows out from under it.
            var subAdminUser = await _db.SeedUserAsync($"sub-{Guid.NewGuid():N}@test.com", "x", UserRole.SubAdmin);
            var otherAdmin = await _db.SeedUserAsync($"admin-{Guid.NewGuid():N}@test.com", "x", UserRole.Admin);
            _db.Context.SubAdminPermissions.Add(new SubAdminPermission
            {
                UserId = subAdminUser.Id,
                Module = PermissionModule.BillingFinance,
                CanView = true,
            });
            await _db.Context.SaveChangesAsync();
            // UserService.SetPermissionsAsync below re-queries and removes this same row;
            // production hands each request its own DbContext, so the seed instance must not
            // stay tracked here (mirrors the same pattern used for RoleService tests).
            _db.Context.ChangeTracker.Clear();

            var auth = CreateAuthService();
            var beforeRevoke = await auth.GetCurrentAccessAsync(subAdminUser.Id);
            Assert.Contains($"{PermissionModule.BillingFinance}:{PermissionAction.View}", beforeRevoke!.Permissions);

            // Revoke it — the same "replace-all" path the real Permissions screen uses.
            await CreateUserService().SetPermissionsAsync(subAdminUser.Id, otherAdmin.Id, []);

            var afterRevoke = await auth.GetCurrentAccessAsync(subAdminUser.Id);
            Assert.DoesNotContain($"{PermissionModule.BillingFinance}:{PermissionAction.View}", afterRevoke!.Permissions);
        }

        [Fact]
        public async Task Login_Blocks_InactiveUser()
        {
            await _db.SeedUserAsync("gone@test.com", _hasher.Hash("4821"), status: UserStatus.Inactive);

            await Assert.ThrowsAsync<UnauthorizedException>(() =>
                CreateAuthService().LoginAsync(new LoginRequest { Email = "gone@test.com", Pin = "4821" }));
        }

        [Fact]
        public async Task RequestPinReset_ThenResetPin_ChangesPinAndBurnsToken()
        {
            var user = await _db.SeedUserAsync("reset@test.com", _hasher.Hash("1234"));
            var auth = CreateAuthService();

            await auth.RequestPinResetAsync(new ForgotPinRequest { Email = "reset@test.com" });

            var token = await _db.Context.PinResetTokens.FirstAsync(t => t.UserId == user.Id);
            Assert.Null(token.UsedAtUtc);
            Assert.Single(_emailSender.Sent); // the reset link actually went out

            await auth.ResetPinAsync(new ResetPinRequest { Token = token.Token, NewPin = "9999" });

            var reloaded = await _db.Context.Users.FirstAsync(u => u.Id == user.Id);
            Assert.True(_hasher.Verify("9999", reloaded.PinHash));
            Assert.False(_hasher.Verify("1234", reloaded.PinHash)); // old PIN no longer works
            var burnedToken = await _db.Context.PinResetTokens.FirstAsync(t => t.Id == token.Id);
            Assert.NotNull(burnedToken.UsedAtUtc);

            // Single-use: redeeming the same token again must fail, not silently reset again.
            await Assert.ThrowsAsync<DomainValidationException>(() =>
                auth.ResetPinAsync(new ResetPinRequest { Token = token.Token, NewPin = "0000" }));
        }

        [Fact]
        public async Task RequestPinReset_UnknownEmail_DoesNothingAndNeverThrows()
        {
            var auth = CreateAuthService();

            // No account with this email exists — must complete quietly (no enumeration signal),
            // never throw NotFoundException or anything else the caller could distinguish.
            await auth.RequestPinResetAsync(new ForgotPinRequest { Email = "nobody@test.com" });

            Assert.Empty(await _db.Context.PinResetTokens.ToListAsync());
            Assert.Empty(_emailSender.Sent);
        }

        [Fact]
        public async Task ResetPin_ExpiredToken_ThrowsAndLeavesPinUnchanged()
        {
            var user = await _db.SeedUserAsync("expired@test.com", _hasher.Hash("1234"));
            _db.Context.PinResetTokens.Add(new PinResetToken
            {
                UserId = user.Id,
                Token = "expired-token-value",
                ExpiresAtUtc = DateTime.UtcNow.AddMinutes(-5), // already expired
            });
            await _db.Context.SaveChangesAsync();

            await Assert.ThrowsAsync<DomainValidationException>(() =>
                CreateAuthService().ResetPinAsync(new ResetPinRequest { Token = "expired-token-value", NewPin = "5555" }));

            var reloaded = await _db.Context.Users.FirstAsync(u => u.Id == user.Id);
            Assert.True(_hasher.Verify("1234", reloaded.PinHash)); // unchanged
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
            Assert.Contains("PIN", email.Body);
        }

        [Fact]
        public async Task ListUsers_SkipsARowWithACorruptStoredEnumValue_InsteadOfFailingTheWholePage()
        {
            // Every enum column round-trips as a string; this simulates the real production
            // failure — a users.role value that doesn't match any current UserRole member
            // (stale data, a manual edit, whatever) — by writing one directly, bypassing EF's
            // type-safe API entirely, the same way corrupt data actually gets into a real
            // database.
            var good1 = await _db.SeedUserAsync("good1@test.com", "x", UserRole.Teacher);
            var corrupt = await _db.SeedUserAsync("corrupt@test.com", "x", UserRole.Teacher);
            var good2 = await _db.SeedUserAsync("good2@test.com", "x", UserRole.Teacher);

            await _db.Context.Database.ExecuteSqlRawAsync(
                "UPDATE users SET role = 'NotARealRole' WHERE id = {0}", corrupt.Id);

            var service = CreateUserService();
            var page = await service.ListAsync(role: null, search: null, page: 1, pageSize: 100);

            // The corrupt row is skipped, not defaulted to some guessed role — but it doesn't
            // take the other two rows down with it the way a single ToListAsync() over the
            // whole batch used to.
            Assert.Contains(page.Items, u => u.Id == good1.Id);
            Assert.Contains(page.Items, u => u.Id == good2.Id);
            Assert.DoesNotContain(page.Items, u => u.Id == corrupt.Id);
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

        /// <summary>
        /// Regression test: CreateAsync used to accept any existing RoleDefinitionId for a
        /// new Sub Admin account without checking it against NonSubAdminPresetNames — the
        /// same reserved-name guard UsersController.ApplyPermissionPreset already enforced
        /// for re-assigning an *existing* account's preset. A real production account ended
        /// up with the zero-permission "student" system role (seeded only to back the
        /// Parent's own "Student View" preview) stamped on at creation time as a result.
        /// </summary>
        [Fact]
        public async Task CreateUser_SubAdminWithReservedSystemRolePreset_Throws()
        {
            var studentRole = new RoleDefinition
            {
                Name = "student",
                DisplayName = "Student",
                DefaultRoute = "/student",
                IsSystem = true,
            };
            _db.Context.Add(studentRole);
            await _db.Context.SaveChangesAsync();
            _db.Context.ChangeTracker.Clear();

            var service = CreateUserService();
            await Assert.ThrowsAsync<DomainValidationException>(() => service.CreateAsync(new CreateUserRequest
            {
                Email = $"reserved-preset-{Guid.NewGuid():N}@test.com",
                FirstName = "Someone",
                LastName = "Staff",
                Role = UserRole.SubAdmin,
                RoleDefinitionId = studentRole.Id,
            }));
        }

        /// <summary>
        /// Same reserved-name guard, exercised directly against SetPermissionsAsync rather
        /// than through UsersController.ApplyPermissionPreset — proves the invariant holds at
        /// the service layer itself, not only for the one controller action that currently
        /// remembers to check first.
        /// </summary>
        [Fact]
        public async Task SetPermissions_ReservedSystemRolePreset_Throws()
        {
            var adminUser = await _db.SeedUserAsync($"admin-{Guid.NewGuid():N}@test.com", "x", UserRole.Admin);
            var subAdmin = await _db.SeedUserAsync($"sub-{Guid.NewGuid():N}@test.com", "x", UserRole.SubAdmin);
            var studentRole = new RoleDefinition
            {
                Name = "student",
                DisplayName = "Student",
                DefaultRoute = "/student",
                IsSystem = true,
            };
            _db.Context.Add(studentRole);
            await _db.Context.SaveChangesAsync();

            var service = CreateUserService();
            await Assert.ThrowsAsync<DomainValidationException>(() =>
                service.SetPermissionsAsync(subAdmin.Id, adminUser.Id, [], studentRole.Id));

            var reloaded = await _db.Context.Users.FirstAsync(u => u.Id == subAdmin.Id);
            Assert.Null(reloaded.RoleDefinitionId);
        }

        [Fact]
        public async Task DeleteUser_SoftDeletes_AndFreesUpEmailForReuse()
        {
            var service = CreateUserService();
            var teacherUser = await _db.SeedUserAsync($"del-{Guid.NewGuid():N}@test.com", "x", UserRole.Teacher);
            var email = teacherUser.Email;
            var adminUser = await _db.SeedUserAsync($"admin-{Guid.NewGuid():N}@test.com", "x", UserRole.Admin);

            await service.DeleteAsync(teacherUser.Id, adminUser.Id);

            await Assert.ThrowsAsync<NotFoundException>(() => service.GetAsync(teacherUser.Id));

            // The email should be free again since the query filter excludes soft-deleted rows.
            var dto = await service.CreateAsync(new CreateUserRequest
            {
                Email = email,
                FirstName = "New",
                LastName = "Teacher",
                Role = UserRole.Teacher,
            });
            Assert.Equal(email.ToLowerInvariant(), dto.Email);
        }

        [Fact]
        public async Task DeleteUser_RefusesSelfDelete_AndLastAdmin()
        {
            var service = CreateUserService();
            var admin = await _db.SeedUserAsync($"solo-admin-{Guid.NewGuid():N}@test.com", "x", UserRole.Admin);
            var otherAdmin = await _db.SeedUserAsync($"other-admin-{Guid.NewGuid():N}@test.com", "x", UserRole.Admin);

            await Assert.ThrowsAsync<DomainValidationException>(() => service.DeleteAsync(admin.Id, admin.Id));

            // Two admins exist — deleting one, called by another Admin, is fine.
            await service.DeleteAsync(otherAdmin.Id, admin.Id);

            // Only "admin" remains now. Its own DeleteAsync(admin.Id, admin.Id) is blocked by
            // the self-delete guard above before it ever reaches the "last admin" ceiling — with
            // the caller-must-be-Admin rule this file also adds (see the test below), that
            // ceiling can no longer be reached via any other legitimate caller either, since a
            // distinct Admin caller always implies at least one admin besides the target still
            // exists. It stays in the code as a defense-in-depth backstop, not dead weight to
            // delete, but there is no longer a legitimate call shape left to pin it against.
        }

        /// <summary>
        /// BUG (authorization audit, 2026-08-22): DeleteAsync's "last admin" guard checked
        /// only how many Admin rows existed, never who was calling — a Sub Admin holding
        /// nothing more than UserManagement:Delete could remove any *non-last* Admin account
        /// outright. Only a genuine Admin caller may delete another Admin account at all.
        /// </summary>
        [Fact]
        public async Task DeleteUser_RefusesANonAdminCaller_DeletingAnAdminAccount()
        {
            var service = CreateUserService();
            var admin = await _db.SeedUserAsync($"admin-{Guid.NewGuid():N}@test.com", "x", UserRole.Admin);
            var otherAdmin = await _db.SeedUserAsync($"other-admin-{Guid.NewGuid():N}@test.com", "x", UserRole.Admin);
            var subAdmin = await _db.SeedUserAsync($"sa-{Guid.NewGuid():N}@test.com", "x", UserRole.SubAdmin);

            await Assert.ThrowsAsync<ForbiddenException>(() => service.DeleteAsync(otherAdmin.Id, subAdmin.Id));

            // The rejected attempt must not have deleted anything.
            Assert.NotNull(await _db.Context.Users.FirstOrDefaultAsync(u => u.Id == otherAdmin.Id));
        }

        [Fact]
        public async Task ChangeUserRole_SwapsProfile_WhenNoOperationalHistory()
        {
            var service = CreateUserService();
            var parentUser = await _db.SeedUserAsync($"p-{Guid.NewGuid():N}@test.com", "x", UserRole.Parent);
            _db.Context.ParentProfiles.Add(new ParentProfile { UserId = parentUser.Id });
            await _db.Context.SaveChangesAsync();

            var dto = await service.ChangeRoleAsync(parentUser.Id, UserRole.Teacher);

            Assert.Equal(UserRole.Teacher, dto.Role);
            Assert.False(await _db.Context.ParentProfiles.AnyAsync(p => p.UserId == parentUser.Id));
            Assert.True(await _db.Context.TeacherProfiles.AnyAsync(t => t.UserId == parentUser.Id));
        }

        [Fact]
        public async Task ChangeUserRole_RefusesParentWithChildren_AndTeacherWithSessions()
        {
            var service = CreateUserService();

            var (batch, _, session) = await SeedBatchWithSessionAsync(totalSessions: 1);
            var teacherUserId = session.TeacherProfileId;
            var teacherUser = await _db.Context.TeacherProfiles.Where(t => t.Id == teacherUserId).Select(t => t.UserId).FirstAsync();
            await Assert.ThrowsAsync<ConflictException>(() => service.ChangeRoleAsync(teacherUser, UserRole.AdmissionTeam));

            var parentUser = await _db.SeedUserAsync($"p-{Guid.NewGuid():N}@test.com", "x", UserRole.Parent);
            var parentProfile = new ParentProfile { UserId = parentUser.Id };
            _db.Context.ParentProfiles.Add(parentProfile);
            await _db.Context.SaveChangesAsync();
            _db.Context.Children.Add(new Child { ParentProfileId = parentProfile.Id, FirstName = "Kid", LastName = "Test" });
            await _db.Context.SaveChangesAsync();

            await Assert.ThrowsAsync<ConflictException>(() => service.ChangeRoleAsync(parentUser.Id, UserRole.Teacher));
        }

        [Fact]
        public async Task ChangeUserRole_RefusesAdminAsSourceOrTarget()
        {
            var service = CreateUserService();
            var admin = await _db.SeedUserAsync($"admin-{Guid.NewGuid():N}@test.com", "x", UserRole.Admin);
            var parentUser = await _db.SeedUserAsync($"p-{Guid.NewGuid():N}@test.com", "x", UserRole.Parent);
            _db.Context.ParentProfiles.Add(new ParentProfile { UserId = parentUser.Id });
            await _db.Context.SaveChangesAsync();

            await Assert.ThrowsAsync<DomainValidationException>(() => service.ChangeRoleAsync(admin.Id, UserRole.Teacher));
            await Assert.ThrowsAsync<DomainValidationException>(() => service.ChangeRoleAsync(parentUser.Id, UserRole.Admin));
        }

        [Fact]
        public async Task ParentSchedule_IncludesDemoSession_ForLeadWithNoEnrolledChildYet()
        {
            var parentEmail = $"lead-{Guid.NewGuid():N}@test.com";
            var parentUser = await _db.SeedUserAsync(parentEmail, "x", UserRole.Parent);
            _db.Context.ParentProfiles.Add(new ParentProfile { UserId = parentUser.Id });

            var teacherUser = await _db.SeedUserAsync($"t-{Guid.NewGuid():N}@test.com", "x", UserRole.Teacher);
            var teacher = new TeacherProfile { UserId = teacherUser.Id };
            _db.Context.TeacherProfiles.Add(teacher);
            await _db.Context.SaveChangesAsync();

            var demoStart = DateTime.UtcNow.AddDays(1);
            var demoSession = new ClassSession
            {
                BatchId = null,
                TeacherProfile = teacher,
                Type = SessionType.Demo,
                Status = SessionStatus.Scheduled,
                ScheduledStartAtUtc = demoStart,
                ScheduledEndAtUtc = demoStart.AddMinutes(30),
            };
            _db.Context.ClassSessions.Add(demoSession);
            _db.Context.DemoBookings.Add(new DemoBooking
            {
                ClassSession = demoSession,
                ParentName = "Lead Parent",
                ParentEmail = parentEmail,
                ChildName = "Prospective Kid",
            });
            await _db.Context.SaveChangesAsync();

            var service = new ParentPortalService(_db.UnitOfWork);
            var schedule = await service.GetScheduleAsync(
                parentUser.Id, demoStart.AddDays(-1), demoStart.AddDays(2));

            var found = Assert.Single(schedule);
            Assert.Equal(demoSession.Id, found.Id);
            Assert.Equal(SessionType.Demo, found.Type);
            // DemoBooking has no Child FK to populate ChildIds from (still empty here, by
            // design), but its own free-text ChildName should carry through so the portal
            // can at least label whose demo this is.
            Assert.Empty(found.ChildIds);
            Assert.Equal("Prospective Kid", found.DemoChildName);
        }

        [Fact]
        public async Task CreateDemoBooking_ConfirmationEmail_IncludesJitsiJoinLink()
        {
            var teacherUser = await _db.SeedUserAsync($"t-{Guid.NewGuid():N}@test.com", "x", UserRole.Teacher);
            var teacher = new TeacherProfile { UserId = teacherUser.Id };
            _db.Context.TeacherProfiles.Add(teacher);
            await _db.Context.SaveChangesAsync();

            var dto = await CreateDemoBookingService().CreateAsync(new CreateDemoBookingRequest
            {
                ParentName = "Lead Parent",
                ParentEmail = "lead@test.com",
                ChildName = "Kid",
                TeacherProfileId = teacher.Id,
                ScheduledStartAtUtc = DateTime.UtcNow.AddDays(1),
                ScheduledEndAtUtc = DateTime.UtcNow.AddDays(1).AddMinutes(30),
            });

            var email = Assert.Single(_emailSender.Sent, e => e.To == "lead@test.com");
            Assert.Contains($"https://meet.techmisai.com/{dto.MeetingRoomId}", email.Body);
        }

        [Fact]
        public async Task CreateDemoBooking_SucceedsAndReturnsTheBooking_EvenWhenConfirmationEmailFails()
        {
            var teacherUser = await _db.SeedUserAsync($"t-{Guid.NewGuid():N}@test.com", "x", UserRole.Teacher);
            var teacher = new TeacherProfile { UserId = teacherUser.Id };
            _db.Context.TeacherProfiles.Add(teacher);
            await _db.Context.SaveChangesAsync();

            var service = new DemoBookingService(
                _db.UnitOfWork, _auditLog, new ThrowingEmailSender(), _emailTemplates,
                new FakeCrmNotifier(), new FakeJitsiTokenService(), _notifications, CreateUserService(), NullLogger<DemoBookingService>.Instance);

            // An SMTP failure (confirmed in production logs as an uncaught exception here) must
            // not turn an already-committed booking into a 500 — the booking is real by the time
            // this code runs, and a delivery failure shouldn't undo confirming that to the caller.
            var dto = await service.CreateAsync(new CreateDemoBookingRequest
            {
                ParentName = "Lead Parent",
                ParentEmail = "lead2@test.com",
                ChildName = "Kid",
                TeacherProfileId = teacher.Id,
                ScheduledStartAtUtc = DateTime.UtcNow.AddDays(1),
                ScheduledEndAtUtc = DateTime.UtcNow.AddDays(1).AddMinutes(30),
            });

            Assert.NotEqual(Guid.Empty, dto.Id);
            Assert.NotNull(await _db.Context.DemoBookings.FirstOrDefaultAsync(b => b.Id == dto.Id));
        }

        [Fact]
        public async Task ReassignTeacher_NotifiesBothTeachers_AndRecordsAuditHistory()
        {
            var oldTeacherUser = await _db.SeedUserAsync($"old-{Guid.NewGuid():N}@test.com", "x", UserRole.Teacher);
            var newTeacherUser = await _db.SeedUserAsync($"new-{Guid.NewGuid():N}@test.com", "x", UserRole.Teacher);
            var oldTeacher = new TeacherProfile { UserId = oldTeacherUser.Id };
            var newTeacher = new TeacherProfile { UserId = newTeacherUser.Id };
            _db.Context.TeacherProfiles.AddRange(oldTeacher, newTeacher);
            await _db.Context.SaveChangesAsync();

            var demoBooking = CreateDemoBookingService();
            var booking = await demoBooking.CreateAsync(new CreateDemoBookingRequest
            {
                ParentName = "Parent", ParentEmail = "reassign-parent@test.com", ChildName = "Kid",
                TeacherProfileId = oldTeacher.Id,
                ScheduledStartAtUtc = DateTime.UtcNow.AddDays(1),
                ScheduledEndAtUtc = DateTime.UtcNow.AddDays(1).AddMinutes(30),
            });
            _emailSender.Sent.Clear();

            var reassigned = await demoBooking.ReassignTeacherAsync(booking.Id,
                new ReassignTeacherRequest { TeacherProfileId = newTeacher.Id, Reason = "Original teacher called in sick" });

            Assert.Equal(newTeacher.Id, reassigned.TeacherProfileId);

            // Regression: overriding a demo's assigned teacher had no notification path at all --
            // the newly-assigned teacher had to check their dashboard to find out, and the
            // displaced teacher kept preparing for a demo that was no longer theirs.
            Assert.Contains(_emailSender.Sent, m => m.To == newTeacherUser.Email && m.Subject.Contains("assigned a demo"));
            Assert.Contains(_emailSender.Sent, m => m.To == oldTeacherUser.Email && m.Subject.Contains("unassigned"));

            var history = await demoBooking.GetReassignmentHistoryAsync(booking.Id);
            var entry = Assert.Single(history);
            Assert.Equal("Original teacher called in sick", entry.Reason);
            Assert.Equal($"{newTeacherUser.FirstName} {newTeacherUser.LastName}", entry.NewTeacherName);
            Assert.Equal($"{oldTeacherUser.FirstName} {oldTeacherUser.LastName}", entry.OldTeacherName);
        }

        [Fact]
        public async Task ReassignTeacher_RejectsWhenNewTeacherAlreadyBusyAtThatSlot()
        {
            var teacherAUser = await _db.SeedUserAsync($"a-{Guid.NewGuid():N}@test.com", "x", UserRole.Teacher);
            var teacherBUser = await _db.SeedUserAsync($"b-{Guid.NewGuid():N}@test.com", "x", UserRole.Teacher);
            var teacherA = new TeacherProfile { UserId = teacherAUser.Id };
            var teacherB = new TeacherProfile { UserId = teacherBUser.Id };
            _db.Context.TeacherProfiles.AddRange(teacherA, teacherB);
            await _db.Context.SaveChangesAsync();

            var demoBooking = CreateDemoBookingService();
            var slotStart = DateTime.UtcNow.AddDays(2);

            // Teacher B is already busy with this other booking at the exact slot in question.
            await demoBooking.CreateAsync(new CreateDemoBookingRequest
            {
                ParentName = "Other Parent", ParentEmail = "other-parent@test.com", ChildName = "Other Kid",
                TeacherProfileId = teacherB.Id,
                ScheduledStartAtUtc = slotStart,
                ScheduledEndAtUtc = slotStart.AddMinutes(30),
            });

            var booking = await demoBooking.CreateAsync(new CreateDemoBookingRequest
            {
                ParentName = "Parent", ParentEmail = "busy-conflict-parent@test.com", ChildName = "Kid",
                TeacherProfileId = teacherA.Id,
                ScheduledStartAtUtc = slotStart,
                ScheduledEndAtUtc = slotStart.AddMinutes(30),
            });

            await Assert.ThrowsAsync<DomainValidationException>(() =>
                demoBooking.ReassignTeacherAsync(booking.Id, new ReassignTeacherRequest { TeacherProfileId = teacherB.Id }));
        }

        [Fact]
        public async Task ReassignTeacher_RejectsOnceTheDemoIsNoLongerScheduled()
        {
            var oldTeacherUser = await _db.SeedUserAsync($"old2-{Guid.NewGuid():N}@test.com", "x", UserRole.Teacher);
            var newTeacherUser = await _db.SeedUserAsync($"new2-{Guid.NewGuid():N}@test.com", "x", UserRole.Teacher);
            var oldTeacher = new TeacherProfile { UserId = oldTeacherUser.Id };
            var newTeacher = new TeacherProfile { UserId = newTeacherUser.Id };
            _db.Context.TeacherProfiles.AddRange(oldTeacher, newTeacher);
            await _db.Context.SaveChangesAsync();

            var demoBooking = CreateDemoBookingService();
            var booking = await demoBooking.CreateAsync(new CreateDemoBookingRequest
            {
                ParentName = "Parent", ParentEmail = "already-done-parent@test.com", ChildName = "Kid",
                TeacherProfileId = oldTeacher.Id,
                ScheduledStartAtUtc = DateTime.UtcNow.AddDays(1),
                ScheduledEndAtUtc = DateTime.UtcNow.AddDays(1).AddMinutes(30),
            });

            // Regression: the frontend only offers "Reassign" for a still-scheduled demo, but
            // that's a UI convenience, not the actual boundary -- a booking reached directly by
            // id (already completed, cancelled, or converted) must not be silently reassignable,
            // which would email a teacher about a demo that has nothing to do with them anymore.
            await demoBooking.UpdateConversionStatusAsync(booking.Id, new UpdateConversionStatusRequest { ConversionStatus = ConversionStatus.NotInterested });

            await Assert.ThrowsAsync<DomainValidationException>(() =>
                demoBooking.ReassignTeacherAsync(booking.Id, new ReassignTeacherRequest { TeacherProfileId = newTeacher.Id }));
        }

        [Fact]
        public async Task ReadyForEnrollment_CreatesTheParentsLoginAndEmailsCredentials()
        {
            var teacherUser = await _db.SeedUserAsync($"t-{Guid.NewGuid():N}@test.com", "x", UserRole.Teacher);
            var teacher = new TeacherProfile { UserId = teacherUser.Id };
            _db.Context.TeacherProfiles.Add(teacher);
            await _db.Context.SaveChangesAsync();

            var demoBooking = CreateDemoBookingService();
            var booking = await demoBooking.CreateAsync(new CreateDemoBookingRequest
            {
                ParentName = "Rhea Kapoor", ParentEmail = $"rfe-{Guid.NewGuid():N}@test.com", ParentPhone = "9000000000",
                ChildName = "Kid", TeacherProfileId = teacher.Id,
                ScheduledStartAtUtc = DateTime.UtcNow.AddDays(1),
                ScheduledEndAtUtc = DateTime.UtcNow.AddDays(1).AddMinutes(30),
            });
            _emailSender.Sent.Clear(); // discard the booking-confirmation email; only the credentials email matters here

            await demoBooking.UpdateConversionStatusAsync(
                booking.Id, new UpdateConversionStatusRequest { ConversionStatus = ConversionStatus.ReadyForEnrollment });

            var user = await _db.Context.Users.AsNoTracking().SingleAsync(u => u.Email == booking.ParentEmail.ToLowerInvariant());
            Assert.Equal(UserRole.Parent, user.Role);
            Assert.Equal("Rhea", user.FirstName);
            Assert.Equal("Kapoor", user.LastName);
            Assert.Single(_db.Context.ParentProfiles.Where(p => p.UserId == user.Id));

            var email = Assert.Single(_emailSender.Sent);
            Assert.Contains("PIN", email.Body);
        }

        [Fact]
        public async Task ReadyForEnrollment_ReusesAnExistingAccount_InsteadOfCreatingADuplicateOrResendingCredentials()
        {
            // The sibling/repeat-lead case: this parent already has a login from an earlier
            // demo (or was added directly through Users) -- moving a second demo to
            // ReadyForEnrollment must not create a second account for the same email, and
            // must not re-email credentials to someone who can already sign in.
            var teacherUser = await _db.SeedUserAsync($"t-{Guid.NewGuid():N}@test.com", "x", UserRole.Teacher);
            var teacher = new TeacherProfile { UserId = teacherUser.Id };
            _db.Context.TeacherProfiles.Add(teacher);
            await _db.Context.SaveChangesAsync();

            var parentEmail = $"existing-{Guid.NewGuid():N}@test.com";
            await CreateUserService().CreateAsync(new CreateUserRequest
            {
                Email = parentEmail, FirstName = "Existing", LastName = "Parent", Role = UserRole.Parent,
            });
            _emailSender.Sent.Clear();

            var demoBooking = CreateDemoBookingService();
            var booking = await demoBooking.CreateAsync(new CreateDemoBookingRequest
            {
                ParentName = "Existing Parent", ParentEmail = parentEmail, TeacherProfileId = teacher.Id,
                ChildName = "Second Kid",
                ScheduledStartAtUtc = DateTime.UtcNow.AddDays(1),
                ScheduledEndAtUtc = DateTime.UtcNow.AddDays(1).AddMinutes(30),
            });
            _emailSender.Sent.Clear(); // discard the booking-confirmation email too

            await demoBooking.UpdateConversionStatusAsync(
                booking.Id, new UpdateConversionStatusRequest { ConversionStatus = ConversionStatus.ReadyForEnrollment });

            Assert.Equal(1, await _db.Context.Users.CountAsync(u => u.Email == parentEmail));
            Assert.Empty(_emailSender.Sent); // no re-sent credentials
        }

        [Fact]
        public async Task TeacherWorkload_FlagsBusyTeacherAndOrdersFreeTeachersFirst()
        {
            var freeTeacherUser = await _db.SeedUserAsync($"free-{Guid.NewGuid():N}@test.com", "x", UserRole.Teacher);
            var busyTeacherUser = await _db.SeedUserAsync($"busy-{Guid.NewGuid():N}@test.com", "x", UserRole.Teacher);
            var freeTeacher = new TeacherProfile { UserId = freeTeacherUser.Id };
            var busyTeacher = new TeacherProfile { UserId = busyTeacherUser.Id };
            _db.Context.TeacherProfiles.AddRange(freeTeacher, busyTeacher);
            await _db.Context.SaveChangesAsync();

            var demoBooking = CreateDemoBookingService();
            var slotStart = DateTime.UtcNow.AddDays(3);

            await demoBooking.CreateAsync(new CreateDemoBookingRequest
            {
                ParentName = "Other Parent", ParentEmail = "workload-other@test.com", ChildName = "Other Kid",
                TeacherProfileId = busyTeacher.Id,
                ScheduledStartAtUtc = slotStart,
                ScheduledEndAtUtc = slotStart.AddMinutes(30),
            });

            var booking = await demoBooking.CreateAsync(new CreateDemoBookingRequest
            {
                ParentName = "Parent", ParentEmail = "workload-parent@test.com", ChildName = "Kid",
                TeacherProfileId = freeTeacher.Id,
                ScheduledStartAtUtc = slotStart,
                ScheduledEndAtUtc = slotStart.AddMinutes(30),
            });

            var workload = await demoBooking.GetTeacherWorkloadAsync(booking.Id);

            var busyEntry = workload.Single(w => w.TeacherProfileId == busyTeacher.Id);
            Assert.True(busyEntry.IsBusyAtSlot);
            var freeIndex = workload.ToList().FindIndex(w => w.TeacherProfileId == freeTeacher.Id);
            var busyIndex = workload.ToList().FindIndex(w => w.TeacherProfileId == busyTeacher.Id);
            Assert.True(freeIndex < busyIndex);
        }

        [Fact]
        public async Task CreateDemoBooking_RejectsExplicitTeacher_AlreadyBookedAtThatTime()
        {
            var teacherUser = await _db.SeedUserAsync($"t-{Guid.NewGuid():N}@test.com", "x", UserRole.Teacher);
            var teacher = new TeacherProfile { UserId = teacherUser.Id };
            _db.Context.TeacherProfiles.Add(teacher);
            await _db.Context.SaveChangesAsync();

            var start = DateTime.UtcNow.AddDays(1);
            var end = start.AddMinutes(30);
            var service = CreateDemoBookingService();

            await service.CreateAsync(new CreateDemoBookingRequest
            {
                ParentName = "First Parent",
                ParentEmail = "first@test.com",
                ChildName = "Kid One",
                TeacherProfileId = teacher.Id,
                ScheduledStartAtUtc = start,
                ScheduledEndAtUtc = end,
            });

            // Same teacher, overlapping slot, explicitly requested this time instead of
            // auto-assigned — must be rejected the same way auto-assign already avoids it.
            await Assert.ThrowsAsync<DomainValidationException>(() => service.CreateAsync(new CreateDemoBookingRequest
            {
                ParentName = "Second Parent",
                ParentEmail = "second@test.com",
                ChildName = "Kid Two",
                TeacherProfileId = teacher.Id,
                ScheduledStartAtUtc = start.AddMinutes(10),
                ScheduledEndAtUtc = end.AddMinutes(10),
            }));

            Assert.Equal(1, await _db.Context.DemoBookings.CountAsync(b => b.ChildName == "Kid One" || b.ChildName == "Kid Two"));
        }

        [Fact]
        public void JitsiLinkBuilder_UsesConfiguredDomain_WhenIntegrationConfigured()
        {
            var url = JitsiLinkBuilder.BuildJoinUrl("trn-abc123", """{"domain":"meet.example.org"}""");
            Assert.Equal("https://meet.example.org/trn-abc123", url);
        }

        [Fact]
        public void JitsiLinkBuilder_FallsBackToDefaultDomain_WhenConfigMissingOrMalformed()
        {
            Assert.Equal("https://meet.techmisai.com/trn-abc123", JitsiLinkBuilder.BuildJoinUrl("trn-abc123", null));
            Assert.Equal("https://meet.techmisai.com/trn-abc123", JitsiLinkBuilder.BuildJoinUrl("trn-abc123", "not-json"));
        }

        [Fact]
        public void JitsiLinkBuilder_ReturnsNull_WhenNoMeetingRoom()
        {
            Assert.Null(JitsiLinkBuilder.BuildJoinUrl(null, """{"domain":"meet.example.org"}"""));
        }

        [Fact]
        public async Task CreateCourse_RejectsNonPositiveDuration_ButAllowsAnyCustomLength()
        {
            var courseService = CreateCourseService();
            var category = await courseService.CreateCategoryAsync(
                new CreateCourseCategoryRequest { Name = "Phonics", DepartmentId = WellKnownDepartments.Phonics });

            await Assert.ThrowsAsync<DomainValidationException>(() => courseService.CreateAsync(new SaveCourseRequest
            {
                CourseCategoryId = category.Id,
                Name = "Bad",
                Type = CourseType.Group,
                DurationMinutes = 0,
                Price = 1,
                TotalSessions = 1,
                DepartmentId = WellKnownDepartments.Phonics,
            }));

            // Regression: duration used to be locked to exactly 30/45/60 -- a centre running
            // shorter (10-minute) or longer (90-minute) classes had no way to configure that.
            var course = await courseService.CreateAsync(new SaveCourseRequest
            {
                CourseCategoryId = category.Id,
                Name = "Custom Length",
                Type = CourseType.Group,
                DurationMinutes = 50,
                Price = 1,
                TotalSessions = 1,
                DepartmentId = WellKnownDepartments.Phonics,
            });
            Assert.Equal(50, course.DurationMinutes);
        }

        /// <summary>
        /// Regression test: CreateCategoryAsync only checked the department existed
        /// (ExistsAsync) instead of fetching it, so the new CourseCategory's Department
        /// navigation was never set — ToDto() reads Department.Name, so the create response
        /// silently came back with departmentName "" even though ListCategoriesAsync (which
        /// does Include(c => c.Department)) shows the real name for the same row a moment
        /// later. Caught live against production while proving out the course-creation flow.
        /// </summary>
        [Fact]
        public async Task CreateCategory_ResponseIncludesRealDepartmentName_NotEmpty()
        {
            var courseService = CreateCourseService();
            var category = await courseService.CreateCategoryAsync(
                new CreateCourseCategoryRequest { Name = $"Cat-{Guid.NewGuid():N}", DepartmentId = WellKnownDepartments.Phonics });

            Assert.Equal("Phonics", category.DepartmentName);
        }

        [Fact]
        public async Task Departments_ListAsync_IncludesTheTwoSeededOnesByDefault()
        {
            var departments = CreateDepartmentService();
            var all = await departments.ListAsync();
            Assert.Contains(all, d => d.Id == WellKnownDepartments.Phonics && d.Name == "Phonics");
            Assert.Contains(all, d => d.Id == WellKnownDepartments.Maths && d.Name == "Maths");
        }

        [Fact]
        public async Task Departments_ListAsync_DefaultsToActiveOnly_MatchingCourseServicesConvention()
        {
            // Pins the exact bug found and fixed during review: DepartmentsController's
            // [FromQuery] bool with no explicit default binds to false when the query string
            // omits it, so the service's own default has to be false too or the two layers
            // silently disagree about what "no includeInactive given" means. ICourseService.
            // ListAsync's sibling default is false -- match it.
            var departments = CreateDepartmentService();
            var inactive = await departments.CreateAsync(new SaveDepartmentRequest { Name = "Retired Department", IsActive = false });

            var defaultList = await departments.ListAsync();
            Assert.DoesNotContain(defaultList, d => d.Id == inactive.Id);

            var explicitAll = await departments.ListAsync(includeInactive: true);
            Assert.Contains(explicitAll, d => d.Id == inactive.Id);
        }

        [Fact]
        public async Task Departments_CreateAsync_AddsANewOne_ImmediatelyUsableByListAsync()
        {
            var departments = CreateDepartmentService();
            var created = await departments.CreateAsync(new SaveDepartmentRequest { Name = "Hindi", Description = "Read and write Hindi.", IsActive = true });

            Assert.NotEqual(Guid.Empty, created.Id);
            Assert.Equal("Hindi", created.Name);
            Assert.True(created.IsActive);

            var all = await departments.ListAsync();
            Assert.Contains(all, d => d.Id == created.Id && d.Name == "Hindi");
        }

        [Fact]
        public async Task Departments_CreateAsync_AlsoCreatesItsPaymentAccount()
        {
            var departments = CreateDepartmentService();
            var created = await departments.CreateAsync(new SaveDepartmentRequest { Name = "Hindi", IsActive = true });

            var account = await _db.Context.PaymentAccounts.FirstOrDefaultAsync(a => a.DepartmentId == created.Id);
            Assert.NotNull(account);
            Assert.Equal("Hindi Department Account", account!.Name);
            // With nothing else configured yet, there's nothing to inherit from — this is the
            // "not wired to real money yet" placeholder an admin fills in via Payment Gateway
            // Mapping's edit dialog, same as the two the app originally shipped with.
            Assert.False(account.IsActive);
            Assert.Equal("pending-client-decision", account.GatewayAccountRef);
        }

        [Fact]
        public async Task Departments_CreateAsync_InheritsAnAlreadyConfiguredAccount_MostOrgsRunJustOne()
        {
            // A department that already has real, working gateway credentials -- most orgs
            // here genuinely run one account for the whole business, not one per department.
            _db.Context.PaymentAccounts.Add(new PaymentAccount
            {
                Name = "Phonics Department Account",
                DepartmentId = WellKnownDepartments.Phonics,
                GatewayProvider = "cashfree",
                GatewayAccountRef = "acc_real_configured_123",
                IsActive = true,
            });
            await _db.Context.SaveChangesAsync();

            var departments = CreateDepartmentService();
            var created = await departments.CreateAsync(new SaveDepartmentRequest { Name = "Abacus", IsActive = true });

            var account = await _db.Context.PaymentAccounts.FirstOrDefaultAsync(a => a.DepartmentId == created.Id);
            Assert.NotNull(account);
            // Immediately usable -- no separate setup step, matching how the org actually
            // operates (one real gateway account shared by every department by default).
            Assert.True(account!.IsActive);
            Assert.Equal("cashfree", account.GatewayProvider);
            Assert.Equal("acc_real_configured_123", account.GatewayAccountRef);
        }

        [Fact]
        public async Task UpdatePaymentAccount_AppliesToEveryDepartment_ByDefault()
        {
            var maths = new PaymentAccount { Name = "Maths Department Account", DepartmentId = WellKnownDepartments.Maths, GatewayProvider = "cashfree", GatewayAccountRef = "acc_maths_old", IsActive = true };
            var phonics = new PaymentAccount { Name = "Phonics Department Account", DepartmentId = WellKnownDepartments.Phonics, GatewayProvider = "razorpay", GatewayAccountRef = "acc_phonics_old", IsActive = true };
            _db.Context.PaymentAccounts.AddRange(maths, phonics);
            await _db.Context.SaveChangesAsync();

            var billing = CreateBillingService();
            await billing.UpdatePaymentAccountAsync(maths.Id, new UpdatePaymentAccountRequest
            {
                Name = "Maths Department Account",
                GatewayProvider = "razorpay",
                GatewayAccountRef = "acc_org_wide_123",
                IsActive = true,
            });

            var updatedPhonics = await _db.Context.PaymentAccounts.AsNoTracking().FirstAsync(a => a.Id == phonics.Id);
            Assert.Equal("razorpay", updatedPhonics.GatewayProvider);
            Assert.Equal("acc_org_wide_123", updatedPhonics.GatewayAccountRef);
            // Only the routing fields converge — the card label stays department-specific.
            Assert.Equal("Phonics Department Account", updatedPhonics.Name);
        }

        [Fact]
        public async Task UpdatePaymentAccount_LeavesOthersAlone_WhenApplyToAllDepartmentsIsFalse()
        {
            var maths = new PaymentAccount { Name = "Maths Department Account", DepartmentId = WellKnownDepartments.Maths, GatewayProvider = "cashfree", GatewayAccountRef = "acc_maths_old", IsActive = true };
            var phonics = new PaymentAccount { Name = "Phonics Department Account", DepartmentId = WellKnownDepartments.Phonics, GatewayProvider = "razorpay", GatewayAccountRef = "acc_phonics_old", IsActive = true };
            _db.Context.PaymentAccounts.AddRange(maths, phonics);
            await _db.Context.SaveChangesAsync();

            var billing = CreateBillingService();
            await billing.UpdatePaymentAccountAsync(maths.Id, new UpdatePaymentAccountRequest
            {
                Name = "Maths Department Account",
                GatewayProvider = "cashfree",
                GatewayAccountRef = "acc_maths_only_999",
                IsActive = true,
                ApplyToAllDepartments = false,
            });

            var stillPhonics = await _db.Context.PaymentAccounts.AsNoTracking().FirstAsync(a => a.Id == phonics.Id);
            Assert.Equal("razorpay", stillPhonics.GatewayProvider);
            Assert.Equal("acc_phonics_old", stillPhonics.GatewayAccountRef);
        }

        [Fact]
        public async Task Departments_CreateAsync_RejectsADuplicateName()
        {
            var departments = CreateDepartmentService();
            await departments.CreateAsync(new SaveDepartmentRequest { Name = "Abacus" });

            await Assert.ThrowsAsync<ConflictException>(() =>
                departments.CreateAsync(new SaveDepartmentRequest { Name = "Abacus" }));
        }

        [Fact]
        public async Task Departments_CreateAsync_TrimsTheNameBeforeCheckingForADuplicate()
        {
            // Guards against the classic bug: " Abacus" and "Abacus" treated as different
            // names because the duplicate check ran before trimming instead of after.
            var departments = CreateDepartmentService();
            await departments.CreateAsync(new SaveDepartmentRequest { Name = "Abacus" });

            await Assert.ThrowsAsync<ConflictException>(() =>
                departments.CreateAsync(new SaveDepartmentRequest { Name = "  Abacus  " }));
        }

        [Fact]
        public async Task Departments_UpdateAsync_RenamesAndCanDeactivate()
        {
            var departments = CreateDepartmentService();
            var created = await departments.CreateAsync(new SaveDepartmentRequest { Name = "Spoken English" });

            var updated = await departments.UpdateAsync(created.Id, new SaveDepartmentRequest
            {
                Name = "Public Speaking",
                Description = "Renamed from Spoken English.",
                IsActive = false,
            });

            Assert.Equal("Public Speaking", updated.Name);
            Assert.False(updated.IsActive);

            var all = await departments.ListAsync(includeInactive: true);
            Assert.Contains(all, d => d.Id == created.Id && d.Name == "Public Speaking" && !d.IsActive);

            var activeOnly = await departments.ListAsync(includeInactive: false);
            Assert.DoesNotContain(activeOnly, d => d.Id == created.Id);
        }

        [Fact]
        public async Task Departments_UpdateAsync_AllowsSavingWithItsOwnUnchangedName()
        {
            // Guards against the classic "duplicate name" false-positive: the update's own
            // conflict check must exclude the department being updated, not just any row.
            var departments = CreateDepartmentService();
            var created = await departments.CreateAsync(new SaveDepartmentRequest { Name = "Grammar" });

            var updated = await departments.UpdateAsync(created.Id, new SaveDepartmentRequest
            {
                Name = "Grammar",
                Description = "Same name, just adding a description.",
                IsActive = true,
            });

            Assert.Equal("Grammar", updated.Name);
        }

        [Fact]
        public async Task Departments_UpdateAsync_RejectsRenamingToAnotherDepartmentsName()
        {
            var departments = CreateDepartmentService();
            await departments.CreateAsync(new SaveDepartmentRequest { Name = "MathsLab" });
            var other = await departments.CreateAsync(new SaveDepartmentRequest { Name = "Bright Speakers Club" });

            await Assert.ThrowsAsync<ConflictException>(() =>
                departments.UpdateAsync(other.Id, new SaveDepartmentRequest { Name = "MathsLab" }));
        }

        [Fact]
        public async Task Departments_UpdateAsync_ThrowsNotFound_ForAnUnknownId()
        {
            var departments = CreateDepartmentService();
            await Assert.ThrowsAsync<NotFoundException>(() =>
                departments.UpdateAsync(Guid.NewGuid(), new SaveDepartmentRequest { Name = "Anything" }));
        }

        [Fact]
        public async Task Departments_NewlyCreatedOne_IsImmediatelyUsableForACourseCategory()
        {
            // End-to-end across the two services that actually matter for "add a department
            // and use it": DepartmentService.CreateAsync's row must be visible to
            // CourseService.CreateCategoryAsync's own lookup in the same unit of work.
            var departments = CreateDepartmentService();
            var courses = CreateCourseService();
            var hindi = await departments.CreateAsync(new SaveDepartmentRequest { Name = "Hindi Ki Pathshala" });

            var category = await courses.CreateCategoryAsync(
                new CreateCourseCategoryRequest { Name = "Hindi Level 1", DepartmentId = hindi.Id });

            Assert.Equal(hindi.Id, category.DepartmentId);
            Assert.Equal("Hindi Ki Pathshala", category.DepartmentName);
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
        public async Task Reschedule_RejectsHolidayDate()
        {
            // ScheduleAsync already blocked holidays; RescheduleAsync didn't — a reschedule
            // is a new calendar entry too, and was the one path that could land a class on one.
            var (_, _, session) = await SeedBatchWithSessionAsync(totalSessions: 2);
            var holidayDate = DateOnly.FromDateTime(session.ScheduledStartAtUtc.AddDays(3));
            _db.Context.Holidays.Add(new Holiday { Name = "Founders' Day", Date = holidayDate });
            await _db.Context.SaveChangesAsync();

            await Assert.ThrowsAsync<DomainValidationException>(() => CreateSessionService().RescheduleAsync(
                session.Id,
                new RescheduleSessionRequest
                {
                    ScheduledStartAtUtc = holidayDate.ToDateTime(TimeOnly.FromDateTime(session.ScheduledStartAtUtc)),
                    ScheduledEndAtUtc = holidayDate.ToDateTime(TimeOnly.FromDateTime(session.ScheduledEndAtUtc)),
                }));

            var untouched = await _db.Context.ClassSessions.FindAsync(session.Id);
            Assert.Equal(SessionStatus.Scheduled, untouched!.Status); // never rescheduled
        }

        [Fact]
        public async Task MarkNoShow_CarriedForwardSession_SkipsAHolidayOneWeekOut()
        {
            // The naive "+7 days" placement used to ignore the holiday calendar entirely.
            var (_, _, session) = await SeedBatchWithSessionAsync(totalSessions: 2);
            var oneWeekOut = DateOnly.FromDateTime(session.ScheduledStartAtUtc.AddDays(7));
            _db.Context.Holidays.Add(new Holiday { Name = "Regional Holiday", Date = oneWeekOut });
            await _db.Context.SaveChangesAsync();

            var carried = await CreateSessionService().MarkNoShowAsync(
                session.Id, new MarkNoShowRequest { Party = NoShowParty.Student });

            Assert.Equal(SessionStatus.CarriedForward, carried.Status);
            Assert.NotEqual(oneWeekOut, DateOnly.FromDateTime(carried.ScheduledStartAtUtc));
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
        public async Task CompleteSession_WithNoTeacherNotes_AutoGeneratesASummary_FromEngagementData()
        {
            var (_, _, session) = await SeedBatchWithSessionAsync(totalSessions: 1);
            await CreateSessionService().RecordEngagementAsync(session.Id, new RecordEngagementRequest
            {
                Events =
                [
                    new EngagementEntryDto { ParticipantName = "Kid One", Type = EngagementEventType.QuizAttempt, Value = 2 },
                    new EngagementEntryDto { ParticipantName = "Kid One", Type = EngagementEventType.QuizCorrect, Value = 1 },
                ],
            });

            var completed = await CreateSessionService().CompleteAsync(session.Id); // no Summary in the request

            Assert.False(string.IsNullOrWhiteSpace(completed.Summary));
            // QuizAttempts (per GetEngagementSummaryAsync) sums BOTH QuizAttempt and QuizCorrect
            // event values, so posting Attempt=2 + Correct=1 totals 3 attempts, 1 correct.
            Assert.Contains("1/3 quiz answers correct", completed.Summary);
        }

        [Fact]
        public async Task CompleteSession_WithNoEngagementAtAll_StillGetsAFallbackSummary()
        {
            var (_, _, session) = await SeedBatchWithSessionAsync(totalSessions: 1);

            var completed = await CreateSessionService().CompleteAsync(session.Id);

            Assert.False(string.IsNullOrWhiteSpace(completed.Summary));
        }

        [Fact]
        public async Task CompleteSession_WithTeacherNotes_KeepsThemInsteadOfAutoGenerating()
        {
            var (_, _, session) = await SeedBatchWithSessionAsync(totalSessions: 1);

            var completed = await CreateSessionService().CompleteAsync(
                session.Id, new CompleteSessionRequest { Summary = "Covered short vowels, both kids engaged well." });

            Assert.Equal("Covered short vowels, both kids engaged well.", completed.Summary);
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
                DepartmentId = WellKnownDepartments.Phonics,
                GatewayProvider = "test",
                GatewayAccountRef = "acc-1",
            });
            await _db.Context.SaveChangesAsync();

            var billing = CreateBillingService();
            var invoice = await billing.CreateInvoiceAsync(new CreateInvoiceRequest
            {
                ParentProfileId = parentProfile.Id,
                DepartmentId = WellKnownDepartments.Phonics,
                Amount = 1000,
                DueDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(7)),
            });

            var paid = await billing.RecordPaymentAsync(invoice.Id, new RecordPaymentRequest { Amount = 1000 });

            Assert.Equal(InvoiceStatus.Paid, paid.Status);
            var transaction = Assert.Single(_db.Context.PaymentTransactions.ToList());
            Assert.StartsWith("RCP-", transaction.ReceiptNumber);
        }

        /// <summary>
        /// Same gap SettleGatewayTransactionAsync had (782d0d0): RecordPaymentAsync -- an admin
        /// manually recording a payment collected through any method, independent of both the
        /// gateway webhook and the parent's own cash-intent flow -- only ever notified Admins.
        /// Method-specific: a Cash recording should read as "cash payment" (matching
        /// ConfirmCashIntentAsync's own wording and carrying a receipt number), everything else
        /// as the generic gateway-style confirmation.
        /// </summary>
        [Fact]
        public async Task RecordPayment_NotifiesTheParent_WithMethodSpecificTemplate()
        {
            var cashParentUser = await _db.SeedUserAsync($"cash-{Guid.NewGuid():N}@test.com", "x", UserRole.Parent);
            var cashParentProfile = new ParentProfile { UserId = cashParentUser.Id };
            var cardParentUser = await _db.SeedUserAsync($"card-{Guid.NewGuid():N}@test.com", "x", UserRole.Parent);
            var cardParentProfile = new ParentProfile { UserId = cardParentUser.Id };
            _db.Context.AddRange(cashParentProfile, cardParentProfile);
            _db.Context.PaymentAccounts.Add(new PaymentAccount
            {
                Name = "Phonics", DepartmentId = WellKnownDepartments.Phonics, GatewayProvider = "test", GatewayAccountRef = "acc-1",
            });
            await _db.Context.SaveChangesAsync();

            var billing = CreateBillingService();
            var cashInvoice = await billing.CreateInvoiceAsync(new CreateInvoiceRequest
            {
                ParentProfileId = cashParentProfile.Id, DepartmentId = WellKnownDepartments.Phonics,
                Amount = 500, DueDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(7)),
            });
            var cardInvoice = await billing.CreateInvoiceAsync(new CreateInvoiceRequest
            {
                ParentProfileId = cardParentProfile.Id, DepartmentId = WellKnownDepartments.Phonics,
                Amount = 700, DueDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(7)),
            });
            _emailSender.Sent.Clear();

            await billing.RecordPaymentAsync(cashInvoice.Id, new RecordPaymentRequest { Amount = 500, Method = PaymentMethod.Cash });
            await billing.RecordPaymentAsync(cardInvoice.Id, new RecordPaymentRequest { Amount = 700, Method = PaymentMethod.Card });

            Assert.Contains(_emailSender.Sent, m => m.To == cashParentUser.Email && m.Subject.StartsWith("Cash payment received"));
            Assert.Contains(_emailSender.Sent, m => m.To == cardParentUser.Email && m.Subject.StartsWith("Payment received"));
        }

        [Fact]
        public async Task CreateInvoice_RoutesThroughParentAccountOverride_WhenSet()
        {
            var parentUser = await _db.SeedUserAsync($"map-{Guid.NewGuid():N}@test.com", "x", UserRole.Parent);
            var phonics = new PaymentAccount { Name = "Phonics", DepartmentId = WellKnownDepartments.Phonics, GatewayProvider = "t", GatewayAccountRef = "ph" };
            var maths = new PaymentAccount { Name = "Maths", DepartmentId = WellKnownDepartments.Maths, GatewayProvider = "t", GatewayAccountRef = "ma" };
            // Parent is pinned to the Maths account even though the invoice is a Phonics one.
            var parentProfile = new ParentProfile { UserId = parentUser.Id, PaymentAccount = maths };
            _db.Context.AddRange(phonics, maths, parentProfile);
            await _db.Context.SaveChangesAsync();

            var invoice = await CreateBillingService().CreateInvoiceAsync(new CreateInvoiceRequest
            {
                ParentProfileId = parentProfile.Id,
                DepartmentId = WellKnownDepartments.Phonics,
                Amount = 500,
                DueDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(7)),
            });

            var stored = await _db.Context.Invoices.FirstAsync(i => i.Id == invoice.Id);
            Assert.Equal(maths.Id, stored.PaymentAccountId); // override wins over the department account
            Assert.Equal(WellKnownDepartments.Phonics, stored.DepartmentId); // department still reflects the course
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
                DepartmentId = WellKnownDepartments.Maths,
                GatewayProvider = "test",
                GatewayAccountRef = "acc-2",
            });
            await _db.Context.SaveChangesAsync();

            var billing = CreateBillingService();
            var invoice = await billing.CreateInvoiceAsync(new CreateInvoiceRequest
            {
                ParentProfileId = parentProfile.Id,
                DepartmentId = WellKnownDepartments.Maths,
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
                DepartmentId = WellKnownDepartments.Phonics,
                GatewayProvider = "razorpay",
                GatewayAccountRef = "acc-1",
            });
            await _db.Context.SaveChangesAsync();

            var billing = CreateBillingService();
            var invoice = await billing.CreateInvoiceAsync(new CreateInvoiceRequest
            {
                ParentProfileId = parentProfile.Id,
                DepartmentId = WellKnownDepartments.Phonics,
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

            // Caught live: this used to only notify Admins -- a parent paying online got nothing
            // from the platform confirming it. Also proves the retry above didn't double-send.
            Assert.Single(_emailSender.Sent, m => m.To == parentUser.Email && m.Subject.StartsWith("Payment received"));
            var settled = await _db.Context.PaymentTransactions.SingleAsync(t => t.InvoiceId == invoice.Id);
            Assert.Equal(TransactionStatus.Success, settled.Status);
            Assert.StartsWith("RCP-", settled.ReceiptNumber);
            Assert.Contains("pay_123", settled.GatewayTransactionId);
        }

        [Fact]
        public async Task GatewaySettlement_CapsAtRemainingBalance_WhenAnotherPaymentAlreadyLanded()
        {
            // A parallel checkout attempt and a manual cash payment can both be in flight on
            // the same invoice; the gateway's late webhook must never push AmountPaid past
            // Amount just because the transaction it's settling was created for the full price.
            var parentUser = await _db.SeedUserAsync($"overpay-{Guid.NewGuid():N}@test.com", "x", UserRole.Parent);
            var parentProfile = new ParentProfile { UserId = parentUser.Id };
            _db.Context.ParentProfiles.Add(parentProfile);
            _db.Context.PaymentAccounts.Add(new PaymentAccount
            {
                Name = "Phonics", DepartmentId = WellKnownDepartments.Phonics, GatewayProvider = "razorpay", GatewayAccountRef = "acc-1",
            });
            await _db.Context.SaveChangesAsync();

            var billing = CreateBillingService();
            var invoice = await billing.CreateInvoiceAsync(new CreateInvoiceRequest
            {
                ParentProfileId = parentProfile.Id, DepartmentId = WellKnownDepartments.Phonics, Amount = 1000,
                DueDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(7)),
            });

            // Gateway checkout starts for the full amount...
            var checkout = await billing.InitiateParentPaymentAsync(
                parentUser.Id, invoice.Id, new InitiateParentPaymentRequest { MethodKey = "razorpay" });

            // ...but a cash payment covers part of the balance before the webhook arrives.
            await billing.RecordPaymentAsync(invoice.Id, new RecordPaymentRequest { Amount = 400 });
            Assert.Equal(InvoiceStatus.PartiallyPaid, (await _db.Context.Invoices.FindAsync(invoice.Id))!.Status);

            // The gateway now confirms the ORIGINAL full-amount transaction.
            await billing.SettleGatewayTransactionAsync(checkout.GatewayReference!, true, "pay_late", null);

            var settledInvoice = await _db.Context.Invoices.FirstAsync(i => i.Id == invoice.Id);
            Assert.Equal(InvoiceStatus.Paid, settledInvoice.Status);
            Assert.Equal(1000, settledInvoice.AmountPaid); // capped, not 400 + 1000 = 1400
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
                DepartmentId = WellKnownDepartments.Phonics,
                GatewayProvider = "razorpay",
                GatewayAccountRef = "acc-1",
            });
            await _db.Context.SaveChangesAsync();

            var gateway = new FakePaymentGateway();
            var billing = CreateBillingService(gateway);
            var invoice = await billing.CreateInvoiceAsync(new CreateInvoiceRequest
            {
                ParentProfileId = parentProfile.Id,
                DepartmentId = WellKnownDepartments.Phonics,
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
                DepartmentId = WellKnownDepartments.Maths,
                GatewayProvider = "cashfree",
                GatewayAccountRef = "acc-2",
            });
            await _db.Context.SaveChangesAsync();

            var billing = CreateBillingService();
            var invoice = await billing.CreateInvoiceAsync(new CreateInvoiceRequest
            {
                ParentProfileId = parentProfile.Id,
                DepartmentId = WellKnownDepartments.Maths,
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
                RatePerMinute = 1100,
                EffectiveFrom = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-30)),
            });

            await CreateSessionService().CompleteAsync(session.Id);

            var payout = Assert.Single(_db.Context.Payouts.ToList());
            var item = Assert.Single(_db.Context.PayoutItems.ToList());
            Assert.Equal(PayoutItemType.SessionEarning, item.Type);
            Assert.Equal(49500, item.Amount); // 1100/min * 45 min
            Assert.Equal(49500, payout.TotalAmount);
            Assert.Equal(PayoutStatus.Pending, payout.Status);
        }

        [Fact]
        public async Task StudentNoShow_AddsWaitingAmount_AndCarriesSessionForward()
        {
            var (_, _, session) = await SeedBatchWithSessionAsync(totalSessions: 2);
            await CreatePayoutService().SetRateAsync(new SavePayoutRateRequest
            {
                TeacherProfileId = session.TeacherProfileId,
                RatePerMinute = 1100,
                EffectiveFrom = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-30)),
            });

            var carried = await CreateSessionService().MarkNoShowAsync(
                session.Id, new MarkNoShowRequest { Party = NoShowParty.Student });

            var original = await _db.Context.ClassSessions.FindAsync(session.Id);
            Assert.Equal(SessionStatus.StudentNoShow, original!.Status);
            Assert.Equal(session.ScheduledStartAtUtc.AddDays(7), carried.ScheduledStartAtUtc);
            var item = Assert.Single(_db.Context.PayoutItems.ToList());
            Assert.Equal(PayoutItemType.StudentNoShowWaiting, item.Type);
            Assert.Equal(49500, item.Amount); // 1100/min * 45 min
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

        /// <summary>
        /// Makes the acting user a Teacher with no connection to <paramref name="session"/> —
        /// the "any teacher who knows a session id" attacker the session endpoints must reject.
        /// </summary>
        private async Task BecomeUnrelatedTeacherAsync()
        {
            var otherTeacherUser = await _db.SeedUserAsync($"t-other-{Guid.NewGuid():N}@test.com", "x", UserRole.Teacher);
            _db.Context.TeacherProfiles.Add(new TeacherProfile { UserId = otherTeacherUser.Id });
            await _db.Context.SaveChangesAsync();
            _db.CurrentUser.UserId = otherTeacherUser.Id;
        }

        [Fact]
        public async Task CompleteSession_Rejects_UnrelatedTeacher()
        {
            var (_, _, session) = await SeedBatchWithSessionAsync(totalSessions: 1);
            await BecomeUnrelatedTeacherAsync();

            // Completing accrues a payout against the session's OWN teacher, so an outsider
            // must not be able to trigger it by naming a session id.
            await Assert.ThrowsAsync<ForbiddenException>(() => CreateSessionService().CompleteAsync(session.Id));

            var untouched = await _db.Context.ClassSessions.FindAsync(session.Id);
            Assert.Equal(SessionStatus.Scheduled, untouched!.Status);
            Assert.Empty(_db.Context.PayoutItems.ToList());
        }

        [Fact]
        public async Task MarkNoShow_Rejects_UnrelatedTeacher()
        {
            var (_, _, session) = await SeedBatchWithSessionAsync(totalSessions: 1);
            await BecomeUnrelatedTeacherAsync();

            // A teacher no-show is a deduction on the assigned teacher's pay — one teacher
            // filing it against another's class would be a direct financial attack.
            await Assert.ThrowsAsync<ForbiddenException>(
                () => CreateSessionService().MarkNoShowAsync(session.Id, new MarkNoShowRequest { Party = NoShowParty.Teacher }));

            Assert.Empty(_db.Context.PayoutItems.ToList());
            Assert.Equal(SessionStatus.Scheduled, (await _db.Context.ClassSessions.FindAsync(session.Id))!.Status);
        }

        [Fact]
        public async Task SessionAttendance_Rejects_UnrelatedTeacher()
        {
            var (_, _, session) = await SeedBatchWithSessionAsync(totalSessions: 1);
            await BecomeUnrelatedTeacherAsync();

            // Attendance rows name real children; reading or writing another class's is
            // neither the outsider's data nor their record to change.
            await Assert.ThrowsAsync<ForbiddenException>(
                () => CreateAcademicOpsService().ListAttendanceAsync(session.Id));
            await Assert.ThrowsAsync<ForbiddenException>(
                () => CreateAcademicOpsService().CaptureAttendanceAsync(session.Id, new CaptureAttendanceRequest
                {
                    Entries = [new AttendanceEntryDto { TeacherProfileId = session.TeacherProfileId, Status = AttendanceStatus.Present }],
                }));
            Assert.Empty(_db.Context.SessionAttendances.ToList());
        }

        [Fact]
        public async Task SessionRecordings_Reject_UnrelatedTeacher()
        {
            var (_, _, session) = await SeedBatchWithSessionAsync(totalSessions: 1);
            await BecomeUnrelatedTeacherAsync();

            await Assert.ThrowsAsync<ForbiddenException>(
                () => CreateSessionService().ListRecordingsAsync(session.Id));
            await Assert.ThrowsAsync<ForbiddenException>(
                () => CreateSessionService().AddRecordingAsync(session.Id, new RegisterRecordingRequest
                {
                    StorageUrl = "https://recordings.test/evil.mp4",
                }));
            Assert.Empty(_db.Context.SessionRecordings.ToList());
        }

        [Fact]
        public async Task RequestRefund_Rejects_TransactionThatNeverSucceeded()
        {
            var parentUser = await _db.SeedUserAsync($"ref-pending-{Guid.NewGuid():N}@test.com", "x", UserRole.Parent);
            var parentProfile = new ParentProfile { UserId = parentUser.Id };
            _db.Context.AddRange(parentProfile,
                new PaymentAccount { Name = "P", DepartmentId = WellKnownDepartments.Phonics, GatewayProvider = "t", GatewayAccountRef = "p" });
            await _db.Context.SaveChangesAsync();

            var billing = CreateBillingService();
            var invoice = await billing.CreateInvoiceAsync(new CreateInvoiceRequest
            {
                ParentProfileId = parentProfile.Id, DepartmentId = WellKnownDepartments.Phonics, Amount = 1000,
                DueDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(7)),
            });

            // A cash intent the parent declared but nobody has collected: Pending, so no
            // money has actually reached the platform to give back.
            await billing.InitiateParentPaymentAsync(parentUser.Id, invoice.Id, new InitiateParentPaymentRequest { MethodKey = "cash" });
            var pending = await _db.Context.PaymentTransactions.FirstAsync(t => t.InvoiceId == invoice.Id);
            Assert.Equal(TransactionStatus.Pending, pending.Status);

            await Assert.ThrowsAsync<DomainValidationException>(
                () => billing.RequestRefundAsync(new RequestRefundRequest
                {
                    PaymentTransactionId = pending.Id, Amount = 1000, Reason = "Refund of money never received",
                }));
            Assert.Empty(_db.Context.Refunds.ToList());
        }

        [Fact]
        public async Task ApproveEnrollment_PersistsStatus_UnlocksParent_AndCreatesChild()
        {
            var parentUser = await _db.SeedUserAsync($"p-{Guid.NewGuid():N}@test.com", "x", UserRole.Parent);
            var parentProfile = new ParentProfile { UserId = parentUser.Id };
            _db.Context.ParentProfiles.Add(parentProfile);
            await _db.Context.SaveChangesAsync();

            var service = CreateEnrollmentService();
            await service.SubmitAsync(parentUser.Id, new SubmitEnrollmentFormRequest { FormDataJson = "{\"childName\":\"Kid One\",\"dob\":\"2016-01-01\",\"grade\":\"3\",\"courseInterest\":\"Math\"}" });
            var formId = (await service.ListAsync(null)).Single().Id;

            var result = await service.ReviewAsync(formId, new ReviewEnrollmentFormRequest
            {
                Approve = true,
                ChildFirstName = "Kid",
                ChildLastName = "One",
                ChildDateOfBirth = DateOnly.FromDateTime(DateTime.UtcNow).AddYears(-8),
            });

            Assert.Equal(EnrollmentFormStatus.Approved, result.Status);
            var refreshedParent = await _db.Context.ParentProfiles.FirstAsync(p => p.Id == parentProfile.Id);
            Assert.True(refreshedParent.EnrollmentFormCompleted);
            Assert.Single(_db.Context.Children.ToList());
        }

        [Fact]
        public async Task ApproveEnrollment_RejectsAFutureDateOfBirth()
        {
            var parentUser = await _db.SeedUserAsync($"p-{Guid.NewGuid():N}@test.com", "x", UserRole.Parent);
            var parentProfile = new ParentProfile { UserId = parentUser.Id };
            _db.Context.ParentProfiles.Add(parentProfile);
            await _db.Context.SaveChangesAsync();

            var service = CreateEnrollmentService();
            await service.SubmitAsync(parentUser.Id, new SubmitEnrollmentFormRequest { FormDataJson = "{\"childName\":\"Kid One\",\"dob\":\"2016-01-01\",\"grade\":\"3\",\"courseInterest\":\"Math\"}" });
            var formId = (await service.ListAsync(null)).Single().Id;

            await Assert.ThrowsAsync<DomainValidationException>(() => service.ReviewAsync(formId, new ReviewEnrollmentFormRequest
            {
                Approve = true,
                ChildFirstName = "Kid",
                ChildLastName = "One",
                ChildDateOfBirth = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(1),
            }));

            Assert.Empty(_db.Context.Children.ToList());
            var form = await _db.Context.EnrollmentForms.FirstAsync(f => f.Id == formId);
            Assert.Equal(EnrollmentFormStatus.Submitted, form.Status); // rejected before anything was mutated
        }

        [Fact]
        public async Task ApproveEnrollment_RequiresADateOfBirth_ButRejectingNeverNeedsOne()
        {
            var parentUser = await _db.SeedUserAsync($"p-{Guid.NewGuid():N}@test.com", "x", UserRole.Parent);
            var parentProfile = new ParentProfile { UserId = parentUser.Id };
            _db.Context.ParentProfiles.Add(parentProfile);
            await _db.Context.SaveChangesAsync();

            var service = CreateEnrollmentService();
            await service.SubmitAsync(parentUser.Id, new SubmitEnrollmentFormRequest { FormDataJson = "{\"childName\":\"Kid One\",\"dob\":\"2016-01-01\",\"grade\":\"3\",\"courseInterest\":\"Math\"}" });
            var formId = (await service.ListAsync(null)).Single().Id;

            // Not [Required] on the DTO — a reject request never touches this field and
            // must not be blocked by its absence.
            var rejected = await service.ReviewAsync(formId, new ReviewEnrollmentFormRequest { Approve = false });
            Assert.Equal(EnrollmentFormStatus.Rejected, rejected.Status);
        }

        [Fact]
        public async Task ApproveEnrollment_WithPackagePlan_StartsSubscription_AndIssuesFirstInvoice()
        {
            var parentUser = await _db.SeedUserAsync($"p-{Guid.NewGuid():N}@test.com", "x", UserRole.Parent);
            var parentProfile = new ParentProfile { UserId = parentUser.Id };
            var plan = new PackagePlan { Name = "Phonics Monthly", BillingType = BillingType.Subscription, BillingCycle = BillingCycle.Monthly, Price = 2500 };
            _db.Context.AddRange(parentProfile, plan,
                new PaymentAccount { Name = "P", DepartmentId = WellKnownDepartments.Phonics, GatewayProvider = "t", GatewayAccountRef = "p" });
            await _db.Context.SaveChangesAsync();

            var service = CreateEnrollmentService();
            await service.SubmitAsync(parentUser.Id, new SubmitEnrollmentFormRequest { FormDataJson = "{\"childName\":\"Kid One\",\"dob\":\"2016-01-01\",\"grade\":\"3\",\"courseInterest\":\"Math\"}" });
            var formId = (await service.ListAsync(null)).Single().Id;

            var result = await service.ReviewAsync(formId, new ReviewEnrollmentFormRequest
            {
                Approve = true,
                ChildFirstName = "Kid",
                ChildLastName = "One",
                ChildDateOfBirth = DateOnly.FromDateTime(DateTime.UtcNow).AddYears(-8),
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
            await service.SubmitAsync(parentUser.Id, new SubmitEnrollmentFormRequest { FormDataJson = "{\"childName\":\"Kid One\",\"dob\":\"2016-01-01\",\"grade\":\"3\",\"courseInterest\":\"Math\"}" });
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
        public async Task AssignStudent_PlacesChildInBatch_AndNotifiesParent()
        {
            var (batch, _, _) = await SeedBatchWithSessionAsync(totalSessions: 1, includeSession: false);
            var parentUser = await _db.SeedUserAsync($"p-{Guid.NewGuid():N}@test.com", "x", UserRole.Parent);
            var parentProfile = new ParentProfile { UserId = parentUser.Id };
            var child = new Child { ParentProfile = parentProfile, FirstName = "Kid", LastName = "One", IsActive = true };
            _db.Context.AddRange(parentProfile, child);
            await _db.Context.SaveChangesAsync();

            var service = CreateBatchService();
            var result = await service.AssignStudentAsync(batch.Id, child.Id);

            Assert.Equal(child.Id, result.ChildId);
            Assert.Equal(EnrollmentStatus.Active, result.Status);
            var enrollment = Assert.Single(_db.Context.BatchEnrollments.ToList());
            Assert.Equal(batch.Id, enrollment.BatchId);
            Assert.Equal(1, (await service.GetAsync(batch.Id)).EnrolledCount);
            Assert.Contains(_emailSender.Sent, m => m.To == parentUser.Email && m.Subject.Contains("assigned to a batch"));
        }

        [Fact]
        public async Task AssignStudent_RejectsWhenBatchIsAtCapacity()
        {
            var teacherUser = await _db.SeedUserAsync($"t-{Guid.NewGuid():N}@test.com", "x", UserRole.Teacher);
            var teacher = new TeacherProfile { UserId = teacherUser.Id };
            var category = new CourseCategory { Name = $"Cat-{Guid.NewGuid():N}", DepartmentId = WellKnownDepartments.Phonics };
            var course = new Course { CourseCategory = category, Name = "Course", Type = CourseType.Group, DurationMinutes = 45, Price = 100, TotalSessions = 1, DepartmentId = WellKnownDepartments.Phonics };
            var batch = new Batch { Course = course, TeacherProfile = teacher, Name = "Full Batch", Capacity = 1 };
            var parentProfile = new ParentProfile { UserId = (await _db.SeedUserAsync($"p1-{Guid.NewGuid():N}@test.com", "x", UserRole.Parent)).Id };
            var seatedChild = new Child { ParentProfile = parentProfile, FirstName = "Seated", LastName = "Kid", IsActive = true };
            _db.Context.AddRange(teacher, category, course, batch, parentProfile, seatedChild);
            await _db.Context.SaveChangesAsync();

            var service = CreateBatchService();
            await service.AssignStudentAsync(batch.Id, seatedChild.Id);

            var otherParentProfile = new ParentProfile { UserId = (await _db.SeedUserAsync($"p2-{Guid.NewGuid():N}@test.com", "x", UserRole.Parent)).Id };
            var waitingChild = new Child { ParentProfile = otherParentProfile, FirstName = "Waiting", LastName = "Kid", IsActive = true };
            _db.Context.AddRange(otherParentProfile, waitingChild);
            await _db.Context.SaveChangesAsync();

            await Assert.ThrowsAsync<DomainValidationException>(() => service.AssignStudentAsync(batch.Id, waitingChild.Id));
        }

        [Fact]
        public async Task AssignStudent_RejectsDuplicate_ButAllowsReassignAfterRemoval()
        {
            var (batch, _, _) = await SeedBatchWithSessionAsync(totalSessions: 1, includeSession: false);
            var parentUser = await _db.SeedUserAsync($"p-{Guid.NewGuid():N}@test.com", "x", UserRole.Parent);
            var parentProfile = new ParentProfile { UserId = parentUser.Id };
            var child = new Child { ParentProfile = parentProfile, FirstName = "Kid", LastName = "One", IsActive = true };
            _db.Context.AddRange(parentProfile, child);
            await _db.Context.SaveChangesAsync();

            var service = CreateBatchService();
            await service.AssignStudentAsync(batch.Id, child.Id);

            // Already-active: rejected, and the unique (BatchId, ChildId) index means a second
            // INSERT would be blocked at the DB level too — the service must catch it earlier.
            await Assert.ThrowsAsync<ConflictException>(() => service.AssignStudentAsync(batch.Id, child.Id));

            await service.RemoveStudentAsync(batch.Id, child.Id);
            Assert.Equal(0, (await service.GetAsync(batch.Id)).EnrolledCount);
            var withdrawn = Assert.Single(_db.Context.BatchEnrollments.ToList());
            Assert.Equal(EnrollmentStatus.Withdrawn, withdrawn.Status);

            // Re-assigning must reactivate the existing (unique-indexed) row, not insert a new one.
            await service.AssignStudentAsync(batch.Id, child.Id);
            Assert.Equal(1, (await service.GetAsync(batch.Id)).EnrolledCount);
            Assert.Single(_db.Context.BatchEnrollments.ToList());
        }

        [Fact]
        public async Task ListUnassignedStudents_ExcludesAlreadyEnrolled_AndInactiveChildren()
        {
            var (batch, _, _) = await SeedBatchWithSessionAsync(totalSessions: 1, includeSession: false);
            var enrolledParent = new ParentProfile { UserId = (await _db.SeedUserAsync($"p1-{Guid.NewGuid():N}@test.com", "x", UserRole.Parent)).Id };
            var enrolledChild = new Child { ParentProfile = enrolledParent, FirstName = "Enrolled", LastName = "Kid", IsActive = true };
            var inactiveParent = new ParentProfile { UserId = (await _db.SeedUserAsync($"p2-{Guid.NewGuid():N}@test.com", "x", UserRole.Parent)).Id };
            var inactiveChild = new Child { ParentProfile = inactiveParent, FirstName = "Inactive", LastName = "Kid", IsActive = false };
            var eligibleParent = new ParentProfile { UserId = (await _db.SeedUserAsync($"p3-{Guid.NewGuid():N}@test.com", "x", UserRole.Parent)).Id };
            var eligibleChild = new Child { ParentProfile = eligibleParent, FirstName = "Eligible", LastName = "Kid", IsActive = true };
            _db.Context.AddRange(enrolledParent, enrolledChild, inactiveParent, inactiveChild, eligibleParent, eligibleChild);
            await _db.Context.SaveChangesAsync();

            var service = CreateBatchService();
            await service.AssignStudentAsync(batch.Id, enrolledChild.Id);

            var unassigned = await service.ListUnassignedStudentsAsync(batch.Id);

            Assert.DoesNotContain(unassigned, c => c.ChildId == enrolledChild.Id);
            Assert.DoesNotContain(unassigned, c => c.ChildId == inactiveChild.Id);
            Assert.Contains(unassigned, c => c.ChildId == eligibleChild.Id);
        }

        [Fact]
        public async Task UpdateEnrollmentForm_PersistsEditedAnswers_AndRejectsApprovedForms()
        {
            var parentUser = await _db.SeedUserAsync($"p-{Guid.NewGuid():N}@test.com", "x", UserRole.Parent);
            _db.Context.ParentProfiles.Add(new ParentProfile { UserId = parentUser.Id });
            await _db.Context.SaveChangesAsync();

            var service = CreateEnrollmentService();
            await service.SubmitAsync(parentUser.Id, new SubmitEnrollmentFormRequest { FormDataJson = "{\"childName\":\"Old Name\",\"dob\":\"2016-01-01\",\"grade\":\"1\",\"courseInterest\":\"Math\"}" });
            var formId = (await service.ListAsync(null)).Single().Id;

            var edited = await service.UpdateFormDataAsync(formId, new SubmitEnrollmentFormRequest
            {
                FormDataJson = "{\"childName\":\"New Name\",\"dob\":\"2016-01-01\",\"grade\":\"2\",\"courseInterest\":\"Math\"}",
            });
            Assert.Contains("New Name", edited.FormDataJson);
            var reloaded = await service.GetAsync(formId);
            Assert.Contains("New Name", reloaded.FormDataJson);

            // Once approved, the form is immutable.
            await service.ReviewAsync(formId, new ReviewEnrollmentFormRequest
            {
                Approve = true,
                ChildFirstName = "New",
                ChildLastName = "Name",
                ChildDateOfBirth = DateOnly.FromDateTime(DateTime.UtcNow).AddYears(-8),
            });
            await Assert.ThrowsAsync<ConflictException>(
                () => service.UpdateFormDataAsync(formId, new SubmitEnrollmentFormRequest { FormDataJson = "{\"childName\":\"Later\",\"dob\":\"2016-01-01\",\"grade\":\"2\",\"courseInterest\":\"Math\"}" }));
        }

        [Fact]
        public async Task Store_PublicPlans_OnlyListsActivePlans()
        {
            var active = new PackagePlan { Name = "Active Plan", BillingType = BillingType.Subscription, BillingCycle = BillingCycle.Monthly, Price = 1500, IsActive = true };
            var inactive = new PackagePlan { Name = "Retired Plan", BillingType = BillingType.Subscription, BillingCycle = BillingCycle.Monthly, Price = 1200, IsActive = false };
            _db.Context.AddRange(active, inactive);
            await _db.Context.SaveChangesAsync();

            var plans = await CreateStoreService().ListPublicPlansAsync();

            Assert.Contains(plans, p => p.Id == active.Id);
            Assert.DoesNotContain(plans, p => p.Id == inactive.Id);
        }

        [Fact]
        public async Task Store_CreateInquiry_RejectsInactivePlan_AndAdminCanTransitionStatus()
        {
            var plan = new PackagePlan { Name = "Phonics Trial", BillingType = BillingType.Subscription, BillingCycle = BillingCycle.Monthly, Price = 1800, IsActive = true };
            var retired = new PackagePlan { Name = "Old Plan", BillingType = BillingType.Subscription, BillingCycle = BillingCycle.Monthly, Price = 900, IsActive = false };
            _db.Context.AddRange(plan, retired);
            await _db.Context.SaveChangesAsync();

            var service = CreateStoreService();

            await Assert.ThrowsAsync<NotFoundException>(() => service.CreateInquiryAsync(new CreateStoreInquiryRequest
            {
                PackagePlanId = retired.Id,
                ParentName = "Rohit Kapoor",
                ParentEmail = "rohit@example.com",
                ParentPhone = "9876543210",
                ChildName = "Aarav",
            }));

            var inquiry = await service.CreateInquiryAsync(new CreateStoreInquiryRequest
            {
                PackagePlanId = plan.Id,
                ParentName = "Rohit Kapoor",
                ParentEmail = "Rohit@Example.com",
                ParentPhone = "9876543210",
                ChildName = "Aarav",
                ChildAge = 6,
            });
            Assert.Equal(StoreInquiryStatus.New, inquiry.Status);
            Assert.Equal("rohit@example.com", inquiry.ParentEmail); // normalized lowercase

            var listed = await service.ListInquiriesAsync(StoreInquiryStatus.New);
            Assert.Single(listed);

            var updated = await service.UpdateInquiryStatusAsync(inquiry.Id, new UpdateStoreInquiryStatusRequest { Status = StoreInquiryStatus.Contacted });
            Assert.Equal(StoreInquiryStatus.Contacted, updated.Status);

            Assert.Empty(await service.ListInquiriesAsync(StoreInquiryStatus.New));
        }

        [Fact]
        public async Task Store_BookDemo_AutoAssignsTeacher_AndCreatesSession()
        {
            var teacherUser = await _db.SeedUserAsync($"t-{Guid.NewGuid():N}@test.com", "x", UserRole.Teacher);
            _db.Context.TeacherProfiles.Add(new TeacherProfile { UserId = teacherUser.Id });
            await _db.Context.SaveChangesAsync();

            var confirmation = await CreateStoreService().BookDemoAsync(new CreateStoreDemoBookingRequest
            {
                ParentName = "Visitor Parent",
                ParentEmail = "visitor@example.com",
                ParentPhone = "9876500000",
                ChildName = "Kid",
                ChildAge = 7,
                PreferredStartAtUtc = DateTime.UtcNow.AddDays(1),
            });

            Assert.Equal(30, (confirmation.ScheduledEndAtUtc - confirmation.ScheduledStartAtUtc).TotalMinutes);
            var booking = await _db.Context.DemoBookings.FirstAsync(b => b.Id == confirmation.Id);
            Assert.NotNull(booking.ClassSessionId);
            var session = await _db.Context.ClassSessions.FirstAsync(s => s.Id == booking.ClassSessionId!.Value);
            Assert.Equal(SessionType.Demo, session.Type);
            Assert.NotEqual(Guid.Empty, session.TeacherProfileId); // auto-assigned, never left blank
        }

        [Fact]
        public async Task Store_BookDemo_ConcurrentRequestsForSameSlot_MustNotDoubleBookTheOnlyTeacher()
        {
            // IMPORTANT SCOPE NOTE — what this does and does not prove. It proves the
            // serialized case: once one booking is committed, a second request for the same
            // slot is refused rather than double-booking the teacher, and the whole flow still
            // works now that it runs inside a SERIALIZABLE transaction. It does NOT prove the
            // genuinely concurrent case, because it cannot: both DbContexts here share one
            // SqliteConnection and a single ADO.NET connection runs one command at a time, so
            // the two requests below execute back to back, never overlapping. The concurrent
            // guarantee comes from where this now runs — CreateAsync wraps the busy-check and
            // the insert in one IUnitOfWork.ExecuteInSerializableTransactionAsync, so on
            // Postgres SSI aborts one of two truly overlapping bookings with SQLSTATE 40001 and
            // the unit of work retries it against the committed state (see that method's docs,
            // and UnitOfWork_SerializableTransaction_* for the retry machinery itself). With no
            // Postgres in this environment that half rests on SSI's documented semantics, not
            // on an observed run.
            var teacherUser = await _db.SeedUserAsync($"race-{Guid.NewGuid():N}@test.com", "x", UserRole.Teacher);
            _db.Context.TeacherProfiles.Add(new TeacherProfile { UserId = teacherUser.Id });
            await _db.Context.SaveChangesAsync();

            var start = DateTime.UtcNow.AddDays(1);

            // Two independent service graphs on two independent DbContexts sharing the same
            // underlying SQLite connection — the same shape as two concurrent HTTP requests
            // each getting their own scoped DbContext in ASP.NET Core.
            var (context2, uow2) = _db.CreateConcurrentSession();
            var auditLog2 = new AuditLogService(uow2, _db.CurrentUser);
            var emailTemplates2 = new EmailTemplateService(uow2, auditLog2, new MemoryCache(new MemoryCacheOptions()));
            var notifications2 = new NotificationService(uow2, _emailSender, emailTemplates2, NullLogger<NotificationService>.Instance);
            var userService2 = new UserService(
                uow2, _hasher, notifications2, emailTemplates2, auditLog2, _emailSender, _whatsAppSender, _smsSender, _bulkFileReader, NullLogger<UserService>.Instance);
            var service1 = CreateStoreService();
            var service2 = new StoreService(
                uow2, auditLog2,
                new DemoBookingService(uow2, auditLog2, _emailSender, emailTemplates2, new FakeCrmNotifier(), new FakeJitsiTokenService(), notifications2, userService2, NullLogger<DemoBookingService>.Instance));

            var request1 = new CreateStoreDemoBookingRequest
            {
                ParentName = "Parent One", ParentEmail = "race1@test.com", ParentPhone = "9000000001",
                ChildName = "Kid One", PreferredStartAtUtc = start,
            };
            var request2 = new CreateStoreDemoBookingRequest
            {
                ParentName = "Parent Two", ParentEmail = "race2@test.com", ParentPhone = "9000000002",
                ChildName = "Kid Two", PreferredStartAtUtc = start, // identical, fully-overlapping slot
            };

            var task1 = service1.BookDemoAsync(request1);
            var task2 = service2.BookDemoAsync(request2);

            Exception? failure = null;
            try
            {
                await Task.WhenAll(task1, task2);
            }
            catch (Exception ex)
            {
                failure = ex;
            }

            // Ground truth: how many demo sessions actually landed on the single teacher
            // for this exact slot, read fresh from a third, independent context.
            var (verifyContext, _) = _db.CreateConcurrentSession();
            var overlapping = await verifyContext.ClassSessions
                .Where(s => s.Type == SessionType.Demo && s.ScheduledStartAtUtc == start)
                .CountAsync();

            if (overlapping > 1)
            {
                Assert.Fail(
                    $"Double-booking confirmed: {overlapping} overlapping demo sessions were created for the " +
                    "same teacher and time slot, with no rejection. " +
                    $"(Task fault, if any: {failure?.Message ?? "none — both requests succeeded"})");
            }

            // Exactly one request must have succeeded and the other must have been refused with
            // the expected "no teacher available" message — a silent no-op, a mismatched error,
            // or a transaction-plumbing failure (e.g. an unsupported isolation level surfacing
            // as an InvalidOperationException) would all be bugs this catches.
            Assert.Equal(1, overlapping);
            var refusal = failure is AggregateException aggregate ? aggregate.InnerExceptions[0] : failure;
            Assert.IsType<DomainValidationException>(refusal);
            Assert.Contains("No teacher is available", refusal!.Message);

            // The losing request must leave nothing behind: its rolled-back transaction means no
            // orphan DemoBooking for the parent whose session was never created.
            Assert.Equal(1, await verifyContext.DemoBookings.CountAsync(b => b.ChildName == "Kid One" || b.ChildName == "Kid Two"));

            context2.Dispose();
            verifyContext.Dispose();
        }

        [Fact]
        public async Task UnitOfWork_SerializableTransaction_RetriesOnSerializationFailure_AndDiscardsTheFailedAttemptsWrites()
        {
            // The retry machinery the demo-booking fix depends on. SQLite never raises SQLSTATE
            // 40001, so the abort is injected in the shape SSI actually delivers it: the INSERT
            // itself is the statement that loses, so attempt 1 dies with its entity staged but
            // not yet persisted. Those entities stay in the change tracker in the Added state
            // across the rollback, so unless the unit of work forgets them, the retry's
            // SaveChanges inserts BOTH the abandoned entity and the fresh one — which, Name
            // being uniquely indexed, blows up rather than quietly duplicating.
            var attempts = 0;
            var result = await _db.UnitOfWork.ExecuteInSerializableTransactionAsync(async ct =>
            {
                attempts++;
                await _db.UnitOfWork.Repository<CourseCategory>().AddAsync(
                    new CourseCategory { Name = "Retry Category", DepartmentId = WellKnownDepartments.Phonics }, ct);

                if (attempts == 1)
                {
                    throw new FakeSerializationFailure();
                }

                await _db.UnitOfWork.SaveChangesAsync(ct);
                return attempts;
            });

            Assert.Equal(2, result); // retried exactly once, and the retry is what succeeded
            var (verifyContext, _) = _db.CreateConcurrentSession();
            Assert.Equal(1, await verifyContext.CourseCategories.CountAsync(c => c.Name == "Retry Category"));
            verifyContext.Dispose();
        }

        [Fact]
        public async Task UnitOfWork_SerializableTransaction_DoesNotRetryOrdinaryFailures_AndRollsThemBack()
        {
            // Only a serialization failure is safe to redo blindly. A business failure must
            // surface once, unchanged, with everything the attempt wrote rolled back — otherwise
            // "no teacher available" would be retried three more times to the same conclusion.
            var attempts = 0;
            await Assert.ThrowsAsync<DomainValidationException>(() =>
                _db.UnitOfWork.ExecuteInSerializableTransactionAsync<int>(async ct =>
                {
                    attempts++;
                    await _db.UnitOfWork.Repository<CourseCategory>().AddAsync(
                        new CourseCategory { Name = "Doomed Category", DepartmentId = WellKnownDepartments.Phonics }, ct);
                    await _db.UnitOfWork.SaveChangesAsync(ct);
                    throw new DomainValidationException("business rule said no");
                }));

            Assert.Equal(1, attempts);
            var (verifyContext, _) = _db.CreateConcurrentSession();
            Assert.Equal(0, await verifyContext.CourseCategories.CountAsync(c => c.Name == "Doomed Category"));
            verifyContext.Dispose();
        }

        [Fact]
        public async Task Store_BookDemo_RejectsTooSoonAndTooFarOut()
        {
            var service = CreateStoreService();
            var request = new CreateStoreDemoBookingRequest
            {
                ParentName = "Visitor Parent",
                ParentEmail = "visitor2@example.com",
                ParentPhone = "9876500001",
                ChildName = "Kid",
                PreferredStartAtUtc = DateTime.UtcNow.AddMinutes(30), // under the 2-hour lead time
            };

            await Assert.ThrowsAsync<DomainValidationException>(() => service.BookDemoAsync(request));

            request.PreferredStartAtUtc = DateTime.UtcNow.AddDays(90); // past the 30-day window
            await Assert.ThrowsAsync<DomainValidationException>(() => service.BookDemoAsync(request));
        }

        [Fact]
        public async Task ProgressReport_SaveThenSend_LocksContentAndEmailsParent()
        {
            var parentUser = await _db.SeedUserAsync($"pr-parent-{Guid.NewGuid():N}@test.com", "x", UserRole.Parent);
            var parentProfile = new ParentProfile { UserId = parentUser.Id };
            var child = new Child { ParentProfile = parentProfile, FirstName = "Aarav", LastName = "Kid", IsActive = true };
            _db.Context.AddRange(parentProfile, child);
            await _db.Context.SaveChangesAsync();

            var service = CreateProgressReportService();
            var created = await service.EnsureMonthlyDraftsAsync(2026, 8);
            Assert.Equal(1, created);

            var draft = (await service.ListAsync(2026, 8, child.Id)).Single();
            Assert.Equal(ProgressReportStatus.Draft, draft.Status);
            Assert.Equal(string.Empty, draft.Content);

            // Sending an empty draft is rejected — there's nothing for the parent to read yet.
            await Assert.ThrowsAsync<DomainValidationException>(() => service.SendAsync(draft.Id));

            var saved = await service.SaveContentAsync(draft.Id, new SaveProgressReportContentRequest
            {
                Content = "Aarav is making great progress with blending sounds this month.",
            });
            Assert.Equal(ProgressReportStatus.Draft, saved.Status);

            var sent = await service.SendAsync(draft.Id);
            Assert.Equal(ProgressReportStatus.Sent, sent.Status);
            Assert.NotNull(sent.SentAtUtc);

            var email = Assert.Single(_emailSender.Sent, e => e.To == parentUser.Email);
            Assert.Contains("blending sounds", email.Body);

            // A sent report is locked: no further content edits, no re-sending.
            await Assert.ThrowsAsync<DomainValidationException>(
                () => service.SaveContentAsync(draft.Id, new SaveProgressReportContentRequest { Content = "Edited after send" }));
            await Assert.ThrowsAsync<DomainValidationException>(() => service.SendAsync(draft.Id));
        }

        [Fact]
        public async Task ProgressReport_EnsureMonthlyDrafts_SkipsInactiveChildrenAndIsIdempotent()
        {
            var parentUser = await _db.SeedUserAsync($"pr-parent2-{Guid.NewGuid():N}@test.com", "x", UserRole.Parent);
            var parentProfile = new ParentProfile { UserId = parentUser.Id };
            var activeChild = new Child { ParentProfile = parentProfile, FirstName = "Active", LastName = "Kid", IsActive = true };
            var inactiveChild = new Child { ParentProfile = parentProfile, FirstName = "Inactive", LastName = "Kid", IsActive = false };
            _db.Context.AddRange(parentProfile, activeChild, inactiveChild);
            await _db.Context.SaveChangesAsync();

            var service = CreateProgressReportService();
            var firstRun = await service.EnsureMonthlyDraftsAsync(2026, 9);
            Assert.Equal(1, firstRun); // only the active child gets a draft

            var secondRun = await service.EnsureMonthlyDraftsAsync(2026, 9);
            Assert.Equal(0, secondRun); // already exists — no duplicate row for the same period

            var reports = await service.ListAsync(2026, 9, null);
            Assert.Single(reports);
            Assert.Equal(activeChild.Id, reports[0].ChildId);
        }

        [Fact]
        public void HtmlText_PlainTextFromHtml_StripsMarkupAndBrandChrome()
        {
            const string rendered =
                "<div style=\"font-family:Arial,Helvetica,sans-serif;\"><div style=\"background:#4F46E5;\">" +
                "<span style=\"color:#ffffff;\">The Reader Nest</span></div><div><p>Your child's class starts at " +
                "<strong>Wed, 05 Aug 2026 3:30 PM (Asia/Kolkata)</strong>.</p><p><a href=\"https://meet.example.com/x\">" +
                "Join Now</a></p></div><p>The Reader Nest &middot; Read &middot; Write &middot; Speak</p></div>";

            var plain = iucs.readernest.application.Common.HtmlText.PlainTextFromHtml(rendered);

            Assert.DoesNotContain('<', plain);
            Assert.DoesNotContain('>', plain);
            Assert.Contains("Your child's class starts at Wed, 05 Aug 2026 3:30 PM (Asia/Kolkata) . Join Now", plain);
            Assert.DoesNotContain("The Reader Nest", plain); // header/footer chrome stripped, not just tags
        }

        /// <summary>
        /// Regression test: the DatabaseInitializer backfill filters Notification.Body with
        /// `.Contains('<')` (char overload) which Npgsql can't translate to SQL and crashes the
        /// whole app at startup — caught only by actually running the query through a real EF
        /// provider, not by unit-testing HtmlText in isolation. Mirrors the exact predicate shape
        /// used there so a future regression to the char overload fails here too.
        /// </summary>
        [Fact]
        public async Task NotificationQuery_ContainsStringPredicate_TranslatesAndFiltersCorrectly()
        {
            var user = await _db.SeedUserAsync($"notif-{Guid.NewGuid():N}@test.com", "x", UserRole.Parent);
            var staleHtml = new Notification
            {
                RecipientUserId = user.Id,
                Type = NotificationType.SessionReminder,
                Channel = NotificationChannel.Email,
                Subject = "Class starts in 1 hour",
                Body = "<div style=\"padding:28px;\"><p>Your child's class starts soon.</p></div>",
                Status = NotificationStatus.Sent,
            };
            var alreadyPlain = new Notification
            {
                RecipientUserId = user.Id,
                Type = NotificationType.SessionReminder,
                Channel = NotificationChannel.Email,
                Subject = "Class starts in 1 hour",
                Body = "Your child's class starts at Sat, 08 Aug 2026 3:30 PM. Join Now",
                Status = NotificationStatus.Sent,
            };
            _db.Context.AddRange(staleHtml, alreadyPlain);
            await _db.Context.SaveChangesAsync();

            var matched = await _db.Context.Notifications
                .Where(n => n.Body.Contains("<") && n.Body.Contains(">"))
                .ToListAsync();

            Assert.Contains(matched, n => n.Id == staleHtml.Id);
            Assert.DoesNotContain(matched, n => n.Id == alreadyPlain.Id);
        }

        [Fact]
        public async Task ParentDashboard_AggregatesPerChild_WithoutCrossContamination()
        {
            var parentUser = await _db.SeedUserAsync($"dash-{Guid.NewGuid():N}@test.com", "x", UserRole.Parent);
            var parentProfile = new ParentProfile { UserId = parentUser.Id };
            _db.Context.ParentProfiles.Add(parentProfile);
            await _db.Context.SaveChangesAsync();

            var (batchA, _, _) = await SeedBatchWithSessionAsync(10, includeSession: false);
            var (batchB, _, _) = await SeedBatchWithSessionAsync(10, includeSession: false);

            // Alice sits in batch A, Bob in batch B, so a per-child aggregate that leaked
            // across siblings (or across batches) shows up as the wrong count below.
            var alice = new Child { ParentProfileId = parentProfile.Id, FirstName = "Alice", LastName = "A", IsActive = true };
            var bob = new Child { ParentProfileId = parentProfile.Id, FirstName = "Bob", LastName = "B", IsActive = true };
            _db.Context.AddRange(alice, bob);
            await _db.Context.SaveChangesAsync();

            _db.Context.AddRange(
                new BatchEnrollment { BatchId = batchA.Id, ChildId = alice.Id, Status = EnrollmentStatus.Active },
                new BatchEnrollment { BatchId = batchB.Id, ChildId = bob.Id, Status = EnrollmentStatus.Active });

            ClassSession SessionIn(Batch batch, SessionStatus status) => new()
            {
                BatchId = batch.Id,
                TeacherProfileId = batch.TeacherProfileId,
                Status = status,
                ScheduledStartAtUtc = DateTime.UtcNow.AddDays(1),
                ScheduledEndAtUtc = DateTime.UtcNow.AddDays(1).AddMinutes(45),
            };

            // Batch A: 2 completed + 1 scheduled. Batch B: 1 completed only.
            var aliceCompleted = SessionIn(batchA, SessionStatus.Completed);
            _db.Context.AddRange(
                aliceCompleted,
                SessionIn(batchA, SessionStatus.Completed),
                SessionIn(batchA, SessionStatus.Scheduled),
                SessionIn(batchB, SessionStatus.Completed));
            await _db.Context.SaveChangesAsync();

            // Alice: 1 of 2 attended → 50%. Bob has no attendance rows → defaults to 100%.
            _db.Context.AddRange(
                new SessionAttendance
                {
                    ClassSessionId = aliceCompleted.Id,
                    ChildId = alice.Id,
                    ParticipantType = ParticipantType.Student,
                    Status = AttendanceStatus.Present,
                },
                new SessionAttendance
                {
                    ClassSessionId = aliceCompleted.Id,
                    ChildId = bob.Id,
                    ParticipantType = ParticipantType.Student,
                    Status = AttendanceStatus.Present,
                });
            await _db.Context.SaveChangesAsync();

            // Bob's single row is Absent, so his percentage must differ from Alice's.
            var bobRow = await _db.Context.SessionAttendances.FirstAsync(a => a.ChildId == bob.Id);
            bobRow.Status = AttendanceStatus.Absent;
            await _db.Context.SaveChangesAsync();
            _db.Context.ChangeTracker.Clear();

            var dashboard = await new ParentPortalService(_db.UnitOfWork).GetDashboardAsync(parentUser.Id);

            var aliceDto = dashboard.Children.Single(c => c.ChildId == alice.Id);
            Assert.Equal(2, aliceDto.ClassesCompleted);
            Assert.Equal(1, aliceDto.ClassesRemaining);
            Assert.Equal(100, aliceDto.AttendancePercent);

            var bobDto = dashboard.Children.Single(c => c.ChildId == bob.Id);
            Assert.Equal(1, bobDto.ClassesCompleted);
            Assert.Equal(0, bobDto.ClassesRemaining);
            Assert.Equal(0, bobDto.AttendancePercent);
        }

        /// <summary>
        /// ClassesCompleted used to count a batch's ENTIRE completed-session history, with no
        /// check against when the child's own enrollment actually started — a child who
        /// transfers into (or is newly assigned to) a batch that's already been running for
        /// weeks would immediately show every session that ran before they ever joined, on a
        /// dashboard the parent has no reason to doubt. A child enrolled BEFORE a session
        /// existed must be credited with it; one enrolled AFTER must not be.
        /// </summary>
        [Fact]
        public async Task ParentDashboard_ClassesCompleted_ExcludesSessionsBeforeTheChildJoinedTheBatch()
        {
            var parentUser = await _db.SeedUserAsync($"latejoin-{Guid.NewGuid():N}@test.com", "x", UserRole.Parent);
            var parentProfile = new ParentProfile { UserId = parentUser.Id };
            _db.Context.ParentProfiles.Add(parentProfile);
            await _db.Context.SaveChangesAsync();

            var (batch, _, _) = await SeedBatchWithSessionAsync(10, includeSession: false);

            var early = new Child { ParentProfileId = parentProfile.Id, FirstName = "Early", LastName = "Bird", IsActive = true };
            _db.Context.Add(early);
            await _db.Context.SaveChangesAsync();
            // Early's enrollment CreatedAtUtc (auto-stamped "now" on insert) predates the session below.
            _db.Context.Add(new BatchEnrollment { BatchId = batch.Id, ChildId = early.Id, Status = EnrollmentStatus.Active });
            await _db.Context.SaveChangesAsync();

            // A class that ran while only Early was enrolled.
            _db.Context.Add(new ClassSession
            {
                BatchId = batch.Id,
                TeacherProfileId = batch.TeacherProfileId,
                Status = SessionStatus.Completed,
                ScheduledStartAtUtc = DateTime.UtcNow,
                ScheduledEndAtUtc = DateTime.UtcNow.AddMinutes(45),
            });
            await _db.Context.SaveChangesAsync();

            // Late transfers into the SAME already-running batch after that class already happened.
            var late = new Child { ParentProfileId = parentProfile.Id, FirstName = "Late", LastName = "Comer", IsActive = true };
            _db.Context.Add(late);
            await _db.Context.SaveChangesAsync();
            _db.Context.Add(new BatchEnrollment { BatchId = batch.Id, ChildId = late.Id, Status = EnrollmentStatus.Active });
            await _db.Context.SaveChangesAsync();
            _db.Context.ChangeTracker.Clear();

            var dashboard = await new ParentPortalService(_db.UnitOfWork).GetDashboardAsync(parentUser.Id);

            var earlyDto = dashboard.Children.Single(c => c.ChildId == early.Id);
            Assert.Equal(1, earlyDto.ClassesCompleted); // was enrolled for it

            var lateDto = dashboard.Children.Single(c => c.ChildId == late.Id);
            Assert.Equal(0, lateDto.ClassesCompleted); // joined after it already ran — must not inherit the batch's history
        }

        [Fact]
        public async Task MarkAllRead_StampsEveryUnreadRow_AndLeavesOtherRecipientsAlone()
        {
            var user = await _db.SeedUserAsync($"notif-{Guid.NewGuid():N}@test.com", "x", UserRole.Parent);
            var otherUser = await _db.SeedUserAsync($"notif-{Guid.NewGuid():N}@test.com", "x", UserRole.Parent);

            Notification Unread(Guid recipientId) => new()
            {
                RecipientUserId = recipientId,
                Type = NotificationType.SessionReminder,
                Channel = NotificationChannel.InApp,
                Subject = "Class starts in 1 hour",
                Body = "Your child's class starts soon.",
                Status = NotificationStatus.Sent,
            };

            var alreadyRead = Unread(user.Id);
            alreadyRead.ReadAtUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

            var mine = new[] { Unread(user.Id), Unread(user.Id) };
            var theirs = Unread(otherUser.Id);
            _db.Context.AddRange([.. mine, alreadyRead, theirs]);
            await _db.Context.SaveChangesAsync();

            // MarkAllReadAsync is a single ExecuteUpdate, which bypasses the change tracker —
            // so this also pins that the audit interceptor's UpdatedAtUtc stamp is applied by
            // hand, and that the conditional WHERE really is scoped to one recipient's unread.
            var marked = await _notifications.MarkAllReadAsync(user.Id);
            Assert.Equal(mine.Length, marked);

            _db.Context.ChangeTracker.Clear();
            var rows = await _db.Context.Notifications.ToListAsync();

            foreach (var expected in mine)
            {
                var row = rows.Single(n => n.Id == expected.Id);
                Assert.NotNull(row.ReadAtUtc);
                Assert.NotNull(row.UpdatedAtUtc);
            }

            // An already-read row keeps its original timestamp, and another user's is untouched.
            Assert.Equal(alreadyRead.ReadAtUtc, rows.Single(n => n.Id == alreadyRead.Id).ReadAtUtc);
            Assert.Null(rows.Single(n => n.Id == theirs.Id).ReadAtUtc);

            // Second call has nothing left to do.
            Assert.Equal(0, await _notifications.MarkAllReadAsync(user.Id));
        }

        // ---- QA pass: object-level authorization on id-keyed teacher endpoints ----

        /// <summary>A demo booking with a real class session assigned to its own teacher.</summary>
        private async Task<(DemoBooking Booking, TeacherProfile OwningTeacher, User OtherTeacherUser)> SeedDemoBookingAsync()
        {
            var owningTeacherUser = await _db.SeedUserAsync($"demo-own-{Guid.NewGuid():N}@test.com", "x", UserRole.Teacher);
            var owningTeacher = new TeacherProfile { UserId = owningTeacherUser.Id };
            var otherTeacherUser = await _db.SeedUserAsync($"demo-other-{Guid.NewGuid():N}@test.com", "x", UserRole.Teacher);
            var otherTeacher = new TeacherProfile { UserId = otherTeacherUser.Id };
            _db.Context.AddRange(owningTeacher, otherTeacher);
            await _db.Context.SaveChangesAsync();

            var booking = await CreateDemoBookingService().CreateAsync(new CreateDemoBookingRequest
            {
                ParentName = "Lead Parent",
                ParentEmail = $"lead-{Guid.NewGuid():N}@test.com",
                ChildName = "Kid",
                TeacherProfileId = owningTeacher.Id,
                ScheduledStartAtUtc = DateTime.UtcNow.AddDays(1),
                ScheduledEndAtUtc = DateTime.UtcNow.AddDays(1).AddMinutes(30),
            });

            var entity = await _db.Context.DemoBookings.AsNoTracking().FirstAsync(b => b.Id == booking.Id);
            return (entity, owningTeacher, otherTeacherUser);
        }

        [Fact]
        public async Task SubmitDemoFeedback_Rejects_UnrelatedTeacher()
        {
            // Same shape as the session IDOR fixed in 8895924: the endpoint is gated on
            // "is a Teacher" but the demo booking id comes straight from the caller. The
            // feedback is the permanent, admission-facing evaluation of a named child
            // (it carries RecommendedCourseId/SuggestedBatchType and drives enrollment),
            // it is filed under the CALLER's teacher profile, and it is one-shot — so a
            // teacher who never ran the demo can both falsify the record and permanently
            // lock the real teacher out of the mandatory post-demo step.
            var (booking, _, otherTeacherUser) = await SeedDemoBookingAsync();

            await Assert.ThrowsAsync<ForbiddenException>(() =>
                CreateDemoBookingService().SubmitFeedbackAsync(booking.Id, otherTeacherUser.Id, new SubmitDemoFeedbackRequest
                {
                    AcademicLevel = "Level 2",
                    Strengths = "Injected by a teacher who never taught this child",
                    ImprovementAreas = "n/a",
                }));

            // Ground truth: nothing was written, so the assigned teacher can still file theirs.
            var (verifyContext, _) = _db.CreateConcurrentSession();
            Assert.Empty(await verifyContext.DemoFeedbacks.Where(f => f.DemoBookingId == booking.Id).ToListAsync());
            verifyContext.Dispose();
        }

        [Fact]
        public async Task SubmitDemoFeedback_Allows_AssignedTeacher_AndIsOneShot()
        {
            var (booking, owningTeacher, _) = await SeedDemoBookingAsync();
            var owningTeacherUserId = (await _db.Context.TeacherProfiles.AsNoTracking()
                .FirstAsync(t => t.Id == owningTeacher.Id)).UserId;

            var request = new SubmitDemoFeedbackRequest
            {
                AcademicLevel = "Level 2",
                Strengths = "Confident reader",
                ImprovementAreas = "Blends",
            };

            var feedback = await CreateDemoBookingService().SubmitFeedbackAsync(booking.Id, owningTeacherUserId, request);
            Assert.Equal(booking.Id, feedback.DemoBookingId);

            // Feedback closes the demo stage of the conversion pipeline.
            var (verifyContext, _) = _db.CreateConcurrentSession();
            Assert.Equal(
                ConversionStatus.DemoCompleted,
                (await verifyContext.DemoBookings.FirstAsync(b => b.Id == booking.Id)).ConversionStatus);
            verifyContext.Dispose();

            // Still one-shot for the legitimate teacher.
            await Assert.ThrowsAsync<DomainValidationException>(() =>
                CreateDemoBookingService().SubmitFeedbackAsync(booking.Id, owningTeacherUserId, request));
        }

        // ---- QA pass: state-machine guards (invalid transitions must be refused, not no-op'd) ----

        [Fact]
        public async Task CashIntent_CannotBeConfirmedOrRejectedTwice()
        {
            var (billing, invoice) = await SeedInvoiceAsync(amount: 1000);
            var parentUserId = await _db.Context.ParentProfiles.AsNoTracking()
                .Where(p => p.Id == invoice.ParentProfileId).Select(p => p.UserId).FirstAsync();

            await billing.InitiateParentPaymentAsync(parentUserId, invoice.Id, new InitiateParentPaymentRequest { MethodKey = "cash" });
            var intent = await _db.Context.PaymentTransactions.AsNoTracking()
                .FirstAsync(t => t.InvoiceId == invoice.Id && t.Method == PaymentMethod.Cash);

            await billing.ConfirmCashIntentAsync(intent.Id, new ConfirmCashIntentRequest());

            // Re-confirming must not credit the invoice a second time.
            await Assert.ThrowsAsync<DomainValidationException>(() =>
                billing.ConfirmCashIntentAsync(intent.Id, new ConfirmCashIntentRequest()));
            // Nor may a settled intent be walked backwards into Failed.
            await Assert.ThrowsAsync<DomainValidationException>(() =>
                billing.RejectCashIntentAsync(intent.Id, new RejectCashIntentRequest { Reason = "changed my mind" }));

            var (verifyContext, _) = _db.CreateConcurrentSession();
            var settled = await verifyContext.Invoices.FirstAsync(i => i.Id == invoice.Id);
            Assert.Equal(1000m, settled.AmountPaid); // credited exactly once
            Assert.Equal(InvoiceStatus.Paid, settled.Status);
            Assert.Equal(
                TransactionStatus.Success,
                (await verifyContext.PaymentTransactions.FirstAsync(t => t.Id == intent.Id)).Status);
            verifyContext.Dispose();
        }

        [Fact]
        public async Task Refund_RejectedThenApproved_IsRefused_AndNeverReachesTheGateway()
        {
            var gateway = new FakePaymentGateway();
            var refund = await SeedRequestedRefundAsync(gateway);
            var billing = CreateBillingService(gateway);

            await billing.ReviewRefundAsync(refund.Id, new ReviewRefundRequest { Approve = false });

            // AppException, not a specific subclass: which of the two guards refuses this depends
            // on whether the caller's DbContext still holds the pre-rejection tracked entity.
            // In production each request is a fresh scope, so the friendly in-memory check wins
            // (400 "already Rejected"); reusing one scope as this test does leaves that read
            // stale — ExecuteUpdateAsync bypasses the change tracker — and the conditional UPDATE
            // catches it instead (409). Both are correct refusals; the invariant under test is
            // that a rejected refund is never resurrected and never reaches the gateway.
            await Assert.ThrowsAsync<ConflictException>(() =>
                billing.ReviewRefundAsync(refund.Id, new ReviewRefundRequest { Approve = true }));

            Assert.Equal(0, gateway.RefundCallCount); // no money left the platform
            var (verifyContext, _) = _db.CreateConcurrentSession();
            Assert.Equal(RefundStatus.Rejected, (await verifyContext.Refunds.FirstAsync(r => r.Id == refund.Id)).Status);
            verifyContext.Dispose();
        }

        [Fact]
        public async Task Session_TerminalStatuses_RefuseCompleteAndNoShowAndAttendance()
        {
            var (_, _, session) = await SeedBatchWithSessionAsync(totalSessions: 3);
            var sessions = CreateSessionService();

            await sessions.CompleteAsync(session.Id, new CompleteSessionRequest());

            await Assert.ThrowsAsync<DomainValidationException>(() =>
                sessions.CompleteAsync(session.Id, new CompleteSessionRequest()));
            await Assert.ThrowsAsync<DomainValidationException>(() =>
                sessions.MarkNoShowAsync(session.Id, new MarkNoShowRequest { Party = NoShowParty.Teacher }));

            // Exactly one payout earning accrued, not two — the guard is what protects the
            // teacher's statement from a double-click on "complete".
            var (verifyContext, _) = _db.CreateConcurrentSession();
            var items = await verifyContext.PayoutItems.Where(i => i.ClassSessionId == session.Id).ToListAsync();
            Assert.Single(items);
            verifyContext.Dispose();
        }

        [Fact]
        public async Task Payout_FinalizeAndMarkPaid_RefuseOutOfOrderAndRepeatTransitions()
        {
            var (_, _, session) = await SeedBatchWithSessionAsync(totalSessions: 1);
            var payouts = CreatePayoutService();
            await payouts.SetRateAsync(new SavePayoutRateRequest
            {
                RatePerMinute = 500, EffectiveFrom = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1)),
            });
            await SeedFullTeacherAttendanceAsync(session);
            await CreateSessionService().CompleteAsync(session.Id, new CompleteSessionRequest());
            var payout = await _db.Context.Payouts.AsNoTracking().FirstAsync();

            // Paying before finalizing skips the lock on the month's total.
            await Assert.ThrowsAsync<DomainValidationException>(() => payouts.MarkPaidAsync(payout.Id));

            await payouts.FinalizeAsync(payout.Id);
            await Assert.ThrowsAsync<DomainValidationException>(() => payouts.FinalizeAsync(payout.Id));

            await payouts.MarkPaidAsync(payout.Id);
            // A second mark-paid would email a duplicate salary slip against the same money.
            await Assert.ThrowsAsync<DomainValidationException>(() => payouts.MarkPaidAsync(payout.Id));

            var (verifyContext, _) = _db.CreateConcurrentSession();
            Assert.Equal(PayoutStatus.Paid, (await verifyContext.Payouts.FirstAsync(p => p.Id == payout.Id)).Status);
            verifyContext.Dispose();
        }

        [Fact]
        public async Task FeeSuspension_CannotBeLiftedTwice()
        {
            var parentUser = await _db.SeedUserAsync($"susp-{Guid.NewGuid():N}@test.com", "x", UserRole.Parent);
            var parentProfile = new ParentProfile { UserId = parentUser.Id };
            var account = new PaymentAccount { Name = "P", DepartmentId = WellKnownDepartments.Phonics, GatewayProvider = "t", GatewayAccountRef = "p" };
            _db.Context.AddRange(parentProfile, account);
            await _db.Context.SaveChangesAsync();

            var billing = CreateBillingService();
            var invoice = await billing.CreateInvoiceAsync(new CreateInvoiceRequest
            {
                ParentProfileId = parentProfile.Id, DepartmentId = WellKnownDepartments.Phonics, Amount = 500,
                DueDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1)),
            });
            var suspension = new FeeSuspension
            {
                ParentProfileId = parentProfile.Id, InvoiceId = invoice.Id,
                Reason = "Overdue", SuspendedAtUtc = DateTime.UtcNow,
            };
            _db.Context.FeeSuspensions.Add(suspension);
            await _db.Context.SaveChangesAsync();

            var lifted = await billing.LiftSuspensionAsync(suspension.Id);
            Assert.Equal(SuspensionStatus.Lifted, lifted.Status);

            // Regression: NotificationType.FeeSuspension existed with zero templates wired to it --
            // a manually-lifted suspension told the parent nothing about their restored access.
            Assert.Contains(_emailSender.Sent, m => m.To == parentUser.Email && m.Subject.Contains("restored"));

            await Assert.ThrowsAsync<DomainValidationException>(() => billing.LiftSuspensionAsync(suspension.Id));
        }

        [Fact]
        public async Task ConfirmCashIntent_RecordsTheCollectedMoneyAsASuccessfulTransaction()
        {
            // Regression: confirming a cash intent that FULLY settles its invoice used to leave
            // the very transaction being confirmed marked Failed ("settled by another payment"),
            // because ApplyPaymentToInvoiceAsync's stale-intent sweep re-read it from the DB —
            // where the flip to Success was not committed yet — and swept it up as if it were a
            // competing intent. The invoice looked right, but the money's own row did not exist
            // as a successful payment: it disappeared from the invoice's receipt list and could
            // never be refunded. Only full settlement triggered it (a partial payment never
            // enters that branch), which is the common case for a cash intent.
            var (billing, invoice) = await SeedInvoiceAsync(amount: 1000);
            var parentUserId = await _db.Context.ParentProfiles.AsNoTracking()
                .Where(p => p.Id == invoice.ParentProfileId).Select(p => p.UserId).FirstAsync();

            await billing.InitiateParentPaymentAsync(parentUserId, invoice.Id, new InitiateParentPaymentRequest { MethodKey = "cash" });
            var intent = await _db.Context.PaymentTransactions.AsNoTracking()
                .FirstAsync(t => t.InvoiceId == invoice.Id && t.Method == PaymentMethod.Cash);

            var confirmed = await billing.ConfirmCashIntentAsync(intent.Id, new ConfirmCashIntentRequest());
            Assert.Equal(1000m, confirmed.Amount);

            var (verifyContext, _) = _db.CreateConcurrentSession();
            var row = await verifyContext.PaymentTransactions.FirstAsync(t => t.Id == intent.Id);
            Assert.Equal(TransactionStatus.Success, row.Status);
            Assert.Null(row.FailureReason);
            Assert.NotNull(row.PaidAtUtc);
            Assert.NotNull(row.ReceiptNumber); // the receipt handed to the parent at the centre
            verifyContext.Dispose();

            // The two things that consume Success transactions must both see the cash payment.
            var listed = Assert.Single(await billing.ListInvoiceTransactionsAsync(invoice.Id));
            Assert.Equal(intent.Id, listed.Id);

            var refund = await billing.RequestRefundAsync(new RequestRefundRequest
            {
                PaymentTransactionId = intent.Id, Amount = 250, Reason = "Goodwill on collected cash",
            });
            Assert.Equal(RefundStatus.Requested, refund.Status);
        }

        [Fact]
        public async Task RecordPayment_SettlingAPendingCashIntentInFull_KeepsItSuccessful()
        {
            // Same defect, sibling call site: RecordPaymentAsync reuses the parent's pending cash
            // intent instead of inserting a duplicate row, so it hit the identical sweep.
            var (billing, invoice) = await SeedInvoiceAsync(amount: 800);
            var parentUserId = await _db.Context.ParentProfiles.AsNoTracking()
                .Where(p => p.Id == invoice.ParentProfileId).Select(p => p.UserId).FirstAsync();

            await billing.InitiateParentPaymentAsync(parentUserId, invoice.Id, new InitiateParentPaymentRequest { MethodKey = "cash" });

            await billing.RecordPaymentAsync(invoice.Id, new RecordPaymentRequest { Amount = 800, Method = PaymentMethod.Cash });

            var (verifyContext, _) = _db.CreateConcurrentSession();
            var rows = await verifyContext.PaymentTransactions.Where(t => t.InvoiceId == invoice.Id).ToListAsync();
            var row = Assert.Single(rows); // reused, not duplicated
            Assert.Equal(TransactionStatus.Success, row.Status);
            Assert.Equal(InvoiceStatus.Paid, (await verifyContext.Invoices.FirstAsync(i => i.Id == invoice.Id)).Status);
            verifyContext.Dispose();
        }

        [Fact]
        public async Task ConfirmCashIntent_StillClosesAGenuinelyCompetingIntent()
        {
            // The guard above must not blunt what the sweep is actually for: a DIFFERENT pending
            // cash intent on the same invoice still has to be closed when the money arrives by
            // another route, or it lingers in the staff confirmation queue and can be collected
            // a second time.
            var (billing, invoice) = await SeedInvoiceAsync(amount: 600);

            // Two pending cash intents on one invoice (an older declaration plus a fresh one).
            // Seeded directly: InitiateParentPaymentAsync deliberately supersedes prior intents.
            var older = new PaymentTransaction
            {
                InvoiceId = invoice.Id, PaymentAccountId = invoice.PaymentAccountId, Amount = 600,
                Currency = invoice.Currency, Status = TransactionStatus.Pending,
                GatewayTransactionId = $"CASH-{Guid.NewGuid():N}", Method = PaymentMethod.Cash,
            };
            var newer = new PaymentTransaction
            {
                InvoiceId = invoice.Id, PaymentAccountId = invoice.PaymentAccountId, Amount = 600,
                Currency = invoice.Currency, Status = TransactionStatus.Pending,
                GatewayTransactionId = $"CASH-{Guid.NewGuid():N}", Method = PaymentMethod.Cash,
            };
            _db.Context.PaymentTransactions.AddRange(older, newer);
            await _db.Context.SaveChangesAsync();

            await billing.ConfirmCashIntentAsync(newer.Id, new ConfirmCashIntentRequest());

            var (verifyContext, _) = _db.CreateConcurrentSession();
            Assert.Equal(TransactionStatus.Success, (await verifyContext.PaymentTransactions.FirstAsync(t => t.Id == newer.Id)).Status);
            var swept = await verifyContext.PaymentTransactions.FirstAsync(t => t.Id == older.Id);
            Assert.Equal(TransactionStatus.Failed, swept.Status);
            Assert.Contains("settled by another payment", swept.FailureReason!);
            verifyContext.Dispose();
        }

        [Fact]
        public async Task RequestRefund_ConcurrentRequests_MustNotStackBeyondTheTransactionAmount()
        {
            // SCOPE NOTE: as with the other concurrency tests here, SQLite serializes the two
            // contexts onto one connection, so this stages the interleaving rather than racing
            // it — request 2 computes its "already refunded" total from the same committed state
            // request 1 saw. That is exactly the read the check-then-insert depends on; on
            // Postgres two overlapping requests reach it by timing instead.
            var gateway = new FakePaymentGateway();
            var parentUser = await _db.SeedUserAsync($"ref-stack-{Guid.NewGuid():N}@test.com", "x", UserRole.Parent);
            var parentProfile = new ParentProfile { UserId = parentUser.Id };
            _db.Context.AddRange(parentProfile,
                new PaymentAccount { Name = "P", DepartmentId = WellKnownDepartments.Phonics, GatewayProvider = "t", GatewayAccountRef = "p" });
            await _db.Context.SaveChangesAsync();

            var billing1 = CreateBillingService(gateway);
            var invoice = await billing1.CreateInvoiceAsync(new CreateInvoiceRequest
            {
                ParentProfileId = parentProfile.Id, DepartmentId = WellKnownDepartments.Phonics, Amount = 1000,
                DueDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(7)),
            });
            await billing1.RecordPaymentAsync(invoice.Id, new RecordPaymentRequest { Amount = 1000 });
            var txn = await _db.Context.PaymentTransactions.AsNoTracking().FirstAsync(t => t.InvoiceId == invoice.Id);

            // A second scoped DbContext, as a concurrent HTTP request would get.
            var (context2, uow2) = _db.CreateConcurrentSession();
            var auditLog2 = new AuditLogService(uow2, _db.CurrentUser);
            var emailTemplates2 = new EmailTemplateService(uow2, auditLog2, new MemoryCache(new MemoryCacheOptions()));
            var notifications2 = new NotificationService(uow2, _emailSender, emailTemplates2, NullLogger<NotificationService>.Instance);
            var billing2 = new BillingService(uow2, auditLog2, gateway, notifications2, _db.CurrentUser, _bulkFileReader, _invoicePdfGenerator);

            var request = () => new RequestRefundRequest
            {
                PaymentTransactionId = txn.Id, Amount = 1000, Reason = "Full refund",
            };

            var task1 = billing1.RequestRefundAsync(request());
            var task2 = billing2.RequestRefundAsync(request());

            try
            {
                await Task.WhenAll(task1, task2);
            }
            catch
            {
                // One of the two being refused is the correct outcome; the assertion below is
                // on the persisted total, not on which request won.
            }

            var (verifyContext, _) = _db.CreateConcurrentSession();
            var live = await verifyContext.Refunds
                .Where(r => r.PaymentTransactionId == txn.Id && r.Status != RefundStatus.Rejected)
                .ToListAsync();
            var total = live.Sum(r => r.Amount);
            verifyContext.Dispose();
            context2.Dispose();

            // The ceiling RequestRefundAsync exists to enforce. Two live refunds of 1000 against
            // one 1000 transaction are each individually approvable, and ReviewRefundAsync's
            // atomic claim is per-refund-row, so it would disburse 2000 against 1000 collected.
            Assert.True(
                total <= txn.Amount,
                $"Refund requests stacked past the transaction: {live.Count} live refund(s) totalling {total} against a {txn.Amount} payment.");
        }

        // ---- QA pass: persisted data consistency after multi-step operations ----

        [Fact]
        public async Task ApproveEnrollment_PersistsAnInternallyConsistentChildSubscriptionAndInvoice()
        {
            // The existing coverage asserts the three rows exist. This asserts they actually
            // hang together once committed — read back through a fresh context, so nothing is
            // satisfied by the seeding context's change tracker.
            var actingAdmin = await _db.SeedUserAsync($"admin-{Guid.NewGuid():N}@test.com", "x", UserRole.Admin);
            _db.CurrentUser.UserId = actingAdmin.Id;

            var parentUser = await _db.SeedUserAsync($"p-{Guid.NewGuid():N}@test.com", "x", UserRole.Parent);
            var parentProfile = new ParentProfile { UserId = parentUser.Id };
            var plan = new PackagePlan
            {
                Name = "Phonics Monthly", BillingType = BillingType.Subscription,
                BillingCycle = BillingCycle.Monthly, Price = 2500,
            };
            var account = new PaymentAccount
            {
                Name = "Phonics", DepartmentId = WellKnownDepartments.Phonics, GatewayProvider = "t", GatewayAccountRef = "p",
            };
            _db.Context.AddRange(parentProfile, plan, account);
            await _db.Context.SaveChangesAsync();

            var service = CreateEnrollmentService();
            await service.SubmitAsync(parentUser.Id, new SubmitEnrollmentFormRequest { FormDataJson = "{\"childName\":\"Kid One\",\"dob\":\"2016-01-01\",\"grade\":\"3\",\"courseInterest\":\"Math\"}" });
            var formId = (await service.ListAsync(null)).Single().Id;
            await service.ReviewAsync(formId, new ReviewEnrollmentFormRequest
            {
                Approve = true, ChildFirstName = "Kid", ChildLastName = "One",
                ChildDateOfBirth = DateOnly.FromDateTime(DateTime.UtcNow).AddYears(-8), PackagePlanId = plan.Id,
            });

            var (verifyContext, _) = _db.CreateConcurrentSession();
            var child = await verifyContext.Children.SingleAsync();
            var subscription = await verifyContext.Subscriptions.SingleAsync();
            var invoice = await verifyContext.Invoices.SingleAsync();

            // Every foreign key points at the row it claims to.
            Assert.Equal(parentProfile.Id, child.ParentProfileId);
            Assert.Equal(parentProfile.Id, subscription.ParentProfileId);
            Assert.Equal(child.Id, subscription.ChildId);
            Assert.Equal(plan.Id, subscription.PackagePlanId);
            Assert.Equal(parentProfile.Id, invoice.ParentProfileId);
            Assert.Equal(child.Id, invoice.ChildId);
            Assert.Equal(subscription.Id, invoice.SubscriptionId);
            Assert.Equal(account.Id, invoice.PaymentAccountId); // routed to the department's account

            // Money and billing pointers agree with the plan.
            Assert.Equal(plan.Price, invoice.Amount);
            Assert.Equal(0m, invoice.AmountPaid);
            Assert.Equal(InvoiceStatus.Pending, invoice.Status);
            Assert.False(string.IsNullOrWhiteSpace(invoice.InvoiceNumber));
            Assert.Equal(SubscriptionStatus.Active, subscription.Status);
            Assert.NotNull(subscription.NextBillingAtUtc);
            Assert.True(subscription.NextBillingAtUtc > DateTime.UtcNow, "the first renewal must be in the future");

            // Audit fields on the AuditEntity rows are actually stamped, not left default.
            Assert.NotEqual(default, invoice.CreatedAtUtc);
            Assert.Equal(actingAdmin.Id, invoice.CreatedBy);
            Assert.Equal(actingAdmin.Id, subscription.CreatedBy);
            Assert.False(invoice.IsDeleted);

            // The approved form is linked to the child it created.
            var form = await verifyContext.EnrollmentForms.SingleAsync(f => f.Id == formId);
            Assert.Equal(EnrollmentFormStatus.Approved, form.Status);
            verifyContext.Dispose();
        }

        [Fact]
        public async Task SoftDeletedChild_IsExcludedFromParentDashboardAndBatchAssignment()
        {
            var parentUser = await _db.SeedUserAsync($"sd-{Guid.NewGuid():N}@test.com", "x", UserRole.Parent);
            var parentProfile = new ParentProfile { UserId = parentUser.Id };
            _db.Context.Add(parentProfile);
            await _db.Context.SaveChangesAsync();

            var kept = new Child { ParentProfileId = parentProfile.Id, FirstName = "Kept", LastName = "Child" };
            var removed = new Child { ParentProfileId = parentProfile.Id, FirstName = "Removed", LastName = "Child" };
            _db.Context.Children.AddRange(kept, removed);
            await _db.Context.SaveChangesAsync();

            // Repository.Remove is transparently converted to a soft delete by the interceptor.
            _db.UnitOfWork.Repository<Child>().Remove(removed);
            await _db.UnitOfWork.SaveChangesAsync();

            var (verifyContext, _) = _db.CreateConcurrentSession();
            var stored = await verifyContext.Children.IgnoreQueryFilters().SingleAsync(c => c.Id == removed.Id);
            Assert.True(stored.IsDeleted);
            Assert.NotNull(stored.DeletedAtUtc); // the row survives for history, it is not erased
            verifyContext.Dispose();

            // The global query filter must keep it out of everything user-facing.
            var dashboard = await new ParentPortalService(_db.UnitOfWork).GetDashboardAsync(parentUser.Id);
            var only = Assert.Single(dashboard.Children);
            Assert.Equal(kept.Id, only.ChildId);
        }

        // ---- QA pass: cross-account isolation on the parent portal ----

        [Fact]
        public async Task ParentPortal_NeverLeaksAnotherParentsInvoicesOrRecordings()
        {
            var (billingA, invoiceA) = await SeedInvoiceAsync(amount: 1000);
            var parentAUserId = await _db.Context.ParentProfiles.AsNoTracking()
                .Where(p => p.Id == invoiceA.ParentProfileId).Select(p => p.UserId).FirstAsync();

            var intruderUser = await _db.SeedUserAsync($"intruder-{Guid.NewGuid():N}@test.com", "x", UserRole.Parent);
            _db.Context.ParentProfiles.Add(new ParentProfile { UserId = intruderUser.Id });
            await _db.Context.SaveChangesAsync();

            var portal = new ParentPortalService(_db.UnitOfWork);

            // Listing is scoped to the caller's own profile.
            Assert.Empty(await portal.GetInvoicesAsync(intruderUser.Id));
            Assert.Single(await portal.GetInvoicesAsync(parentAUserId));

            // And every id-keyed read/write on someone else's invoice is refused, not served.
            await Assert.ThrowsAsync<NotFoundException>(() => billingA.GenerateParentInvoicePdfAsync(intruderUser.Id, invoiceA.Id));
            await Assert.ThrowsAsync<NotFoundException>(() =>
                billingA.InitiateParentPaymentAsync(intruderUser.Id, invoiceA.Id, new InitiateParentPaymentRequest { MethodKey = "cash" }));
            await Assert.ThrowsAsync<NotFoundException>(() =>
                billingA.StartParentInlineCheckoutAsync(intruderUser.Id, invoiceA.Id, new InitiateParentPaymentRequest { MethodKey = "upi" }));
            await Assert.ThrowsAsync<NotFoundException>(() => billingA.ReconcileInvoicePaymentAsync(intruderUser.Id, invoiceA.Id));

            // Recordings of a class the intruder's child is not enrolled in.
            var (_, _, session) = await SeedBatchWithSessionAsync(totalSessions: 1);
            await CreateSessionService().AddRecordingAsync(session.Id, new RegisterRecordingRequest
            {
                StorageUrl = "https://recordings.test/private.mp4", DurationSeconds = 600,
            });
            await Assert.ThrowsAsync<NotFoundException>(() => portal.GetRecordingsAsync(intruderUser.Id, session.Id));
        }

        // ---- QA pass: input validation at the service boundary ----

        [Fact]
        public async Task RecordPayment_RejectsNonPositiveAndOverpayingAmounts()
        {
            var (billing, invoice) = await SeedInvoiceAsync(amount: 1000);

            // Overpayment is already refused; the boundary cases are the interesting ones.
            await Assert.ThrowsAsync<DomainValidationException>(() =>
                billing.RecordPaymentAsync(invoice.Id, new RecordPaymentRequest { Amount = 1000.01m }));

            await billing.RecordPaymentAsync(invoice.Id, new RecordPaymentRequest { Amount = 1000 });

            // A settled invoice takes no further payment at all.
            await Assert.ThrowsAsync<DomainValidationException>(() =>
                billing.RecordPaymentAsync(invoice.Id, new RecordPaymentRequest { Amount = 1 }));

            var (verifyContext, _) = _db.CreateConcurrentSession();
            var settled = await verifyContext.Invoices.FirstAsync(i => i.Id == invoice.Id);
            Assert.Equal(1000m, settled.AmountPaid);
            Assert.NotNull(settled.PaidAtUtc);
            verifyContext.Dispose();
        }

        [Fact]
        public async Task SubmitLeave_RejectsInvertedAndZeroLengthWindows()
        {
            var teacherUser = await _db.SeedUserAsync($"lv-{Guid.NewGuid():N}@test.com", "x", UserRole.Teacher);
            _db.Context.TeacherProfiles.Add(new TeacherProfile { UserId = teacherUser.Id });
            await _db.Context.SaveChangesAsync();

            var ops = CreateAcademicOpsService();
            var start = DateTime.UtcNow.AddDays(5);

            await Assert.ThrowsAsync<DomainValidationException>(() =>
                ops.SubmitLeaveAsync(teacherUser.Id, new SubmitLeaveRequest
                {
                    StartAtUtc = start, EndAtUtc = start.AddDays(-1), Reason = "Inverted window",
                }));

            await Assert.ThrowsAsync<DomainValidationException>(() =>
                ops.SubmitLeaveAsync(teacherUser.Id, new SubmitLeaveRequest
                {
                    StartAtUtc = start, EndAtUtc = start, Reason = "Zero-length window",
                }));

            Assert.Empty(await _db.Context.LeaveRequests.ToListAsync());
        }

        [Fact]
        public async Task Gamification_RejectsSelfMintedMilestonesAndNonParticipants()
        {
            var (_, _, session) = await SeedBatchWithSessionAsync(totalSessions: 1);
            var outsiderUser = await _db.SeedUserAsync($"out-{Guid.NewGuid():N}@test.com", "x", UserRole.Parent);
            _db.Context.ParentProfiles.Add(new ParentProfile { UserId = outsiderUser.Id });
            await _db.Context.SaveChangesAsync();

            var gamification = CreateGamificationService();

            // A client must never be able to mint a milestone directly — they are server-computed.
            await Assert.ThrowsAsync<DomainValidationException>(() =>
                gamification.GrantAsync(outsiderUser.Id, new GrantAwardRequest
                {
                    SessionId = session.Id, ParticipantName = "Kid", Kind = AwardKind.Milestone, Points = 0,
                }));

            // A parent with no child in this batch is not a participant.
            await Assert.ThrowsAsync<ForbiddenException>(() =>
                gamification.GrantAsync(outsiderUser.Id, new GrantAwardRequest
                {
                    SessionId = session.Id, ParticipantName = "Kid", Kind = AwardKind.Star, Points = 1,
                }));

            // Nor may a non-staff caller award outside any session at all.
            await Assert.ThrowsAsync<ForbiddenException>(() =>
                gamification.GrantAsync(outsiderUser.Id, new GrantAwardRequest
                {
                    ParticipantName = "Kid", Kind = AwardKind.Star, Points = 1,
                }));

            Assert.Empty(await _db.Context.StudentAwards.ToListAsync());
        }

        [Fact]
        public async Task Gamification_ParentReportsStarForOwnEnrolledChild_Succeeds()
        {
            var (batch, _, session) = await SeedBatchWithSessionAsync(totalSessions: 1);
            var parentUser = await _db.SeedUserAsync($"p-{Guid.NewGuid():N}@test.com", "x", UserRole.Parent);
            var parentProfile = new ParentProfile { UserId = parentUser.Id };
            var child = new Child { ParentProfile = parentProfile, FirstName = "Aarav", LastName = "Kapoor" };
            _db.Context.AddRange(parentProfile, child);
            await _db.Context.SaveChangesAsync();
            _db.Context.Add(new BatchEnrollment { BatchId = batch.Id, ChildId = child.Id });
            await _db.Context.SaveChangesAsync();

            var gamification = CreateGamificationService();
            var granted = await gamification.GrantAsync(parentUser.Id, new GrantAwardRequest
            {
                SessionId = session.Id, ParticipantName = "Aarav Kapoor", Kind = AwardKind.Star, Points = 1,
            });

            Assert.Single(granted);
            Assert.Equal("Aarav Kapoor", Assert.Single(await _db.Context.StudentAwards.ToListAsync()).ParticipantName);
        }

        /// <summary>
        /// A live-quiz self-report used to take ParticipantName as free text with no check it
        /// was actually the caller's own child — any parent with a child in the batch could post
        /// a Star under a classmate's name, inflating that classmate's persisted award history
        /// and leaderboard rank.
        /// </summary>
        [Fact]
        public async Task Gamification_ParentReportsStarUnderAnotherChildsName_IsForbidden()
        {
            var (batch, _, session) = await SeedBatchWithSessionAsync(totalSessions: 1);
            var parentUser = await _db.SeedUserAsync($"p-{Guid.NewGuid():N}@test.com", "x", UserRole.Parent);
            var parentProfile = new ParentProfile { UserId = parentUser.Id };
            var ownChild = new Child { ParentProfile = parentProfile, FirstName = "Aarav", LastName = "Kapoor" };
            _db.Context.AddRange(parentProfile, ownChild);
            await _db.Context.SaveChangesAsync();
            _db.Context.Add(new BatchEnrollment { BatchId = batch.Id, ChildId = ownChild.Id });
            await _db.Context.SaveChangesAsync();

            var gamification = CreateGamificationService();
            await Assert.ThrowsAsync<ForbiddenException>(() =>
                gamification.GrantAsync(parentUser.Id, new GrantAwardRequest
                {
                    // A genuinely-enrolled parent, but naming a different child than their own.
                    SessionId = session.Id, ParticipantName = "Some Classmate", Kind = AwardKind.Star, Points = 1,
                }));

            Assert.Empty(await _db.Context.StudentAwards.ToListAsync());
        }

        /// <summary>A parent with a payment account and one open invoice, plus a live BillingService.</summary>
        private async Task<(BillingService Billing, Invoice Invoice)> SeedInvoiceAsync(decimal amount)
        {
            var parentUser = await _db.SeedUserAsync($"inv-{Guid.NewGuid():N}@test.com", "x", UserRole.Parent);
            var parentProfile = new ParentProfile { UserId = parentUser.Id };
            _db.Context.AddRange(parentProfile,
                new PaymentAccount { Name = "P", DepartmentId = WellKnownDepartments.Phonics, GatewayProvider = "t", GatewayAccountRef = "p" });
            await _db.Context.SaveChangesAsync();

            var billing = CreateBillingService();
            var dto = await billing.CreateInvoiceAsync(new CreateInvoiceRequest
            {
                ParentProfileId = parentProfile.Id, DepartmentId = WellKnownDepartments.Phonics, Amount = amount,
                DueDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(7)),
            });
            return (billing, await _db.Context.Invoices.AsNoTracking().FirstAsync(i => i.Id == dto.Id));
        }

        // ---- QA pass: input validation on financially-sensitive fields ----

        [Fact]
        public async Task PayoutRate_RejectsNegativeRateAndOutOfRangeNoShowPenalty()
        {
            var payouts = CreatePayoutService();
            var effectiveFrom = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1));

            // A negative per-minute rate makes every completed class DEDUCT from the teacher.
            await Assert.ThrowsAsync<DomainValidationException>(() => payouts.SetRateAsync(new SavePayoutRateRequest
            {
                RatePerMinute = -500, EffectiveFrom = effectiveFrom,
            }));

            // A negative penalty percent inverts the sign of the no-show deduction
            // (-(rate * -100 / 100) = +rate), turning a missed class into a BONUS.
            await Assert.ThrowsAsync<DomainValidationException>(() => payouts.SetRateAsync(new SavePayoutRateRequest
            {
                RatePerMinute = 500, TeacherNoShowPenaltyPercent = -100, EffectiveFrom = effectiveFrom,
            }));

            // Deducting many times the session's worth is not a configuration, it's a typo.
            await Assert.ThrowsAsync<DomainValidationException>(() => payouts.SetRateAsync(new SavePayoutRateRequest
            {
                RatePerMinute = 500, TeacherNoShowPenaltyPercent = 10_000, EffectiveFrom = effectiveFrom,
            }));

            // The legitimate range still saves: 0% is a warning-only no-show, and >100% stays
            // allowed on purpose — deducting more than the session was worth is a supported
            // policy (WBS p.31), which is why the guard bounds the sign and typos, not the policy.
            var saved = await payouts.SetRateAsync(new SavePayoutRateRequest
            {
                RatePerMinute = 500, TeacherNoShowPenaltyPercent = 0, EffectiveFrom = effectiveFrom,
            });
            Assert.Equal(0m, saved.TeacherNoShowPenaltyPercent);

            var punitive = await payouts.SetRateAsync(new SavePayoutRateRequest
            {
                RatePerMinute = 500, TeacherNoShowPenaltyPercent = 150, EffectiveFrom = effectiveFrom,
            });
            Assert.Equal(150m, punitive.TeacherNoShowPenaltyPercent);
        }

        [Fact]
        public async Task CompletingASession_AccruesRatePerMinuteTimesTheSessionsScheduledDuration()
        {
            // Rate cards price per minute now, not a flat amount per session -- the same
            // configured rate must NOT pay a 30-minute class and a 50-minute class the same
            // amount. This is the actual proof: two sessions at one rate, priced strictly
            // by each session's own scheduled duration.
            var teacherUser = await _db.SeedUserAsync($"t-{Guid.NewGuid():N}@test.com", "x", UserRole.Teacher);
            var teacher = new TeacherProfile { UserId = teacherUser.Id };
            var category = new CourseCategory { Name = $"Cat-{Guid.NewGuid():N}", DepartmentId = WellKnownDepartments.Phonics };
            var course = new Course
            {
                CourseCategory = category, Name = "Mixed Length Course", Type = CourseType.Group,
                DurationMinutes = 50, Price = 100, TotalSessions = 2, DepartmentId = WellKnownDepartments.Phonics,
            };
            var batch = new Batch { Course = course, TeacherProfile = teacher, Name = "Batch", Capacity = 5 };
            var shortSession = new ClassSession
            {
                Batch = batch, TeacherProfile = teacher,
                ScheduledStartAtUtc = DateTime.UtcNow.AddDays(1),
                ScheduledEndAtUtc = DateTime.UtcNow.AddDays(1).AddMinutes(30),
            };
            var longSession = new ClassSession
            {
                Batch = batch, TeacherProfile = teacher,
                ScheduledStartAtUtc = DateTime.UtcNow.AddDays(2),
                ScheduledEndAtUtc = DateTime.UtcNow.AddDays(2).AddMinutes(50),
            };
            _db.Context.AddRange(teacher, category, course, batch, shortSession, longSession);
            await _db.Context.SaveChangesAsync();
            _db.CurrentUser.UserId = teacherUser.Id;

            await CreatePayoutService().SetRateAsync(new SavePayoutRateRequest
            {
                RatePerMinute = 15, EffectiveFrom = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1)),
            });
            await SeedFullTeacherAttendanceAsync(shortSession);
            await SeedFullTeacherAttendanceAsync(longSession);
            await CreateSessionService().CompleteAsync(shortSession.Id, new CompleteSessionRequest());
            await CreateSessionService().CompleteAsync(longSession.Id, new CompleteSessionRequest());

            var shortItem = await _db.Context.PayoutItems.AsNoTracking().FirstAsync(i => i.ClassSessionId == shortSession.Id);
            var longItem = await _db.Context.PayoutItems.AsNoTracking().FirstAsync(i => i.ClassSessionId == longSession.Id);
            Assert.Equal(450m, shortItem.Amount); // 15/min * 30 min
            Assert.Equal(750m, longItem.Amount); // 15/min * 50 min
        }

        [Fact]
        public async Task PayoutRate_HistoricalVersioning_PricesEachSessionAtTheRateEffectiveOnItsOwnDate()
        {
            // "The rate effective on the session date" (AccrueForSessionAsync's own comment) is a
            // real invariant with no direct test anywhere before this: a rate change must only
            // affect sessions from its effective date forward -- never retroactively repricing a
            // session that already happened under the old rate.
            var teacherUser = await _db.SeedUserAsync($"t-{Guid.NewGuid():N}@test.com", "x", UserRole.Teacher);
            var teacher = new TeacherProfile { UserId = teacherUser.Id };
            var category = new CourseCategory { Name = $"Cat-{Guid.NewGuid():N}", DepartmentId = WellKnownDepartments.Phonics };
            var course = new Course
            {
                CourseCategory = category, Name = "Course", Type = CourseType.Group,
                DurationMinutes = 30, Price = 100, TotalSessions = 2, DepartmentId = WellKnownDepartments.Phonics,
            };
            var batch = new Batch { Course = course, TeacherProfile = teacher, Name = "Batch", Capacity = 5 };
            var oldSession = new ClassSession
            {
                Batch = batch, TeacherProfile = teacher,
                ScheduledStartAtUtc = DateTime.UtcNow.AddDays(-10), ScheduledEndAtUtc = DateTime.UtcNow.AddDays(-10).AddMinutes(30),
            };
            var newSession = new ClassSession
            {
                Batch = batch, TeacherProfile = teacher,
                ScheduledStartAtUtc = DateTime.UtcNow.AddDays(1), ScheduledEndAtUtc = DateTime.UtcNow.AddDays(1).AddMinutes(30),
            };
            _db.Context.AddRange(teacher, category, course, batch, oldSession, newSession);
            await _db.Context.SaveChangesAsync();
            _db.CurrentUser.UserId = teacherUser.Id;

            var payouts = CreatePayoutService();
            // Original rate, effective well in the past -- covers oldSession.
            await payouts.SetRateAsync(new SavePayoutRateRequest
            {
                TeacherProfileId = teacher.Id, RatePerMinute = 10,
                EffectiveFrom = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-30)),
            });
            // A raise, effective from today -- must NOT retroactively touch oldSession, dated 10 days ago.
            await payouts.SetRateAsync(new SavePayoutRateRequest
            {
                TeacherProfileId = teacher.Id, RatePerMinute = 20,
                EffectiveFrom = DateOnly.FromDateTime(DateTime.UtcNow),
            });

            await SeedFullTeacherAttendanceAsync(oldSession);
            await SeedFullTeacherAttendanceAsync(newSession);
            await CreateSessionService().CompleteAsync(oldSession.Id);
            await CreateSessionService().CompleteAsync(newSession.Id);

            var oldItem = await _db.Context.PayoutItems.AsNoTracking().FirstAsync(i => i.ClassSessionId == oldSession.Id);
            var newItem = await _db.Context.PayoutItems.AsNoTracking().FirstAsync(i => i.ClassSessionId == newSession.Id);
            Assert.Equal(300m, oldItem.Amount); // 10/min * 30 min -- the rate in force 10 days ago
            Assert.Equal(600m, newItem.Amount); // 20/min * 30 min -- the raise now in force
        }

        [Fact]
        public async Task PayoutRate_FutureDatedRate_DoesNotApplyToASessionCompletedBeforeItsEffectiveDate()
        {
            // The reverse of the versioning test above: a rate scheduled to take effect NEXT
            // month must not retroactively price a session completing today. "Effective on the
            // session date" cuts both ways -- old rates still apply to old sessions, and a
            // future rate doesn't apply early just because a row for it already exists.
            var (_, _, session) = await SeedBatchWithSessionAsync(totalSessions: 1); // 45-minute session
            await CreatePayoutService().SetRateAsync(new SavePayoutRateRequest
            {
                TeacherProfileId = session.TeacherProfileId, RatePerMinute = 50,
                EffectiveFrom = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(30)),
            });
            await SeedFullTeacherAttendanceAsync(session);

            await CreateSessionService().CompleteAsync(session.Id);

            var item = await _db.Context.PayoutItems.AsNoTracking().FirstAsync(i => i.ClassSessionId == session.Id);
            Assert.Equal(0m, item.Amount); // no rate is actually in force yet
            Assert.Contains("No payout rate configured", item.Note);
        }

        [Fact]
        public async Task Complete_DoesNotFlag_WhenAttendedExactlyAtTheReviewThreshold()
        {
            // The review check is strictly "<" the threshold (AccrueForSessionAsync), so
            // attendance landing exactly on the boundary must NOT flag -- only falling short of
            // it should. The closest existing coverage (Complete_UsesConfiguredMinAttendancePercent...)
            // sits one point below its threshold, never exactly on one.
            var (_, _, session) = await SeedBatchWithSessionAsync(totalSessions: 1); // 45-minute session
            await CreatePayoutService().SetRateAsync(new SavePayoutRateRequest
            {
                TeacherProfileId = session.TeacherProfileId, RatePerMinute = 10,
                EffectiveFrom = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1)),
            });
            var joinedAt = DateTime.UtcNow.AddMinutes(-22.5);
            _db.Context.SessionAttendances.Add(new SessionAttendance
            {
                ClassSessionId = session.Id,
                ParticipantType = ParticipantType.Teacher,
                TeacherProfileId = session.TeacherProfileId,
                Status = AttendanceStatus.Present,
                JoinedAtUtc = joinedAt,
                LeftAtUtc = joinedAt.AddMinutes(22.5), // exactly 50% of the 45-minute session
            });
            await _db.Context.SaveChangesAsync();

            await CreateSessionService().CompleteAsync(session.Id);

            var item = Assert.Single(_db.Context.PayoutItems.ToList());
            Assert.False(item.RequiresReview);
        }

        [Fact]
        public async Task AccrueForSession_RoundsAFractionalScheduledDurationToTheNearestMinute()
        {
            // Session lengths aren't always a clean whole number of minutes once custom
            // durations are involved -- pins the actual rounding behaviour (Math.Round's default
            // MidpointRounding.ToEven, i.e. banker's rounding) so a future change to it is caught
            // rather than silently shifting every affected teacher's pay by a few paise. Worth a
            // second look if session lengths should instead round in the teacher's favour
            // (AwayFromZero) rather than to the nearest even minute.
            var teacherUser = await _db.SeedUserAsync($"t-{Guid.NewGuid():N}@test.com", "x", UserRole.Teacher);
            var teacher = new TeacherProfile { UserId = teacherUser.Id };
            var category = new CourseCategory { Name = $"Cat-{Guid.NewGuid():N}", DepartmentId = WellKnownDepartments.Phonics };
            var course = new Course
            {
                CourseCategory = category, Name = "Course", Type = CourseType.Group,
                DurationMinutes = 30, Price = 100, TotalSessions = 1, DepartmentId = WellKnownDepartments.Phonics,
            };
            var batch = new Batch { Course = course, TeacherProfile = teacher, Name = "Batch", Capacity = 5 };
            var start = DateTime.UtcNow.AddDays(1);
            var session = new ClassSession
            {
                Batch = batch, TeacherProfile = teacher,
                ScheduledStartAtUtc = start, ScheduledEndAtUtc = start.AddSeconds(30 * 60 + 30), // 30 min 30 sec
            };
            _db.Context.AddRange(teacher, category, course, batch, session);
            await _db.Context.SaveChangesAsync();
            _db.CurrentUser.UserId = teacherUser.Id;

            await CreatePayoutService().SetRateAsync(new SavePayoutRateRequest
            {
                TeacherProfileId = teacher.Id, RatePerMinute = 10,
                EffectiveFrom = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1)),
            });
            await SeedFullTeacherAttendanceAsync(session);

            await CreateSessionService().CompleteAsync(session.Id);

            var item = await _db.Context.PayoutItems.AsNoTracking().FirstAsync(i => i.ClassSessionId == session.Id);
            Assert.Equal(300m, item.Amount); // 10/min * 30 min -- 30.5 rounds DOWN to the even number
        }

        [Fact]
        public async Task CaptureJoinAttendance_MultipleReconnects_KeepsOriginalJoinAcrossThreeDropCycles()
        {
            // The single-reconnect regression test (TeacherRejoinAfterDrop...) only proves one
            // drop/reconnect cycle. A genuinely shaky connection drops and reconnects several
            // times over one class -- this must still never bump JoinedAtUtc forward, and must
            // never leave a stale LeftAtUtc behind after the final reconnect.
            var (_, _, session) = await SeedBatchWithSessionAsync(totalSessions: 1);
            var teacherProfile = await _db.Context.TeacherProfiles.FindAsync(session.TeacherProfileId);
            var ops = CreateAcademicOpsService();

            await ops.CaptureJoinAttendanceAsync(session.Id, teacherProfile!.UserId);
            var originalJoin = _db.Context.SessionAttendances.AsNoTracking().Single(a => a.ClassSessionId == session.Id).JoinedAtUtc;

            for (var i = 0; i < 3; i++)
            {
                await ops.CaptureLeaveAttendanceAsync(session.Id, teacherProfile.UserId);
                Assert.NotNull(_db.Context.SessionAttendances.AsNoTracking().Single(a => a.ClassSessionId == session.Id).LeftAtUtc);
                await ops.CaptureJoinAttendanceAsync(session.Id, teacherProfile.UserId);
            }

            var row = _db.Context.SessionAttendances.AsNoTracking().Single(a => a.ClassSessionId == session.Id);
            Assert.Equal(originalJoin, row.JoinedAtUtc);
            Assert.Null(row.LeftAtUtc);
        }

        [Fact]
        public async Task AdjustItemAsync_ClearsReviewFlag_WhenConfirmingTheSameAmount()
        {
            // AdjustItemAsync's own comment: it clears RequiresReview whether or not the amount
            // actually changes, so "reviewed, full amount stands" is a real recorded admin
            // decision -- not something FinalizeAsync's guard could otherwise be worked around by
            // leaving the flagged item untouched. The only existing coverage always changes the
            // amount; this is the "confirm as-is" path.
            var (_, _, session) = await SeedBatchWithSessionAsync(totalSessions: 1); // 45-minute session
            var payouts = CreatePayoutService();
            await payouts.SetRateAsync(new SavePayoutRateRequest
            {
                TeacherProfileId = session.TeacherProfileId, RatePerMinute = 1000,
                EffectiveFrom = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1)),
            });
            // No attendance at all recorded -- flags for review per AccrueForSessionAsync.
            await CreateSessionService().CompleteAsync(session.Id);

            var payout = await _db.Context.Payouts.AsNoTracking().FirstAsync();
            var flaggedItem = Assert.Single(_db.Context.PayoutItems.ToList());
            Assert.True(flaggedItem.RequiresReview);

            var confirmed = await payouts.AdjustItemAsync(payout.Id, flaggedItem.Id, new AdjustPayoutItemRequest
            {
                NewAmount = flaggedItem.Amount, // admin verified it; the full scheduled amount stands
                Reason = "Verified with the teacher directly; class did run as scheduled.",
            });
            var confirmedItem = Assert.Single(confirmed.Items);
            Assert.False(confirmedItem.RequiresReview);
            Assert.Equal(flaggedItem.Amount, confirmedItem.Amount);

            var finalized = await payouts.FinalizeAsync(payout.Id);
            Assert.Equal(flaggedItem.Amount, finalized.TotalAmount);
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
            var category = new CourseCategory { Name = $"Cat-{Guid.NewGuid():N}", DepartmentId = WellKnownDepartments.Phonics };
            var course = new Course
            {
                CourseCategory = category,
                Name = "Course",
                Type = CourseType.Group,
                DurationMinutes = 45,
                Price = 100,
                TotalSessions = totalSessions,
                DepartmentId = WellKnownDepartments.Phonics,
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

            // Session write/read paths (complete, no-show, attendance, recordings, engagement)
            // are scoped to the session's own teacher, so the acting user defaults to exactly
            // that teacher here — the realistic caller. Tests that need a different actor
            // (an unrelated teacher, a parent) overwrite this after seeding.
            _db.CurrentUser.UserId = teacherUser.Id;
            return (batch, course, session);
        }

        /// <summary>
        /// For tests whose own concern is downstream of accrual (finalize, mark-paid, ...) and
        /// just need CompleteAsync to accrue a plain, unflagged SessionEarning -- without this,
        /// completing a seeded session with no SessionAttendance row trips the "no attendance was
        /// ever recorded" review flag (see PayoutService.AccrueForSessionAsync), which is correct
        /// behavior but not what these tests are about.
        /// </summary>
        private async Task SeedFullTeacherAttendanceAsync(ClassSession session)
        {
            _db.Context.SessionAttendances.Add(new SessionAttendance
            {
                ClassSessionId = session.Id,
                ParticipantType = ParticipantType.Teacher,
                TeacherProfileId = session.TeacherProfileId,
                Status = AttendanceStatus.Present,
                JoinedAtUtc = session.ScheduledStartAtUtc,
                LeftAtUtc = session.ScheduledEndAtUtc,
            });
            await _db.Context.SaveChangesAsync();
        }

        // ---- QA round 7: regression coverage ----

        /// <summary>
        /// BUG-001. A cancelled subscription whose child has since been re-subscribed to the
        /// same plan cannot be renewed — that would be a second Active row for the same
        /// child+plan, which CreateSubscriptionAsync forbids and the DB's partial unique index
        /// blocks. RenewSubscriptionAsync made neither check, so the index violation escaped as
        /// a raw DbUpdateException (HTTP 500) instead of the 409 the admin should have seen.
        /// </summary>
        [Fact]
        public async Task RenewSubscription_Conflicts_WhenChildAlreadyHasAnActiveSubscriptionOnThatPlan()
        {
            var (parentProfile, child, plan) = await SeedSubscriptionFixtureAsync();
            var billing = CreateBillingService();

            var first = await billing.CreateSubscriptionAsync(new CreateSubscriptionRequest
            {
                ParentProfileId = parentProfile.Id,
                ChildId = child.Id,
                PackagePlanId = plan.Id,
                StartDate = DateOnly.FromDateTime(DateTime.UtcNow),
            });
            await billing.CancelSubscriptionAsync(first.Id);

            // Free to re-subscribe now that the first one is Cancelled.
            var second = await billing.CreateSubscriptionAsync(new CreateSubscriptionRequest
            {
                ParentProfileId = parentProfile.Id,
                ChildId = child.Id,
                PackagePlanId = plan.Id,
                StartDate = DateOnly.FromDateTime(DateTime.UtcNow),
            });
            Assert.Equal(SubscriptionStatus.Active, second.Status);

            await Assert.ThrowsAsync<ConflictException>(() => billing.RenewSubscriptionAsync(first.Id));

            // The rejected renewal must leave nothing behind: the old subscription stays
            // Cancelled and — critically — no renewal invoice was raised for it.
            var (context, _) = _db.CreateConcurrentSession();
            using (context)
            {
                var stored = await context.Subscriptions.FirstAsync(s => s.Id == first.Id);
                Assert.Equal(SubscriptionStatus.Cancelled, stored.Status);
                Assert.Null(stored.NextBillingAtUtc);
                // Only the invoice raised when it was first created — the rejected renewal
                // must not have billed the parent for a cycle that never restarted.
                Assert.Equal(1, await context.Invoices.CountAsync(i => i.SubscriptionId == first.Id));
            }
        }

        /// <summary>A genuinely renewable subscription still renews — the new guard must not block the happy path.</summary>
        [Fact]
        public async Task RenewSubscription_StillRenews_WhenNoOtherActiveSubscriptionExists()
        {
            var (parentProfile, child, plan) = await SeedSubscriptionFixtureAsync();
            var billing = CreateBillingService();

            var subscription = await billing.CreateSubscriptionAsync(new CreateSubscriptionRequest
            {
                ParentProfileId = parentProfile.Id,
                ChildId = child.Id,
                PackagePlanId = plan.Id,
                StartDate = DateOnly.FromDateTime(DateTime.UtcNow),
            });
            await billing.CancelSubscriptionAsync(subscription.Id);

            var renewed = await billing.RenewSubscriptionAsync(subscription.Id);

            Assert.Equal(SubscriptionStatus.Active, renewed.Status);
            var (context, _) = _db.CreateConcurrentSession();
            using (context)
            {
                var stored = await context.Subscriptions.FirstAsync(s => s.Id == subscription.Id);
                Assert.Equal(SubscriptionStatus.Active, stored.Status);
                Assert.Null(stored.CancelledAtUtc);
                Assert.NotNull(stored.NextBillingAtUtc);
                // One invoice from the original start, one from the renewal.
                Assert.Equal(2, await context.Invoices.CountAsync(i => i.SubscriptionId == subscription.Id));
            }
        }

        /// <summary>
        /// RenewSubscriptionAsync bills the renewal at the plan's *current* price
        /// (Amount = plan.Price, no proration) — none of the other renewal tests actually
        /// assert the amount, only status/counts. Pins that both the original and the renewal
        /// invoice were raised for exactly the plan price, and that the renewal invoice is a
        /// distinct, still-open row rather than a mutation of the first one.
        /// </summary>
        [Fact]
        public async Task RenewSubscription_InvoicesTheRenewalForExactlyThePlanPrice()
        {
            var (parentProfile, child, plan) = await SeedSubscriptionFixtureAsync();
            var billing = CreateBillingService();

            var subscription = await billing.CreateSubscriptionAsync(new CreateSubscriptionRequest
            {
                ParentProfileId = parentProfile.Id,
                ChildId = child.Id,
                PackagePlanId = plan.Id,
                StartDate = DateOnly.FromDateTime(DateTime.UtcNow),
            });
            await billing.CancelSubscriptionAsync(subscription.Id);
            await billing.RenewSubscriptionAsync(subscription.Id);

            var (context, _) = _db.CreateConcurrentSession();
            using (context)
            {
                var invoices = await context.Invoices
                    .Where(i => i.SubscriptionId == subscription.Id)
                    .OrderBy(i => i.CreatedAtUtc)
                    .ToListAsync();
                Assert.Equal(2, invoices.Count);
                Assert.All(invoices, i => Assert.Equal(plan.Price, i.Amount));

                // The renewal must be its own fresh, unpaid invoice — not the original row
                // relabeled — so the parent is billed once per cycle, not once total.
                Assert.NotEqual(invoices[0].Id, invoices[1].Id);
                Assert.Equal(InvoiceStatus.Pending, invoices[1].Status);
                Assert.Equal(0, invoices[1].AmountPaid);
            }
        }

        /// <summary>
        /// BUG-002. AppSetting.Key is uniquely indexed, so the same key twice in one bulk
        /// upsert inserted two colliding rows and failed at SaveChanges as a 500 — taking every
        /// other setting in the same request with it.
        /// </summary>
        [Fact]
        public async Task UpsertSettings_RejectsADuplicateKeyInTheSamePayload_WithoutPartiallySaving()
        {
            var settings = new SettingsService(_db.UnitOfWork, _auditLog);

            await Assert.ThrowsAsync<DomainValidationException>(() => settings.UpsertAsync(
            [
                new UpdateSettingRequest { Key = "brand.name", Value = "First", Category = SettingCategory.Branding },
                new UpdateSettingRequest { Key = "brand.name", Value = "Second", Category = SettingCategory.Branding },
                new UpdateSettingRequest { Key = "brand.colour", Value = "#fff", Category = SettingCategory.Branding },
            ]));

            // Validation runs before anything is staged, so the unrelated third key must not
            // have been written either — a half-applied settings save is worse than none.
            var (context, _) = _db.CreateConcurrentSession();
            using (context)
            {
                Assert.False(await context.AppSettings.AnyAsync(s => s.Key == "brand.name"));
                Assert.False(await context.AppSettings.AnyAsync(s => s.Key == "brand.colour"));
            }
        }

        /// <summary>
        /// BUG-002 (same fix). UpdateSettingRequest carries no length attributes, so a key or
        /// value longer than the column would pass model validation and fail as a 500 on
        /// Postgres. Both are now bounded in the service, matching the entity.
        /// </summary>
        [Fact]
        public async Task UpsertSettings_RejectsAnOverLongKeyOrValue()
        {
            var settings = new SettingsService(_db.UnitOfWork, _auditLog);

            await Assert.ThrowsAsync<DomainValidationException>(() => settings.UpsertAsync(
                [new UpdateSettingRequest { Key = new string('k', 101), Value = "x", Category = SettingCategory.General }]));

            await Assert.ThrowsAsync<DomainValidationException>(() => settings.UpsertAsync(
                [new UpdateSettingRequest { Key = "brand.blurb", Value = new string('v', 2001), Category = SettingCategory.General }]));

            // Exactly at the limit is still accepted — the guard bounds, it doesn't over-reject.
            var saved = await settings.UpsertAsync(
                [new UpdateSettingRequest { Key = new string('k', 100), Value = new string('v', 2000), Category = SettingCategory.General }]);
            Assert.Contains(saved, s => s.Key.Length == 100 && s.Value!.Length == 2000);
        }

        /// <summary>
        /// BUG-003. SubAdminPermission is uniquely indexed on (UserId, Module). RoleService
        /// already rejected a duplicated module in the role-level matrix; the per-user matrix
        /// didn't, so the same module twice failed at SaveChanges as a 500.
        /// </summary>
        [Fact]
        public async Task SetPermissions_RejectsADuplicateModule_WithoutClearingExistingGrants()
        {
            var admin = await _db.SeedUserAsync($"pa-{Guid.NewGuid():N}@test.com", "x", UserRole.Admin);
            var sub = await _db.SeedUserAsync($"ps-{Guid.NewGuid():N}@test.com", "x", UserRole.SubAdmin);
            var users = CreateUserService();

            await users.SetPermissionsAsync(sub.Id, admin.Id,
                [new PermissionDto { Module = PermissionModule.Admission, CanView = true }]);

            await Assert.ThrowsAsync<DomainValidationException>(() => users.SetPermissionsAsync(sub.Id, admin.Id,
            [
                new PermissionDto { Module = PermissionModule.Settings, CanView = true },
                new PermissionDto { Module = PermissionModule.Settings, CanEdit = true },
            ]));

            // SetPermissionsAsync is replace-all: rejecting late (at SaveChanges) would have
            // meant the existing grants were already staged for removal. The guard runs first,
            // so the sub-admin keeps the access they had.
            var (context, _) = _db.CreateConcurrentSession();
            using (context)
            {
                var grants = await context.SubAdminPermissions.Where(p => p.UserId == sub.Id).ToListAsync();
                Assert.Single(grants);
                Assert.Equal(PermissionModule.Admission, grants[0].Module);
                Assert.True(grants[0].CanView);
            }
        }

        /// <summary>
        /// Regression for a68b1a1: the status filter has to compose with paging and with the
        /// parentProfileId filter. The commit's own test only paged a parentProfileId-filtered
        /// set, so the status branch was never proven to survive Skip/Take or to be reflected
        /// in TotalCount (which is counted off a separate query from the page itself).
        /// </summary>
        [Fact]
        public async Task ListInvoices_ComposesTheStatusFilterWithPagingAndTheParentFilter()
        {
            var (mine, _) = await SeedInvoiceOwnerAsync();
            var (other, _) = await SeedInvoiceOwnerAsync();
            var billing = CreateBillingService();

            // 5 invoices for the parent under test, plus 2 for an unrelated parent that must
            // never leak into a parent-filtered page or its TotalCount.
            var minesIds = new List<Guid>();
            for (var i = 0; i < 5; i++)
            {
                var invoice = await billing.CreateInvoiceAsync(new CreateInvoiceRequest
                {
                    ParentProfileId = mine.Id,
                    DepartmentId = WellKnownDepartments.Phonics,
                    Amount = 100 + i,
                    DueDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(7)),
                });
                minesIds.Add(invoice.Id);
            }

            for (var i = 0; i < 2; i++)
            {
                await billing.CreateInvoiceAsync(new CreateInvoiceRequest
                {
                    ParentProfileId = other.Id,
                    DepartmentId = WellKnownDepartments.Phonics,
                    Amount = 900,
                    DueDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(7)),
                });
            }

            // Settle two of this parent's invoices so the two statuses are genuinely mixed.
            await billing.RecordPaymentAsync(minesIds[0], new RecordPaymentRequest
            {
                Amount = 100,
                Method = PaymentMethod.Cash,
            });
            await billing.RecordPaymentAsync(minesIds[1], new RecordPaymentRequest
            {
                Amount = 101,
                Method = PaymentMethod.Cash,
            });

            // Status filter alone: TotalCount counts the filtered set, not the whole table.
            var paid = await billing.ListInvoicesAsync(InvoiceStatus.Paid, null, page: 1, pageSize: 50);
            Assert.Equal(2, paid.TotalCount);
            Assert.All(paid.Items, i => Assert.Equal(InvoiceStatus.Paid, i.Status));

            // Status + parent together, paged: 3 Pending rows for this parent over 2-row pages.
            var firstPage = await billing.ListInvoicesAsync(InvoiceStatus.Pending, mine.Id, page: 1, pageSize: 2);
            Assert.Equal(3, firstPage.TotalCount);
            Assert.Equal(2, firstPage.Items.Count);
            Assert.All(firstPage.Items, i => Assert.Equal(InvoiceStatus.Pending, i.Status));
            Assert.All(firstPage.Items, i => Assert.Equal(mine.Id, i.ParentProfileId));

            var secondPage = await billing.ListInvoicesAsync(InvoiceStatus.Pending, mine.Id, page: 2, pageSize: 2);
            Assert.Single(secondPage.Items);
            Assert.Equal(3, secondPage.TotalCount);

            // The Id tiebreaker has to hold under the filtered query too, not just the
            // unfiltered one — every row appears exactly once across the two pages.
            var paged = firstPage.Items.Concat(secondPage.Items).Select(i => i.Id).ToList();
            Assert.Equal(3, paged.Distinct().Count());
            Assert.DoesNotContain(minesIds[0], paged); // the Paid ones stay filtered out
            Assert.DoesNotContain(minesIds[1], paged);

            // Past the last page is empty, but TotalCount still reports the filtered total.
            var beyond = await billing.ListInvoicesAsync(InvoiceStatus.Pending, mine.Id, page: 3, pageSize: 2);
            Assert.Empty(beyond.Items);
            Assert.Equal(3, beyond.TotalCount);
        }

        /// <summary>
        /// Regression for a68b1a1's clamping. page/pageSize arrive straight off the query
        /// string, so page=0 (a 0-indexed client) or a negative page would otherwise produce
        /// Skip(-n) — which EF rejects at translation time — and pageSize=0 an empty page.
        /// </summary>
        [Fact]
        public async Task ListInvoices_ClampsANonPositivePageOrPageSize()
        {
            var (parentProfile, _) = await SeedInvoiceOwnerAsync();
            var billing = CreateBillingService();
            for (var i = 0; i < 3; i++)
            {
                await billing.CreateInvoiceAsync(new CreateInvoiceRequest
                {
                    ParentProfileId = parentProfile.Id,
                    DepartmentId = WellKnownDepartments.Phonics,
                    Amount = 100 + i,
                    DueDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(7)),
                });
            }

            var pageZero = await billing.ListInvoicesAsync(null, parentProfile.Id, page: 0, pageSize: 2);
            Assert.Equal(1, pageZero.Page);
            Assert.Equal(2, pageZero.Items.Count);

            var negativePage = await billing.ListInvoicesAsync(null, parentProfile.Id, page: -5, pageSize: 2);
            Assert.Equal(1, negativePage.Page);
            Assert.Equal(pageZero.Items.Select(i => i.Id), negativePage.Items.Select(i => i.Id));

            // pageSize floors at 1 rather than returning an empty page for a valid request.
            var zeroSize = await billing.ListInvoicesAsync(null, parentProfile.Id, page: 1, pageSize: 0);
            Assert.Equal(1, zeroSize.PageSize);
            Assert.Single(zeroSize.Items);
            Assert.Equal(3, zeroSize.TotalCount);

            var negativeSize = await billing.ListInvoicesAsync(null, parentProfile.Id, page: 1, pageSize: -10);
            Assert.Equal(1, negativeSize.PageSize);
        }

        /// <summary>
        /// Regression for b35e798. AuditLogService.ListAsync gained a ThenBy(Id) tiebreaker;
        /// the commit proved pages don't overlap, but not that the entityName/action filters
        /// still compose with paging, nor that page/pageSize are clamped the same way.
        /// </summary>
        [Fact]
        public async Task AuditLog_ListAsync_ComposesFiltersWithPaging_AndClampsThePage()
        {
            var actor = await _db.SeedUserAsync($"al-f-{Guid.NewGuid():N}@test.com", "x", UserRole.Admin);
            var other = await _db.SeedUserAsync($"al-o-{Guid.NewGuid():N}@test.com", "x", UserRole.Admin);

            // One batched save, so every row ties on CreatedAtUtc — the exact condition the
            // Id tiebreaker exists for, now applied through a filter as well.
            _db.Context.AuditLogs.AddRange(
            [
                .. Enumerable.Range(0, 5).Select(_ => new AuditLog
                {
                    ActorUserId = actor.Id, Action = AuditAction.Update, EntityName = "FilterProbe",
                }),
                .. Enumerable.Range(0, 3).Select(_ => new AuditLog
                {
                    ActorUserId = actor.Id, Action = AuditAction.Delete, EntityName = "FilterProbe",
                }),
                .. Enumerable.Range(0, 4).Select(_ => new AuditLog
                {
                    ActorUserId = other.Id, Action = AuditAction.Update, EntityName = "OtherEntity",
                }),
            ]);
            await _db.Context.SaveChangesAsync();

            // entityName filter, paged: 8 rows over 3-row pages, no overlap and none missing.
            var p1 = await _auditLog.ListAsync("FilterProbe", null, page: 1, pageSize: 3);
            var p2 = await _auditLog.ListAsync("FilterProbe", null, page: 2, pageSize: 3);
            var p3 = await _auditLog.ListAsync("FilterProbe", null, page: 3, pageSize: 3);
            Assert.Equal(8, p1.TotalCount);
            var walked = p1.Items.Concat(p2.Items).Concat(p3.Items).Select(e => e.Id).ToList();
            Assert.Equal(8, walked.Count);
            Assert.Equal(8, walked.Distinct().Count());

            // entityName + action together.
            var deletes = await _auditLog.ListAsync("FilterProbe", AuditAction.Delete, page: 1, pageSize: 50);
            Assert.Equal(3, deletes.TotalCount);
            Assert.All(deletes.Items, e => Assert.Equal(AuditAction.Delete, e.Action));

            // restrictToActorId (what a non-platform-view caller gets) composes too.
            var mineOnly = await _auditLog.ListAsync(null, null, page: 1, pageSize: 50, restrictToActorId: other.Id);
            Assert.All(mineOnly.Items, e => Assert.Equal(other.Id, e.ActorUserId));
            Assert.Equal(4, mineOnly.TotalCount);

            // Same clamping contract as ListInvoicesAsync.
            var clamped = await _auditLog.ListAsync("FilterProbe", null, page: 0, pageSize: 100_000);
            Assert.Equal(1, clamped.Page);
            Assert.Equal(200, clamped.PageSize);
        }

        /// <summary>
        /// BUG-004. SaveIntegrationRequest / SaveRoleRequest / SaveMenuItemRequest /
        /// UpdateSettingRequest carried no length attributes at all, while their entities'
        /// columns are varchar(N). On Postgres an over-long field passed model validation and
        /// blew up at SaveChanges as an unhandled DbUpdateException (a 500) rather than a 400.
        /// Asserted against the annotations directly: SQLite does not enforce varchar length,
        /// so the 500 itself is only reproducible on the real stack.
        /// </summary>
        [Theory]
        [InlineData(typeof(SaveIntegrationRequest), nameof(SaveIntegrationRequest.Key), 64)]
        [InlineData(typeof(SaveIntegrationRequest), nameof(SaveIntegrationRequest.Name), 100)]
        [InlineData(typeof(SaveIntegrationRequest), nameof(SaveIntegrationRequest.Description), 500)]
        [InlineData(typeof(SaveRoleRequest), nameof(SaveRoleRequest.Name), 64)]
        [InlineData(typeof(SaveRoleRequest), nameof(SaveRoleRequest.DisplayName), 100)]
        [InlineData(typeof(SaveRoleRequest), nameof(SaveRoleRequest.Description), 500)]
        [InlineData(typeof(SaveRoleRequest), nameof(SaveRoleRequest.DefaultRoute), 200)]
        [InlineData(typeof(SaveMenuItemRequest), nameof(SaveMenuItemRequest.Portal), 32)]
        [InlineData(typeof(SaveMenuItemRequest), nameof(SaveMenuItemRequest.Section), 64)]
        [InlineData(typeof(SaveMenuItemRequest), nameof(SaveMenuItemRequest.Label), 100)]
        [InlineData(typeof(SaveMenuItemRequest), nameof(SaveMenuItemRequest.Path), 200)]
        [InlineData(typeof(SaveMenuItemRequest), nameof(SaveMenuItemRequest.Icon), 64)]
        [InlineData(typeof(UpdateSettingRequest), nameof(UpdateSettingRequest.Key), 100)]
        [InlineData(typeof(UpdateSettingRequest), nameof(UpdateSettingRequest.Value), 2000)]
        public void AdminSaveRequests_BoundEveryStringFieldToItsColumnLength(
            Type requestType, string propertyName, int expectedMaxLength)
        {
            var property = requestType.GetProperty(propertyName)!;
            var maxLength = property
                .GetCustomAttributes(typeof(System.ComponentModel.DataAnnotations.MaxLengthAttribute), false)
                .Cast<System.ComponentModel.DataAnnotations.MaxLengthAttribute>()
                .SingleOrDefault();

            Assert.True(maxLength is not null, $"{requestType.Name}.{propertyName} has no [MaxLength].");
            Assert.Equal(expectedMaxLength, maxLength!.Length);
            Assert.False(maxLength.IsValid(new string('x', expectedMaxLength + 1)));
            Assert.True(maxLength.IsValid(new string('x', expectedMaxLength)));
        }

        [Fact]
        public async Task ResetPin_ReturnsAWorkingPin_WithoutSendingAnything()
        {
            var user = await _db.SeedUserAsync($"resetpin-{Guid.NewGuid():N}@test.com", "old-pin", UserRole.Parent);
            var originalHash = user.PinHash;

            var temporaryPin = await CreateUserService().ResetPinAsync(user.Id);

            Assert.False(string.IsNullOrWhiteSpace(temporaryPin));
            var (verifyContext, _) = _db.CreateConcurrentSession();
            var stored = await verifyContext.Users.FirstAsync(u => u.Id == user.Id);
            Assert.NotEqual(originalHash, stored.PinHash); // a real new PIN was set, not a no-op
            Assert.True(_hasher.Verify(temporaryPin, stored.PinHash)); // and it's the one actually returned
            Assert.False(_hasher.Verify("old-pin", stored.PinHash)); // the old PIN no longer works
            verifyContext.Dispose();
        }

        /// <summary>
        /// BUG (authorization audit, 2026-08-22): every account — Admin included — logs in
        /// with email+PIN alone, and ResetPinAsync hands the new PIN straight back in the
        /// response. Without this guard, anyone holding UserManagement:Edit (a routine,
        /// mid-tier grant) could reset the Admin account's PIN and read it off the screen —
        /// a full account takeover. ChangeRoleAsync already refused to touch Admin accounts;
        /// this closes the same hole for the reset/status/delete actions.
        /// </summary>
        [Fact]
        public async Task ResetPin_RefusesAnAdminTarget()
        {
            var admin = await _db.SeedUserAsync($"admin-{Guid.NewGuid():N}@test.com", "old-pin", UserRole.Admin);
            var originalHash = admin.PinHash;

            await Assert.ThrowsAsync<DomainValidationException>(() => CreateUserService().ResetPinAsync(admin.Id));

            var stored = await _db.Context.Users.FirstAsync(u => u.Id == admin.Id);
            Assert.Equal(originalHash, stored.PinHash); // untouched
        }

        [Fact]
        public async Task SetStatus_RefusesAnAdminTarget()
        {
            var admin = await _db.SeedUserAsync($"admin-{Guid.NewGuid():N}@test.com", "x", UserRole.Admin, status: UserStatus.Active);

            await Assert.ThrowsAsync<DomainValidationException>(
                () => CreateUserService().SetStatusAsync(admin.Id, UserStatus.Suspended));

            var stored = await _db.Context.Users.FirstAsync(u => u.Id == admin.Id);
            Assert.Equal(UserStatus.Active, stored.Status); // untouched
        }

        /// <summary>
        /// BUG (authorization audit, 2026-08-22): SetPermissionsAsync let any caller holding
        /// UserManagement:Edit hand a Sub Admin colleague a bigger permission matrix than the
        /// caller holds themselves — e.g. a Sub Admin with only UserManagement:Edit granting
        /// BillingFinance:Approve or Settings:Edit to a colleague, escalating privilege by
        /// proxy. A genuine Admin caller is unaffected (bypasses the SubAdminPermission table
        /// entirely per PermissionAuthorizationHandler) and can still grant anything.
        /// </summary>
        [Fact]
        public async Task SetPermissions_RefusesGrantingAModuleTheCallerDoesNotHoldThemselves()
        {
            var caller = await _db.SeedUserAsync($"caller-{Guid.NewGuid():N}@test.com", "x", UserRole.SubAdmin);
            var target = await _db.SeedUserAsync($"target-{Guid.NewGuid():N}@test.com", "x", UserRole.SubAdmin);
            _db.Context.SubAdminPermissions.Add(new SubAdminPermission
            {
                UserId = caller.Id,
                Module = PermissionModule.UserManagement,
                CanView = true,
                CanEdit = true,
            });
            await _db.Context.SaveChangesAsync();
            _db.Context.ChangeTracker.Clear();

            var service = CreateUserService();
            await Assert.ThrowsAsync<ForbiddenException>(() => service.SetPermissionsAsync(
                target.Id, caller.Id,
                [new PermissionDto { Module = PermissionModule.BillingFinance, CanApprove = true }]));

            // Nothing was granted — the target's permission set is untouched.
            Assert.Empty(await _db.Context.SubAdminPermissions.Where(p => p.UserId == target.Id).ToListAsync());

            // Granting exactly what the caller already holds (or less) still works.
            await service.SetPermissionsAsync(
                target.Id, caller.Id,
                [new PermissionDto { Module = PermissionModule.UserManagement, CanView = true }]);
            Assert.True(await _db.Context.SubAdminPermissions.AnyAsync(
                p => p.UserId == target.Id && p.Module == PermissionModule.UserManagement && p.CanView));
            // Production hands each request its own DbContext; this test's shared one still
            // tracks the row SetPermissionsAsync just added above, so the next call's own
            // Query()/Remove() on that same row needs a clean slate (mirrors the established
            // pattern for repeated SetPermissionsAsync calls against one context elsewhere).
            _db.Context.ChangeTracker.Clear();

            // An Admin caller is unrestricted by their own (nonexistent) SubAdminPermission rows.
            var admin = await _db.SeedUserAsync($"admin-{Guid.NewGuid():N}@test.com", "x", UserRole.Admin);
            await service.SetPermissionsAsync(
                target.Id, admin.Id,
                [new PermissionDto { Module = PermissionModule.BillingFinance, CanApprove = true }]);
            Assert.True(await _db.Context.SubAdminPermissions.AnyAsync(
                p => p.UserId == target.Id && p.Module == PermissionModule.BillingFinance && p.CanApprove));
        }

        /// <summary>
        /// CourseService.UpdateAsync's two structural guards, neither previously covered:
        /// a course backing a multi-student batch can't become Individual, and TotalSessions /
        /// DurationMinutes can't move once a schedule has been generated from them.
        /// </summary>
        [Fact]
        public async Task UpdateCourse_RefusesToDesyncAnAlreadyGeneratedScheduleOrAMultiStudentBatch()
        {
            var (batch, course, _) = await SeedBatchWithSessionAsync(totalSessions: 4);
            var courses = CreateCourseService();

            Task<CourseDto> Save(CourseType type, int totalSessions, int durationMinutes) =>
                courses.UpdateAsync(course.Id, new SaveCourseRequest
                {
                    CourseCategoryId = course.CourseCategoryId,
                    Name = course.Name,
                    Type = type,
                    DurationMinutes = durationMinutes,
                    Price = course.Price,
                    TotalSessions = totalSessions,
                    DepartmentId = course.DepartmentId,
                    IsActive = true,
                });

            // A session already exists for this batch, so the schedule is generated: neither
            // TotalSessions nor DurationMinutes may move.
            await Assert.ThrowsAsync<DomainValidationException>(() => Save(CourseType.Group, 6, 45));
            await Assert.ThrowsAsync<DomainValidationException>(() => Save(CourseType.Group, 4, 60));

            // Leaving both alone is fine — the guard is about desync, not about editing at all.
            var renamed = await Save(CourseType.Group, 4, 45);
            Assert.Equal(CourseType.Group, renamed.Type);

            // Two active students in the batch blocks the switch to Individual (which would
            // otherwise bypass BatchService's own one-student-per-Individual-batch rule).
            var parentProfile = new ParentProfile
            {
                UserId = (await _db.SeedUserAsync($"cp-{Guid.NewGuid():N}@test.com", "x", UserRole.Parent)).Id,
            };
            _db.Context.Add(parentProfile);
            var children = Enumerable.Range(0, 2)
                .Select(i => new Child { ParentProfile = parentProfile, FirstName = $"Kid{i}", LastName = "X" })
                .ToList();
            _db.Context.AddRange(children);
            _db.Context.AddRange(children.Select(c => new BatchEnrollment
            {
                BatchId = batch.Id,
                Child = c,
                Status = EnrollmentStatus.Active,
            }));
            await _db.Context.SaveChangesAsync();

            await Assert.ThrowsAsync<DomainValidationException>(() => Save(CourseType.Individual, 4, 45));

            var (context, _) = _db.CreateConcurrentSession();
            using (context)
            {
                var stored = await context.Courses.FirstAsync(c => c.Id == course.Id);
                Assert.Equal(CourseType.Group, stored.Type); // nothing partially applied
                Assert.Equal(4, stored.TotalSessions);
                Assert.Equal(45, stored.DurationMinutes);
            }
        }

        /// <summary>
        /// BatchService.SetStatusAsync's side effects, previously uncovered: taking a batch
        /// out of service must cancel its still-scheduled future sessions and expire the
        /// subscriptions that were paying for the course it just stopped running.
        /// </summary>
        [Fact]
        public async Task SetBatchDormant_CancelsFutureSessions_AndExpiresTheSubscriptionsPayingForIt()
        {
            var (batch, course, futureSession) = await SeedBatchWithSessionAsync(totalSessions: 4);

            var parentUser = await _db.SeedUserAsync($"bs-{Guid.NewGuid():N}@test.com", "x", UserRole.Parent);
            var parentProfile = new ParentProfile { UserId = parentUser.Id };
            var child = new Child { ParentProfile = parentProfile, FirstName = "Kid", LastName = "X" };
            var plan = new PackagePlan
            {
                Name = "Monthly",
                Course = course,
                BillingType = BillingType.Subscription,
                BillingCycle = BillingCycle.Monthly,
                Price = 1000,
            };
            var subscription = new Subscription
            {
                ParentProfile = parentProfile,
                Child = child,
                PackagePlan = plan,
                StartDate = DateOnly.FromDateTime(DateTime.UtcNow),
                Status = SubscriptionStatus.Active,
                NextBillingAtUtc = DateTime.UtcNow.AddDays(30),
            };
            // A session already in the past must be left alone — only future ones are dangling.
            var pastSession = new ClassSession
            {
                BatchId = batch.Id,
                TeacherProfileId = batch.TeacherProfileId,
                ScheduledStartAtUtc = DateTime.UtcNow.AddDays(-3),
                ScheduledEndAtUtc = DateTime.UtcNow.AddDays(-3).AddMinutes(45),
                Status = SessionStatus.Scheduled,
            };
            _db.Context.AddRange(parentProfile, child, plan, subscription, pastSession);
            _db.Context.Add(new BatchEnrollment { BatchId = batch.Id, Child = child, Status = EnrollmentStatus.Active });
            await _db.Context.SaveChangesAsync();

            await CreateBatchService().SetStatusAsync(batch.Id, BatchStatus.Dormant);

            var (context, _) = _db.CreateConcurrentSession();
            using (context)
            {
                var stored = await context.Batches.FirstAsync(b => b.Id == batch.Id);
                Assert.Equal(BatchStatus.Dormant, stored.Status);
                Assert.NotNull(stored.CompletedAtUtc);

                var future = await context.ClassSessions.FirstAsync(s => s.Id == futureSession.Id);
                Assert.Equal(SessionStatus.Cancelled, future.Status);
                Assert.Contains("Dormant", future.CancellationReason!);

                var past = await context.ClassSessions.FirstAsync(s => s.Id == pastSession.Id);
                Assert.Equal(SessionStatus.Scheduled, past.Status); // history untouched

                var storedSubscription = await context.Subscriptions.FirstAsync(s => s.Id == subscription.Id);
                Assert.Equal(SubscriptionStatus.Expired, storedSubscription.Status);
                Assert.Null(storedSubscription.NextBillingAtUtc); // and the billing job won't re-invoice
            }
        }

        /// <summary>
        /// IntegrationService's secret handling, previously untested and security-relevant:
        /// gateway credentials must never round-trip to the client in the clear, and an admin
        /// saving the form back unchanged must not overwrite the real secret with its mask.
        /// </summary>
        [Fact]
        public async Task Integration_MasksSecretsOnRead_AndPreservesThemWhenTheMaskIsSavedBack()
        {
            var integrations = new IntegrationService(_db.UnitOfWork, _auditLog, new FakePaymentGateway());

            var created = await integrations.CreateAsync(new SaveIntegrationRequest
            {
                Key = "Razorpay",
                Name = "Razorpay",
                Category = IntegrationCategory.PaymentGateway,
                IsEnabled = true,
                Config = new Dictionary<string, string?>
                {
                    ["apiKey"] = "rzp_live_supersecret1234",
                    ["apiSecret"] = "shhh_abcd",
                    ["webhookUrl"] = "https://hooks.test/rzp",
                },
            });

            Assert.Equal("razorpay", created.Key); // normalized to lower-case
            // All but the last 4 characters bulleted — "rzp_live_supersecret1234" is 24 long.
            Assert.Equal(new string('•', 20) + "1234", created.Config["apiKey"]);
            Assert.Equal("•••••abcd", created.Config["apiSecret"]);
            Assert.Equal("https://hooks.test/rzp", created.Config["webhookUrl"]); // not a secret field

            // The admin edits only the webhook and posts the form back — the secret fields
            // still hold the masks they were rendered with.
            var updated = await integrations.UpdateAsync(created.Id, new SaveIntegrationRequest
            {
                Key = "razorpay",
                Name = "Razorpay",
                Category = IntegrationCategory.PaymentGateway,
                IsEnabled = true,
                Config = new Dictionary<string, string?>
                {
                    ["apiKey"] = created.Config["apiKey"],
                    ["apiSecret"] = created.Config["apiSecret"],
                    ["webhookUrl"] = "https://hooks.test/rzp-v2",
                },
            });

            Assert.Equal("https://hooks.test/rzp-v2", updated.Config["webhookUrl"]);

            // The stored ciphertext must still be the original, not the bullet string.
            var (context, _) = _db.CreateConcurrentSession();
            using (context)
            {
                var stored = await context.Integrations.FirstAsync(i => i.Id == created.Id);
                Assert.Contains("rzp_live_supersecret1234", stored.ConfigJson!);
                Assert.Contains("shhh_abcd", stored.ConfigJson!);
                Assert.DoesNotContain("•", stored.ConfigJson!);
            }

            // A genuinely new secret still replaces the old one.
            var rotated = await integrations.UpdateAsync(created.Id, new SaveIntegrationRequest
            {
                Key = "razorpay",
                Name = "Razorpay",
                Category = IntegrationCategory.PaymentGateway,
                IsEnabled = true,
                Config = new Dictionary<string, string?> { ["apiSecret"] = "rotated_wxyz" },
            });
            Assert.Equal("••••••••wxyz", rotated.Config["apiSecret"]);
        }

        /// <summary>
        /// The Configure dialog already warns client-side when a Razorpay Key Id doesn't look
        /// right (most likely the Key Secret pasted into the wrong field) — but nothing
        /// stopped the bad value from actually being saved. Confirmed live: production has an
        /// integration saved with exactly this mixup.
        /// </summary>
        [Fact]
        public async Task Integration_RejectsRazorpayKeyId_ThatDoesNotStartWithRzp()
        {
            var integrations = new IntegrationService(_db.UnitOfWork, _auditLog, new FakePaymentGateway());

            await Assert.ThrowsAsync<DomainValidationException>(() => integrations.CreateAsync(new SaveIntegrationRequest
            {
                Key = "razorpay",
                Name = "Razorpay",
                Category = IntegrationCategory.PaymentGateway,
                IsEnabled = true,
                Config = new Dictionary<string, string?>
                {
                    // Looks like a Key Secret was pasted into the Key Id field.
                    ["apiKey"] = "shhh_this_is_actually_the_secret",
                    ["apiSecret"] = "rzp_live_realkeyid",
                },
            }));

            // A valid keyId still saves fine — this isn't rejecting Razorpay integrations
            // outright, only a value that can't be a real Key Id.
            var created = await integrations.CreateAsync(new SaveIntegrationRequest
            {
                Key = "razorpay",
                Name = "Razorpay",
                Category = IntegrationCategory.PaymentGateway,
                IsEnabled = true,
                Config = new Dictionary<string, string?> { ["apiKey"] = "rzp_live_valid1234" },
            });
            Assert.NotEqual(Guid.Empty, created.Id);

            // An update that touches an unrelated field (the keyId's mask round-trips
            // untouched) must not be rejected as if it were a fresh invalid value.
            var updated = await integrations.UpdateAsync(created.Id, new SaveIntegrationRequest
            {
                Key = "razorpay",
                Name = "Razorpay",
                Category = IntegrationCategory.PaymentGateway,
                IsEnabled = false,
                Config = new Dictionary<string, string?> { ["apiKey"] = created.Config["apiKey"] },
            });
            Assert.False(updated.IsEnabled);
        }

        /// <summary>
        /// Caught live: Razorpay switched on with no API keys yet still showed up as a real
        /// option in the Pay Now popup, because GetEnabledPaymentMethodsAsync only checked
        /// IsEnabled -- a parent picking it got a silently-simulated fake link instead of an
        /// actual checkout. "Cash" has no adapter/config at all and must always be offered.
        /// </summary>
        [Fact]
        public async Task GetEnabledPaymentMethods_ExcludesEnabledButUnconfiguredGateways()
        {
            var gateway = new FakePaymentGateway { UnconfiguredKeys = ["razorpay"] };
            var integrations = new IntegrationService(_db.UnitOfWork, _auditLog, gateway);

            await integrations.CreateAsync(new SaveIntegrationRequest
            {
                Key = "razorpay",
                Name = "Razorpay",
                Category = IntegrationCategory.PaymentGateway,
                IsEnabled = true,
                Config = new Dictionary<string, string?>(),
            });
            await integrations.CreateAsync(new SaveIntegrationRequest
            {
                Key = "cash",
                Name = "Cash",
                Category = IntegrationCategory.PaymentGateway,
                IsEnabled = true,
                Config = new Dictionary<string, string?>(),
            });

            var methods = await integrations.GetEnabledPaymentMethodsAsync();

            Assert.DoesNotContain(methods, m => m.Key == "razorpay");
            Assert.Contains(methods, m => m.Key == "cash");
        }

        /// <summary>
        /// RoleService's system-role and in-use protections, previously untested. A seeded role
        /// backs the Sub Admin preset flow, so renaming or deleting one would strand every user
        /// assigned to it.
        /// </summary>
        [Fact]
        public async Task RoleService_ProtectsSystemRoles_AndRefusesToDeleteOneStillAssigned()
        {
            var roles = new RoleService(_db.UnitOfWork, _auditLog);

            var systemRole = new RoleDefinition
            {
                Name = "academic-coordinator",
                DisplayName = "Academic Coordinator",
                DefaultRoute = "/coordinator",
                IsSystem = true,
            };
            _db.Context.Add(systemRole);
            await _db.Context.SaveChangesAsync();
            // RoleService loads no-tracking and calls Update(); production hands each request
            // its own DbContext, so the seed instance must not stay tracked here.
            _db.Context.ChangeTracker.Clear();

            SaveRoleRequest Request(string name, string? route = "/coordinator") => new()
            {
                Name = name,
                DisplayName = "Academic Coordinator",
                DefaultRoute = route,
                Permissions = [new PermissionDto { Module = PermissionModule.Admission, CanView = true }],
            };

            // Each call stands for its own HTTP request, so each gets a clean tracker.
            Task<RoleDto> Update(SaveRoleRequest request)
            {
                _db.Context.ChangeTracker.Clear();
                return roles.UpdateAsync(systemRole.Id, request);
            }

            await Assert.ThrowsAsync<DomainValidationException>(() => Update(Request("renamed-coordinator")));

            _db.Context.ChangeTracker.Clear();
            await Assert.ThrowsAsync<DomainValidationException>(() => roles.DeleteAsync(systemRole.Id));

            // A system role's permission matrix is still editable — that's the whole point of
            // the preset editor; only its identity is frozen.
            var edited = await Update(Request("academic-coordinator"));
            Assert.Single(edited.Permissions);

            // A route not starting with '/' is rejected, and a duplicated module is too.
            await Assert.ThrowsAsync<DomainValidationException>(
                () => Update(Request("academic-coordinator", "coordinator")));
            await Assert.ThrowsAsync<DomainValidationException>(() => Update(new SaveRoleRequest
            {
                Name = "academic-coordinator",
                DisplayName = "Academic Coordinator",
                Permissions =
                [
                    new PermissionDto { Module = PermissionModule.Admission, CanView = true },
                    new PermissionDto { Module = PermissionModule.Admission, CanEdit = true },
                ],
            }));
            _db.Context.ChangeTracker.Clear();

            // A custom role in use by a Sub Admin can't be deleted out from under them.
            var custom = await roles.CreateAsync(new SaveRoleRequest
            {
                Name = "Front-Desk",
                DisplayName = "Front Desk",
                Permissions = [],
            });
            Assert.Equal("front-desk", custom.Name);
            await Assert.ThrowsAsync<ConflictException>(() => roles.CreateAsync(new SaveRoleRequest
            {
                Name = "FRONT-DESK",
                DisplayName = "Front Desk Again",
            }));

            var subAdmin = await _db.SeedUserAsync($"rs-{Guid.NewGuid():N}@test.com", "x", UserRole.SubAdmin);
            subAdmin.RoleDefinitionId = custom.Id;
            await _db.Context.SaveChangesAsync();

            await Assert.ThrowsAsync<ConflictException>(() => roles.DeleteAsync(custom.Id));
        }

        [Fact]
        public async Task UpdateRole_CannotSaveAwayARequiredSystemGrant()
        {
            // Reproduces the live bug: saving the "management" preset from the Roles &
            // Permissions screen without the Courses box checked used to wipe
            // CourseBatchManagement:View outright, breaking that role's own Revenue & Courses
            // screen until the process next restarted (the startup-only backfill was the only
            // thing that ever put it back). RoleService.UpdateAsync must not be able to save
            // that grant away, independent of what the submitted matrix says.
            var roles = new RoleService(_db.UnitOfWork, _auditLog);
            var managementRole = new RoleDefinition
            {
                Name = "management",
                DisplayName = "Management",
                DefaultRoute = "/management",
                IsSystem = true,
            };
            _db.Context.Add(managementRole);
            await _db.Context.SaveChangesAsync();
            _db.Context.ChangeTracker.Clear();

            // Admin edits the role for an unrelated reason (adding Reports access) and submits
            // the matrix as the screen actually would — Courses simply isn't in it.
            var updated = await roles.UpdateAsync(managementRole.Id, new SaveRoleRequest
            {
                Name = "management",
                DisplayName = "Management",
                DefaultRoute = "/management",
                Permissions = [new PermissionDto { Module = PermissionModule.ReportsAnalytics, CanView = true }],
            });

            var coursesGrant = Assert.Single(updated.Permissions, p => p.Module == PermissionModule.CourseBatchManagement);
            Assert.True(coursesGrant.CanView);
            Assert.Contains(updated.Permissions, p => p.Module == PermissionModule.ReportsAnalytics && p.CanView);
        }

        /// <summary>
        /// EmailTemplateService's substitution rules, previously exercised only indirectly.
        /// Token values are parent-supplied (child/parent names), so they must be HTML-escaped
        /// in the body and stripped of CR/LF in the subject (mail-header injection).
        /// </summary>
        [Fact]
        public async Task EmailTemplate_EscapesTokenValuesInHtml_AndStripsLineBreaksFromTheSubject()
        {
            var template = new EmailTemplate
            {
                Key = "qa-substitution",
                Name = "QA",
                Category = NotificationType.General,
                Subject = "Welcome {{Name}}",
                HtmlBody = "<p>Hello {{Name}}, see {{Missing}} and {{Note}}</p>",
                PlaceholdersJson = "[\"Name\",\"Note\"]",
                IsActive = true,
            };
            _db.Context.EmailTemplates.Add(template);
            await _db.Context.SaveChangesAsync();

            var (subject, body) = await _emailTemplates.RenderAsync("qa-substitution", new Dictionary<string, string>
            {
                // A name carrying markup, a header-injection attempt, and a value that itself
                // looks like another token (which a naive replace-per-token loop would re-expand).
                ["Name"] = "<script>alert(1)</script>\r\nBcc: attacker@evil.test",
                ["Note"] = "{{Name}}",
            });

            Assert.DoesNotContain("\r", subject);
            Assert.DoesNotContain("\n", subject);
            Assert.Contains("Bcc: attacker@evil.test", subject); // flattened onto one line, not a new header

            Assert.DoesNotContain("<script>", body);
            Assert.Contains("&lt;script&gt;", body);
            // {{Note}}'s value is literally "{{Name}}" — it must survive as text, never be
            // re-substituted with Name's value.
            Assert.Contains("{{Name}}", body);
            Assert.Contains("{{Missing}}", body); // an unsupplied token is left as-is, not blanked

            // An edit invalidates the render cache immediately rather than waiting out the TTL.
            await _emailTemplates.UpdateAsync(template.Id, new SaveEmailTemplateRequest
            {
                Subject = "Updated {{Name}}",
                HtmlBody = "<p>v2 {{Name}}</p>",
                IsActive = true,
            });
            var (afterEdit, afterBody) = await _emailTemplates.RenderAsync(
                "qa-substitution", new Dictionary<string, string> { ["Name"] = "Ann" });
            Assert.Equal("Updated Ann", afterEdit);
            Assert.Contains("v2 Ann", afterBody);

            // Deactivating falls back to the generic message rather than blocking the send.
            await _emailTemplates.UpdateAsync(template.Id, new SaveEmailTemplateRequest
            {
                Subject = "Updated {{Name}}",
                HtmlBody = "<p>v2 {{Name}}</p>",
                IsActive = false,
            });
            var (fallback, _) = await _emailTemplates.RenderAsync(
                "qa-substitution", new Dictionary<string, string> { ["Name"] = "Ann" });
            Assert.Equal("Notification from Meet to Manage", fallback);
        }

        /// <summary>
        /// {{OrgName}} is available to every template without its caller having to pass one —
        /// resolved centrally from the "brand.name" setting (see EmailTemplateSeedData.Wrap's
        /// header/footer, and ReconcileOrgNameEmailTemplatesAsync for already-seeded DBs). A
        /// deployment that renames its brand should see that reflected in outgoing email
        /// without anyone having to touch a single template's own content.
        /// </summary>
        [Fact]
        public async Task EmailTemplate_SubstitutesOrgNameFromBrandSetting_WithoutCallerSupplyingIt()
        {
            _db.Context.EmailTemplates.Add(new EmailTemplate
            {
                Key = "qa-orgname",
                Name = "QA OrgName",
                Category = NotificationType.General,
                Subject = "Hello from {{OrgName}}",
                HtmlBody = "<p>Welcome to {{OrgName}}</p>",
                PlaceholdersJson = "[]",
                IsActive = true,
            });
            _db.Context.AppSettings.Add(new AppSetting
            {
                Category = SettingCategory.Branding,
                Key = "brand.name",
                Value = "Acme Academy",
            });
            await _db.Context.SaveChangesAsync();

            var (subject, body) = await _emailTemplates.RenderAsync("qa-orgname", new Dictionary<string, string>());

            Assert.Equal("Hello from Acme Academy", subject);
            Assert.Contains("Welcome to Acme Academy", body);
        }

        /// <summary>
        /// Bulk email recipient scoping, previously untested. The count shown on the compose
        /// screen has to be exactly who the send reaches, and a batch-scoped send must not
        /// spill into unrelated parents.
        /// </summary>
        [Fact]
        public async Task BulkEmail_PreviewCountMatchesTheSend_AndABatchScopedSendStaysInsideTheBatch()
        {
            var (batch, _, _) = await SeedBatchWithSessionAsync(totalSessions: 2);
            var reports = new ReportsService(_db.UnitOfWork, _notifications);
            var sender = await _db.SeedUserAsync($"sender-{Guid.NewGuid():N}@test.com", "x", UserRole.Admin);

            async Task<ParentProfile> SeedParentWithChildAsync(bool enrol, UserStatus status = UserStatus.Active)
            {
                var user = await _db.SeedUserAsync($"be-{Guid.NewGuid():N}@test.com", "x", UserRole.Parent, status);
                var profile = new ParentProfile { UserId = user.Id };
                var child = new Child { ParentProfile = profile, FirstName = "Kid", LastName = "X" };
                _db.Context.AddRange(profile, child);
                if (enrol)
                {
                    _db.Context.Add(new BatchEnrollment { BatchId = batch.Id, Child = child, Status = EnrollmentStatus.Active });
                }

                await _db.Context.SaveChangesAsync();
                return profile;
            }

            await SeedParentWithChildAsync(enrol: true);
            await SeedParentWithChildAsync(enrol: true);
            await SeedParentWithChildAsync(enrol: false);          // active, but not in this batch
            await SeedParentWithChildAsync(enrol: false, status: UserStatus.Inactive);

            var batchPreview = await reports.PreviewBulkEmailAsync(batch.Id);
            Assert.Equal(2, batchPreview.RecipientCount);

            _emailSender.Sent.Clear();
            var batchSend = await reports.SendBulkEmailAsync(sender.Id, new BulkEmailRequest
            {
                Subject = "Class update",
                Body = "<p>See you Monday.</p>",
                BatchId = batch.Id,
            });
            Assert.Equal(2, batchSend.RecipientCount);
            Assert.Equal(2, _emailSender.Sent.Count); // the preview count is the real reach

            // Unscoped goes to every ACTIVE parent — the inactive one is excluded, and so is
            // any parent whose account was deactivated after enrolling.
            var allPreview = await reports.PreviewBulkEmailAsync(null);
            Assert.Equal(3, allPreview.RecipientCount);

            _emailSender.Sent.Clear();
            var allSend = await reports.SendBulkEmailAsync(sender.Id, new BulkEmailRequest
            {
                Subject = "Newsletter",
                Body = "<p>Hello</p>",
            });
            Assert.Equal(3, allSend.RecipientCount);
            Assert.Equal(3, _emailSender.Sent.Count);

            // Every send is journalled, so a failed delivery is auditable rather than silent.
            var (context, _) = _db.CreateConcurrentSession();
            using (context)
            {
                Assert.Equal(5, await context.Notifications.CountAsync(n => n.Channel == NotificationChannel.Email
                    && (n.Subject == "Class update" || n.Subject == "Newsletter")));
            }
        }

        [Fact]
        public async Task TeacherPerformance_IncludesLatestPayoutSummary_StatusAndTotalOnly()
        {
            // Management's Teacher Snapshot report reads this (ReportsAnalytics:View, which is
            // all the Management preset actually grants -- GET /api/payouts itself stays
            // Admin-only). A still-Pending payout (accruing this month, not yet finalized) must
            // show here too -- the whole point is visibility into what's owed even before it's
            // locked/paid, not just a finalized/paid history.
            var (_, _, session) = await SeedBatchWithSessionAsync(totalSessions: 1);
            await CreatePayoutService().SetRateAsync(new SavePayoutRateRequest
            {
                TeacherProfileId = session.TeacherProfileId, RatePerMinute = 10,
                EffectiveFrom = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1)),
            });
            await SeedFullTeacherAttendanceAsync(session);
            await CreateSessionService().CompleteAsync(session.Id);

            var payout = await _db.Context.Payouts.AsNoTracking().SingleAsync(p => p.TeacherProfileId == session.TeacherProfileId);
            Assert.Equal(PayoutStatus.Pending, payout.Status); // sanity: not finalized/paid yet

            var reports = new ReportsService(_db.UnitOfWork, _notifications);
            var row = (await reports.GetTeacherPerformanceAsync())
                .Single(t => t.TeacherProfileId == session.TeacherProfileId);

            Assert.Equal(payout.PeriodYear, row.LatestPayoutPeriodYear);
            Assert.Equal(payout.PeriodMonth, row.LatestPayoutPeriodMonth);
            Assert.Equal(PayoutStatus.Pending, row.LatestPayoutStatus);
            Assert.Equal(payout.TotalAmount, row.LatestPayoutAmount);
        }

        [Fact]
        public async Task TeacherPerformance_LeavesLatestPayoutNull_ForATeacherWithNoPayoutOnRecord()
        {
            var (batch, _, _) = await SeedBatchWithSessionAsync(totalSessions: 1, includeSession: false);
            var reports = new ReportsService(_db.UnitOfWork, _notifications);

            var row = (await reports.GetTeacherPerformanceAsync())
                .Single(t => t.TeacherProfileId == batch.TeacherProfileId);

            Assert.Null(row.LatestPayoutPeriodYear);
            Assert.Null(row.LatestPayoutStatus);
            Assert.Null(row.LatestPayoutAmount);
        }

        private async Task<(ParentProfile Parent, User User)> SeedInvoiceOwnerAsync()
        {
            var parentUser = await _db.SeedUserAsync($"inv-{Guid.NewGuid():N}@test.com", "x", UserRole.Parent);
            var parentProfile = new ParentProfile { UserId = parentUser.Id };
            _db.Context.Add(parentProfile);
            if (!await _db.Context.PaymentAccounts.AnyAsync(a => a.DepartmentId == WellKnownDepartments.Phonics))
            {
                _db.Context.Add(new PaymentAccount
                {
                    Name = "Phonics",
                    DepartmentId = WellKnownDepartments.Phonics,
                    GatewayProvider = "razorpay",
                    GatewayAccountRef = "ph",
                });
            }

            await _db.Context.SaveChangesAsync();
            return (parentProfile, parentUser);
        }

        private async Task<(ParentProfile Parent, Child Child, PackagePlan Plan)> SeedSubscriptionFixtureAsync()
        {
            var parentUser = await _db.SeedUserAsync($"sub-{Guid.NewGuid():N}@test.com", "x", UserRole.Parent);
            var parentProfile = new ParentProfile { UserId = parentUser.Id };
            var child = new Child { ParentProfile = parentProfile, FirstName = "Kid", LastName = "One" };
            var category = new CourseCategory { Name = $"Cat-{Guid.NewGuid():N}", DepartmentId = WellKnownDepartments.Phonics };
            var course = new Course
            {
                CourseCategory = category,
                Name = "Course",
                Type = CourseType.Group,
                DurationMinutes = 45,
                Price = 100,
                TotalSessions = 8,
                DepartmentId = WellKnownDepartments.Phonics,
            };
            var plan = new PackagePlan
            {
                Name = "Monthly",
                Course = course,
                BillingType = BillingType.Subscription,
                BillingCycle = BillingCycle.Monthly,
                Price = 1000,
            };
            var account = new PaymentAccount
            {
                Name = "Phonics",
                DepartmentId = WellKnownDepartments.Phonics,
                GatewayProvider = "razorpay",
                GatewayAccountRef = "ph",
            };
            _db.Context.AddRange(parentProfile, child, category, course, plan, account);
            await _db.Context.SaveChangesAsync();
            return (parentProfile, child, plan);
        }

        /// <summary>
        /// ResourceService.CreateAsync never checked CourseId/BatchId(s) existed before
        /// writing Resource/ResourceBatchVisibility rows referencing them - a stale dropdown
        /// value (or one deleted between page-load and submit) hit the FK constraint at
        /// SaveChanges as an unhandled DbUpdateException, surfacing to the uploader as a raw
        /// 500 with no indication of what was wrong. Reproduced directly against SQLite
        /// (real FK enforcement, same as Postgres) before being fixed.
        /// </summary>
        [Fact]
        public async Task UploadResource_RejectsANonExistentCourseId_WithACleanNotFound()
        {
            var resources = CreateResourceService();
            var ex = await Assert.ThrowsAsync<NotFoundException>(() => resources.CreateAsync(
                new CreateResourceRequest { Title = "R", Type = ResourceType.Worksheet, CourseId = Guid.NewGuid() },
                "some/stored/path.txt", "text/plain", 100));
            Assert.Contains("Course", ex.Message);
            Assert.Empty(await _db.Context.Resources.ToListAsync());
        }

        [Fact]
        public async Task UploadResource_RejectsANonExistentBatchId_WithACleanNotFound()
        {
            var resources = CreateResourceService();
            var ex = await Assert.ThrowsAsync<NotFoundException>(() => resources.CreateAsync(
                new CreateResourceRequest { Title = "R", Type = ResourceType.Worksheet, BatchId = Guid.NewGuid() },
                "some/stored/path.txt", "text/plain", 100));
            Assert.Contains("Batch", ex.Message);
            Assert.Empty(await _db.Context.Resources.ToListAsync());
        }

        [Fact]
        public async Task UploadResource_WithARealCourseAndBatch_StillSucceeds()
        {
            // The guard above must not reject a genuinely valid selection.
            var (batch, course, _) = await SeedBatchWithSessionAsync(totalSessions: 1, includeSession: false);
            var resources = CreateResourceService();
            var dto = await resources.CreateAsync(
                new CreateResourceRequest { Title = "Worksheet", Type = ResourceType.Worksheet, CourseId = course.Id, BatchId = batch.Id },
                "some/stored/path.txt", "text/plain", 100);
            Assert.Equal(course.Id, dto.CourseId);
            Assert.Equal(batch.Id, dto.BatchId);
        }

        /// <summary>
        /// Phase 1 of the menu/role redesign (see Documentation/Dynamic_Menu_RBAC_Redesign_Feasibility.md):
        /// GetForRoleAsync must report every active menu item, defaulting to all-false when no
        /// grant row exists yet, and SetForRoleAsync must round-trip whatever was saved while
        /// silently dropping all-false rows rather than storing them ("no row = no access").
        /// </summary>
        [Fact]
        public async Task MenuPermissionService_SetForRole_ThenGet_RoundTripsGrants_StoringAllFalseRowsToo()
        {
            var role = new RoleDefinition { Name = "front-desk", DisplayName = "Front Desk" };
            var menuA = new domain.Entities.Navigation.MenuItem
            {
                Portal = "admin", Label = "Users", Path = "/admin/users", Icon = "Users",
                SectionOrder = 0, SortOrder = 0, IsActive = true,
            };
            var menuB = new domain.Entities.Navigation.MenuItem
            {
                Portal = "admin", Label = "Billing", Path = "/admin/billing", Icon = "Receipt",
                SectionOrder = 0, SortOrder = 1, IsActive = true,
            };
            _db.Context.AddRange(role, menuA, menuB);
            await _db.Context.SaveChangesAsync();
            _db.Context.ChangeTracker.Clear();

            var service = CreateMenuPermissionService();

            var initial = await service.GetForRoleAsync(role.Id);
            Assert.All(initial, r => Assert.False(r.CanView || r.CanCreate || r.CanEdit || r.CanDelete));

            await service.SetForRoleAsync(role.Id,
            [
                new SaveMenuPermissionItem { MenuItemId = menuA.Id, CanView = true, CanEdit = true },
                new SaveMenuPermissionItem { MenuItemId = menuB.Id }, // all-false — an explicit "hide this", still stored as a row
            ]);

            var stored = await _db.Context.MenuPermissions.Where(p => p.RoleDefinitionId == role.Id).ToListAsync();
            Assert.Equal(2, stored.Count);

            var afterSet = await service.GetForRoleAsync(role.Id);
            var grantA = Assert.Single(afterSet, r => r.MenuItemId == menuA.Id);
            Assert.True(grantA.CanView);
            Assert.True(grantA.CanEdit);
            Assert.False(grantA.CanCreate);
            Assert.False(grantA.CanDelete);

            var grantB = Assert.Single(afterSet, r => r.MenuItemId == menuB.Id);
            Assert.False(grantB.CanView || grantB.CanCreate || grantB.CanEdit || grantB.CanDelete);
        }

        /// <summary>
        /// SetForRoleAsync is replace-all, mirroring RoleService.UpdateAsync: a menu item
        /// granted in one save that is simply absent from the next save (not explicitly
        /// unchecked, just omitted) must lose its grant rather than linger.
        /// </summary>
        [Fact]
        public async Task MenuPermissionService_SetForRole_ReplacesAll_DroppingItemsNotResubmitted()
        {
            var role = new RoleDefinition { Name = "front-desk", DisplayName = "Front Desk" };
            var menuA = new domain.Entities.Navigation.MenuItem
            {
                Portal = "admin", Label = "Users", Path = "/admin/users", Icon = "Users",
                SectionOrder = 0, SortOrder = 0, IsActive = true,
            };
            var menuB = new domain.Entities.Navigation.MenuItem
            {
                Portal = "admin", Label = "Billing", Path = "/admin/billing", Icon = "Receipt",
                SectionOrder = 0, SortOrder = 1, IsActive = true,
            };
            _db.Context.AddRange(role, menuA, menuB);
            await _db.Context.SaveChangesAsync();
            _db.Context.ChangeTracker.Clear();

            var service = CreateMenuPermissionService();
            await service.SetForRoleAsync(role.Id,
            [
                new SaveMenuPermissionItem { MenuItemId = menuA.Id, CanView = true },
                new SaveMenuPermissionItem { MenuItemId = menuB.Id, CanView = true },
            ]);

            // The next save only resubmits menuA — menuB's earlier grant must not survive.
            await service.SetForRoleAsync(role.Id, [new SaveMenuPermissionItem { MenuItemId = menuA.Id, CanView = true }]);

            var afterSet = await service.GetForRoleAsync(role.Id);
            Assert.True(Assert.Single(afterSet, r => r.MenuItemId == menuA.Id).CanView);
            Assert.False(Assert.Single(afterSet, r => r.MenuItemId == menuB.Id).CanView);
        }

        [Fact]
        public async Task MenuPermissionService_SetForRole_RejectsUnknownOrInactiveMenuItem()
        {
            var role = new RoleDefinition { Name = "front-desk", DisplayName = "Front Desk" };
            var inactiveMenu = new domain.Entities.Navigation.MenuItem
            {
                Portal = "admin", Label = "Retired", Path = "/admin/retired", Icon = "Circle",
                SectionOrder = 0, SortOrder = 0, IsActive = false,
            };
            _db.Context.AddRange(role, inactiveMenu);
            await _db.Context.SaveChangesAsync();
            _db.Context.ChangeTracker.Clear();

            var service = CreateMenuPermissionService();

            await Assert.ThrowsAsync<DomainValidationException>(() => service.SetForRoleAsync(
                role.Id, [new SaveMenuPermissionItem { MenuItemId = Guid.NewGuid(), CanView = true }]));

            await Assert.ThrowsAsync<DomainValidationException>(() => service.SetForRoleAsync(
                role.Id, [new SaveMenuPermissionItem { MenuItemId = inactiveMenu.Id, CanView = true }]));
        }

        [Fact]
        public async Task MenuPermissionService_ThrowsNotFound_ForAnUnknownRole()
        {
            var service = CreateMenuPermissionService();
            await Assert.ThrowsAsync<NotFoundException>(() => service.GetForRoleAsync(Guid.NewGuid()));
        }

        [Fact]
        public async Task MenuPermissionService_SetAndGet_RoundTripsCanApprove()
        {
            var role = new RoleDefinition { Name = "front-desk-approve", DisplayName = "Front Desk" };
            var menuItem = new domain.Entities.Navigation.MenuItem
            {
                Portal = "admin", Label = "Refunds", Path = "/admin/refunds", Icon = "Receipt",
                SectionOrder = 0, SortOrder = 0, IsActive = true, RequiredModule = PermissionModule.BillingFinance,
            };
            _db.Context.AddRange(role, menuItem);
            await _db.Context.SaveChangesAsync();

            var service = CreateMenuPermissionService();
            await service.SetForRoleAsync(role.Id, [new SaveMenuPermissionItem { MenuItemId = menuItem.Id, CanView = true, CanApprove = true }]);

            var afterSet = await service.GetForRoleAsync(role.Id);
            var grant = Assert.Single(afterSet, r => r.MenuItemId == menuItem.Id);
            Assert.True(grant.CanView);
            Assert.True(grant.CanApprove);
            Assert.False(grant.CanCreate);
        }

        /// <summary>
        /// Module-aggregated enforcement (the decided architecture): every [HasPermission]
        /// check still reads a "Module:Action" claim, so a menu's grant is rolled up into
        /// whichever module its RequiredModule names. Two menu items sharing one module, only
        /// one granted CanEdit, must still produce the module's Edit claim — proving and
        /// locking in the documented, accepted limitation (not just leaving it as a comment).
        /// </summary>
        [Fact]
        public async Task MenuService_GetModulePermissionClaims_AggregatesAcrossSiblingMenuItems()
        {
            var teacherRole = new RoleDefinition { Name = "teacher", DisplayName = "Teacher" };
            var teacherUser = await _db.SeedUserAsync($"teacher-{Guid.NewGuid():N}@test.com", "x", UserRole.Teacher);

            var itemA = new domain.Entities.Navigation.MenuItem
            {
                Portal = "teacher", Label = "My Classes", Path = "/teacher", Icon = "LayoutDashboard",
                SectionOrder = 0, SortOrder = 0, IsActive = true, RequiredModule = PermissionModule.SessionCalendarManagement,
            };
            var itemB = new domain.Entities.Navigation.MenuItem
            {
                Portal = "teacher", Label = "Attendance", Path = "/teacher/attendance", Icon = "ClipboardCheck",
                SectionOrder = 0, SortOrder = 1, IsActive = true, RequiredModule = PermissionModule.SessionCalendarManagement,
            };
            _db.Context.AddRange(teacherRole, itemA, itemB);
            await _db.Context.SaveChangesAsync();

            var menuPermissions = CreateMenuPermissionService();
            await menuPermissions.SetForRoleAsync(teacherRole.Id,
            [
                new SaveMenuPermissionItem { MenuItemId = itemA.Id, CanView = true }, // no Edit here
                new SaveMenuPermissionItem { MenuItemId = itemB.Id, CanView = true, CanEdit = true }, // Edit granted only here
            ]);

            var menuService = CreateMenuService();
            var claims = await menuService.GetModulePermissionClaimsAsync(teacherUser.Id, UserRole.Teacher);

            Assert.Contains($"{PermissionModule.SessionCalendarManagement}:{PermissionAction.View}", claims);
            Assert.Contains($"{PermissionModule.SessionCalendarManagement}:{PermissionAction.Edit}", claims);
        }

        /// <summary>
        /// A Sub Admin's real login claims are the union of their preset's Menu Access grants
        /// and their own SubAdminPermission rows — the additive overlay that keeps Access
        /// Request approval (which writes SubAdminPermission directly) actually taking effect.
        /// </summary>
        [Fact]
        public async Task AuthService_Login_SubAdminClaims_UnionMenuAccessGrantsWithSubAdminPermissionOverlay()
        {
            var baseRole = new RoleDefinition { Name = "sub-admin", DisplayName = "Parent Relationship Manager" };
            var menuItem = new domain.Entities.Navigation.MenuItem
            {
                Portal = "subadmin", Label = "Reports", Path = "/subadmin/reports", Icon = "BarChart3",
                SectionOrder = 0, SortOrder = 0, IsActive = true, RequiredModule = PermissionModule.ReportsAnalytics,
            };
            var subAdmin = await _db.SeedUserAsync("rm-approve@test.com", _hasher.Hash("4821"), UserRole.SubAdmin);
            _db.Context.AddRange(baseRole, menuItem);
            await _db.Context.SaveChangesAsync();

            var menuPermissions = CreateMenuPermissionService();
            await menuPermissions.SetForRoleAsync(baseRole.Id, [new SaveMenuPermissionItem { MenuItemId = menuItem.Id, CanView = true }]);

            // The per-person overlay Access Request approval writes directly.
            _db.Context.SubAdminPermissions.Add(new SubAdminPermission
            {
                UserId = subAdmin.Id,
                Module = PermissionModule.BillingFinance,
                CanView = true,
            });
            await _db.Context.SaveChangesAsync();

            var response = await CreateAuthService().LoginAsync(new LoginRequest { Email = "rm-approve@test.com", Pin = "4821" });

            Assert.Contains($"{PermissionModule.ReportsAnalytics}:{PermissionAction.View}", response.Permissions);
            Assert.Contains($"{PermissionModule.BillingFinance}:{PermissionAction.View}", response.Permissions);
        }

        public void Dispose() => _db.Dispose();
    }
}
