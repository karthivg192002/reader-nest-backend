using iucs.readernest.application.Common.Exceptions;
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

        private UserService CreateUserService() => new(_db.UnitOfWork, _hasher, _notifications, _auditLog, _emailSender, _whatsAppSender);

        private CourseService CreateCourseService() => new(_db.UnitOfWork, _auditLog);

        private BatchService CreateBatchService() => new(_db.UnitOfWork, _auditLog);

        private PayoutService CreatePayoutService() => new(_db.UnitOfWork, _auditLog, _notifications);

        private SessionService CreateSessionService() => new(_db.UnitOfWork, _auditLog, CreatePayoutService(), _notifications, _db.CurrentUser);

        private BillingService CreateBillingService() => new(_db.UnitOfWork, _auditLog, new FakePaymentGateway(), _notifications);

        private EnrollmentService CreateEnrollmentService() => new(_db.UnitOfWork, _auditLog);

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
