using iucs.readernest.application.Common.Exceptions;
using iucs.readernest.application.Dto.Enrollment;
using iucs.readernest.application.Dto.Resources;
using iucs.readernest.application.Services;
using iucs.readernest.domain.Entities.Academics;
using iucs.readernest.domain.Entities.Integrations;
using iucs.readernest.domain.Entities.Sessions;
using iucs.readernest.domain.Entities.Users;
using iucs.readernest.domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace iucs.readernest.tests
{
    /// <summary>
    /// Covers the three client requirements: admin-created children, Google Drive link resources,
    /// and the one-recording-at-a-time setting (allowConcurrentRecording).
    /// </summary>
    public class ClientRequirementsTests : IDisposable
    {
        private readonly TestDatabase _db = new();
        private readonly FakeEmailSender _emailSender = new();
        private readonly FakeBulkFileReader _bulkFileReader = new();
        private readonly AuditLogService _auditLog;
        private readonly NotificationService _notifications;

        public ClientRequirementsTests()
        {
            _auditLog = new AuditLogService(_db.UnitOfWork, _db.CurrentUser);
            var templates = new EmailTemplateService(_db.UnitOfWork, _auditLog, new Microsoft.Extensions.Caching.Memory.MemoryCache(new Microsoft.Extensions.Caching.Memory.MemoryCacheOptions()));
            _notifications = new NotificationService(_db.UnitOfWork, _emailSender, templates, NullLogger<NotificationService>.Instance);
        }

        public void Dispose() => _db.Dispose();

        private SessionService CreateSessionService() =>
            new(_db.UnitOfWork, _auditLog,
                new PayoutService(_db.UnitOfWork, _auditLog, _notifications, new ConfigurationBuilder().Build()),
                _notifications, _db.CurrentUser, new FakeJitsiTokenService(), new ClassSessionEventLogService(_db.UnitOfWork), new FakeTokenService());

        private EnrollmentService CreateEnrollmentService()
        {
            var billing = new BillingService(_db.UnitOfWork, _auditLog, new FakePaymentGateway(), _notifications, _db.CurrentUser, _bulkFileReader, new FakeInvoicePdfGenerator());
            return new EnrollmentService(_db.UnitOfWork, _auditLog, billing, new BatchService(_db.UnitOfWork, _auditLog, _notifications), _bulkFileReader);
        }

        private ResourceService CreateResourceService() => new(_db.UnitOfWork, _auditLog);

        // ---------- one-recording-at-a-time ----------

        private async Task SetJitsiConfigAsync(string configJson)
        {
            _db.Context.Integrations.Add(new Integration
            {
                Key = "jitsi",
                Name = "Jitsi",
                Category = IntegrationCategory.VideoConferencing,
                IsEnabled = true,
                ConfigJson = configJson,
            });
            await _db.Context.SaveChangesAsync();
        }

        private async Task<ClassSession> SeedLiveSessionAsync()
        {
            var teacherUser = await _db.SeedUserAsync($"t-{Guid.NewGuid():N}@test.com", "x", UserRole.Teacher);
            var teacher = new TeacherProfile { UserId = teacherUser.Id };
            var session = new ClassSession
            {
                TeacherProfile = teacher,
                Status = SessionStatus.InProgress,
                ScheduledStartAtUtc = DateTime.UtcNow,
                ScheduledEndAtUtc = DateTime.UtcNow.AddMinutes(45),
            };
            _db.Context.AddRange(teacher, session);
            await _db.Context.SaveChangesAsync();
            return session;
        }

        [Fact]
        public async Task ClassroomSettings_ConcurrentRecording_DefaultsToAllowedWhenNotConfigured()
        {
            var settings = await CreateSessionService().GetClassroomSettingsAsync();
            Assert.True(settings.AllowConcurrentRecording);

            await SetJitsiConfigAsync("""{"domain":"meet.test","autoRecord":"true"}""");
            Assert.True((await CreateSessionService().GetClassroomSettingsAsync()).AllowConcurrentRecording);
        }

        [Fact]
        public async Task ClassroomSettings_ReadsAutoRecordAndConcurrentFlagsFromDb()
        {
            await SetJitsiConfigAsync("""{"domain":"meet.test","autoRecord":"false","allowConcurrentRecording":"false"}""");

            var settings = await CreateSessionService().GetClassroomSettingsAsync();

            Assert.False(settings.AutoRecordEnabled);
            Assert.False(settings.AllowConcurrentRecording);
        }

        [Fact]
        public async Task RecordingSlot_WhenConcurrentAllowed_EveryClassIsGranted()
        {
            await SetJitsiConfigAsync("""{"allowConcurrentRecording":"true"}""");
            var a = await SeedLiveSessionAsync();
            var b = await SeedLiveSessionAsync();
            var service = CreateSessionService();

            Assert.True((await service.AcquireRecordingSlotAsync(a.Id)).Granted);
            Assert.True((await service.AcquireRecordingSlotAsync(b.Id)).Granted);
        }

        [Fact]
        public async Task RecordingSlot_WhenConcurrentDisallowed_SecondClassWaitsUntilFirstReleases()
        {
            await SetJitsiConfigAsync("""{"allowConcurrentRecording":"false"}""");
            var first = await SeedLiveSessionAsync();
            var second = await SeedLiveSessionAsync();
            var service = CreateSessionService();

            Assert.True((await service.AcquireRecordingSlotAsync(first.Id)).Granted);

            var refused = await service.AcquireRecordingSlotAsync(second.Id);
            Assert.False(refused.Granted);
            Assert.False(string.IsNullOrWhiteSpace(refused.Reason));

            await service.ReleaseRecordingSlotAsync(first.Id);
            Assert.True((await service.AcquireRecordingSlotAsync(second.Id)).Granted);
        }

        [Fact]
        public async Task RecordingSlot_HolderCanRenewItsOwnLease()
        {
            await SetJitsiConfigAsync("""{"allowConcurrentRecording":"false"}""");
            var session = await SeedLiveSessionAsync();
            var service = CreateSessionService();

            Assert.True((await service.AcquireRecordingSlotAsync(session.Id)).Granted);
            Assert.True((await service.AcquireRecordingSlotAsync(session.Id)).Granted);
        }

        [Fact]
        public async Task RecordingSlot_FreedWhenHoldingClassCompletes()
        {
            await SetJitsiConfigAsync("""{"allowConcurrentRecording":"false"}""");
            var first = await SeedLiveSessionAsync();
            var second = await SeedLiveSessionAsync();
            var service = CreateSessionService();
            await service.AcquireRecordingSlotAsync(first.Id);
            Assert.False((await service.AcquireRecordingSlotAsync(second.Id)).Granted);

            await _db.Context.ClassSessions.Where(s => s.Id == first.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, SessionStatus.Completed));

            Assert.True((await service.AcquireRecordingSlotAsync(second.Id)).Granted);
        }

        [Fact]
        public async Task RecordingSlot_StaleLeaseFromCrashedClassDoesNotBlockForever()
        {
            await SetJitsiConfigAsync("""{"allowConcurrentRecording":"false"}""");
            var crashed = await SeedLiveSessionAsync();
            var next = await SeedLiveSessionAsync();
            var staleAt = DateTime.UtcNow.AddMinutes(-10);
            await _db.Context.ClassSessions.Where(s => s.Id == crashed.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.RecordingSlotHeldAtUtc, staleAt));

            Assert.True((await CreateSessionService().AcquireRecordingSlotAsync(next.Id)).Granted);
        }

        [Fact]
        public async Task IntegrationSave_PersistsAllowConcurrentRecordingOff_AcrossReload()
        {
            var integrations = new IntegrationService(_db.UnitOfWork, _auditLog, new FakePaymentGateway());
            var created = await integrations.CreateAsync(new iucs.readernest.application.Dto.Integrations.SaveIntegrationRequest
            {
                Key = "jitsi",
                Name = "Jitsi Meet",
                Category = IntegrationCategory.VideoConferencing,
                IsEnabled = true,
                Config = new() { ["domain"] = "meet.test", ["autoRecord"] = "true", ["allowConcurrentRecording"] = "false" },
            });

            var listed = (await integrations.ListAsync()).Single(i => i.Key == "jitsi");
            Assert.Equal("false", listed.Config["allowConcurrentRecording"]);

            await integrations.UpdateAsync(created.Id, new iucs.readernest.application.Dto.Integrations.SaveIntegrationRequest
            {
                Key = "jitsi",
                Name = "Jitsi Meet",
                Category = IntegrationCategory.VideoConferencing,
                IsEnabled = true,
                Config = new() { ["domain"] = "meet.test", ["autoRecord"] = "true", ["allowConcurrentRecording"] = "false" },
            });

            Assert.Equal("false", (await integrations.ListAsync()).Single(i => i.Key == "jitsi").Config["allowConcurrentRecording"]);
            Assert.False((await CreateSessionService().GetClassroomSettingsAsync()).AllowConcurrentRecording);
        }

        // ---------- Google Drive link resources ----------

        [Theory]
        [InlineData("https://drive.google.com/file/d/abc123/view?usp=sharing")]
        [InlineData("https://docs.google.com/document/d/abc123/edit")]
        public async Task DriveLink_ValidGoogleLink_CreatesExternalResource(string url)
        {
            var dto = await CreateResourceService().CreateLinkAsync(
                new CreateLinkResourceRequest { Title = "Week 1 sheet", Type = ResourceType.Worksheet, Url = url });

            Assert.Equal(url, dto.ExternalUrl);
            Assert.Equal("Week 1 sheet", dto.Title);
            Assert.Null(dto.FileSizeBytes);
            Assert.False(dto.IsDownloadable);
        }

        [Theory]
        [InlineData("http://drive.google.com/file/d/abc")]       // not https
        [InlineData("https://evil.example.com/file")]            // not Google
        [InlineData("https://drive.google.com.evil.com/file")]   // look-alike host
        [InlineData("javascript:alert(1)")]
        [InlineData("not a url")]
        public async Task DriveLink_InvalidOrNonGoogleLink_IsRejected(string url)
        {
            await Assert.ThrowsAsync<DomainValidationException>(() => CreateResourceService().CreateLinkAsync(
                new CreateLinkResourceRequest { Title = "x", Type = ResourceType.Worksheet, Url = url }));
        }

        [Fact]
        public async Task DriveLink_ReadingBookLinkIsNeverDownloadable_AndUploadedFilesHaveNoExternalUrl()
        {
            var service = CreateResourceService();
            var link = await service.CreateLinkAsync(
                new CreateLinkResourceRequest { Title = "Book", Type = ResourceType.ReadingBook, Url = "https://drive.google.com/file/d/b/view" });
            var uploaded = await service.CreateAsync(
                new CreateResourceRequest { Title = "PDF", Type = ResourceType.Worksheet, IsDownloadable = true },
                "abc.pdf", "application/pdf", 1024);

            Assert.False(link.IsDownloadable);
            Assert.Null(uploaded.ExternalUrl);
            Assert.Equal(1024, uploaded.FileSizeBytes);
        }

        // ---------- admin creates a child under a parent ----------

        private async Task<User> SeedParentAsync()
        {
            var user = await _db.SeedUserAsync($"p-{Guid.NewGuid():N}@test.com", "x", UserRole.Parent);
            _db.Context.ParentProfiles.Add(new ParentProfile { UserId = user.Id });
            await _db.Context.SaveChangesAsync();
            return user;
        }

        [Fact]
        public async Task CreateChild_LinksChildToTheParentAndSavesDetails()
        {
            var parent = await SeedParentAsync();

            var child = await CreateEnrollmentService().CreateChildAsync(new CreateChildRequest
            {
                ParentUserId = parent.Id,
                FirstName = "  Aarav ",
                LastName = "Shah",
                DateOfBirth = new DateOnly(2018, 5, 1),
                Gender = Gender.Male,
                AcademicLevel = "Grade 2",
            });

            var saved = await _db.Context.Children.Include(c => c.ParentProfile).SingleAsync(c => c.Id == child.Id);
            Assert.Equal(parent.Id, saved.ParentProfile.UserId);
            Assert.Equal("Aarav", saved.FirstName);
            Assert.Equal("Shah", saved.LastName);
            Assert.Equal(Gender.Male, saved.Gender);
            Assert.Equal("Grade 2", saved.AcademicLevel);
            Assert.True(saved.IsActive);

            var listed = await CreateEnrollmentService().ListChildrenForParentUserAsync(parent.Id);
            Assert.Contains(listed, c => c.Id == child.Id);
        }

        [Fact]
        public async Task CreateChild_DuplicateNameUnderSameParentIsRefused_ButAllowedUnderAnotherParent()
        {
            var parentA = await SeedParentAsync();
            var parentB = await SeedParentAsync();
            var service = CreateEnrollmentService();
            await service.CreateChildAsync(new CreateChildRequest { ParentUserId = parentA.Id, FirstName = "Mia", LastName = "Rao" });

            await Assert.ThrowsAsync<ConflictException>(() =>
                service.CreateChildAsync(new CreateChildRequest { ParentUserId = parentA.Id, FirstName = "mia", LastName = "RAO" }));

            var other = await service.CreateChildAsync(new CreateChildRequest { ParentUserId = parentB.Id, FirstName = "Mia", LastName = "Rao" });
            Assert.NotEqual(Guid.Empty, other.Id);
        }

        [Fact]
        public async Task CreateChild_UnknownParentOrFutureBirthDate_IsRejected()
        {
            var parent = await SeedParentAsync();
            var service = CreateEnrollmentService();

            await Assert.ThrowsAsync<NotFoundException>(() =>
                service.CreateChildAsync(new CreateChildRequest { ParentUserId = Guid.NewGuid(), FirstName = "Ghost" }));
            await Assert.ThrowsAsync<DomainValidationException>(() =>
                service.CreateChildAsync(new CreateChildRequest
                {
                    ParentUserId = parent.Id,
                    FirstName = "Future",
                    DateOfBirth = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(5)),
                }));
        }
    }
}
