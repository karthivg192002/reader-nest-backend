using System.Text;
using iucs.readernest.api.Services;
using iucs.readernest.application.Common.Exceptions;
using iucs.readernest.application.Common.Interfaces;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Xunit;

namespace iucs.readernest.tests
{
    /// <summary>Storage:Provider = Local: same capabilities as S3 (uploads, multipart, signed links), on disk.</summary>
    public sealed class LocalFileStorageTests : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "rn-local-storage-" + Guid.NewGuid().ToString("N"));
        private readonly LocalFileStorage _storage;

        public LocalFileStorageTests()
        {
            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Storage:LocalPath"] = _root,
                    ["Jwt:SigningKey"] = "test-signing-key-test-signing-key",
                    ["Api:BaseUrl"] = "https://api.example.test",
                })
                .Build();
            _storage = new LocalFileStorage(new TestEnvironment(), config, new HttpContextAccessor());
        }

        public void Dispose()
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }

        [Fact]
        public async Task StoresAndReadsBack_AndRefusesUnsafeFileTypes()
        {
            var stored = await _storage.StoreAsync(new MemoryStream(Encoding.UTF8.GetBytes("worksheet")), "Week 1.pdf");
            Assert.EndsWith(".pdf", stored.RelativePath);
            Assert.Equal(9, stored.SizeBytes);

            await using var read = await _storage.OpenReadAsync(stored.RelativePath);
            Assert.Equal("worksheet", await new StreamReader(read!).ReadToEndAsync());

            await Assert.ThrowsAsync<DomainValidationException>(() => _storage.StoreAsync(new MemoryStream([1]), "evil.exe"));
            Assert.Null(await _storage.OpenReadAsync("missing.pdf"));
        }

        [Fact]
        public async Task KeysCanNeverEscapeTheStorageFolder()
        {
            Assert.Null(await _storage.OpenReadAsync("../secrets.txt"));
            Assert.Null(await _storage.OpenReadAsync("sub/../../etc/passwd"));
            Assert.Null(_storage.ExistingFilePath("..\\x.pdf"));
        }

        [Fact]
        public async Task MultipartUpload_StitchesPartsInOrder_WithSignedPartUrls()
        {
            var session = await _storage.StartMultipartAsync("lesson.mp4", "video/mp4");
            Assert.Equal(LocalFileStorage.PartSizeBytes, session.PartSizeBytes);

            var urls = _storage.GetPartUrls(session.Key, session.UploadId, [1, 2]);
            Assert.All(urls, u => Assert.StartsWith($"https://api.example.test/api/storage/local/parts/{session.UploadId}/", u.Url));

            // Parts can arrive out of order; each is answered with an ETag.
            var tag2 = await _storage.SavePartAsync(session.UploadId, session.Key, 2, new MemoryStream(Encoding.UTF8.GetBytes("world")), default);
            var tag1 = await _storage.SavePartAsync(session.UploadId, session.Key, 1, new MemoryStream(Encoding.UTF8.GetBytes("hello ")), default);
            Assert.StartsWith("\"", tag1);

            var size = await _storage.CompleteMultipartAsync(session.Key, session.UploadId,
                [new UploadedPart { PartNumber = 2, ETag = tag2 }, new UploadedPart { PartNumber = 1, ETag = tag1 }]);
            Assert.Equal(11, size);
            await using var read = await _storage.OpenReadAsync(session.Key);
            Assert.Equal("hello world", await new StreamReader(read!).ReadToEndAsync());
            Assert.Contains(session.Key, _storage.ListKeys());

            // The finished upload's id is gone: no more parts can be added to it.
            await Assert.ThrowsAsync<DomainValidationException>(() =>
                _storage.SavePartAsync(session.UploadId, session.Key, 3, new MemoryStream([1]), default));
        }

        [Fact]
        public async Task MultipartUpload_MissingPart_IsRefused_AndAbortCleansUp()
        {
            var session = await _storage.StartMultipartAsync("lesson.mp4", null);
            await _storage.SavePartAsync(session.UploadId, session.Key, 2, new MemoryStream([1, 2]), default);
            await Assert.ThrowsAsync<DomainValidationException>(() => _storage.CompleteMultipartAsync(session.Key, session.UploadId,
                [new UploadedPart { PartNumber = 2, ETag = "x" }]));

            await _storage.AbortMultipartAsync(session.Key, session.UploadId);
            Assert.Empty(_storage.ListKeys());
            Assert.False(Directory.Exists(Path.Combine(_root, ".multipart", session.UploadId)));
        }

        [Fact]
        public async Task Signatures_BindTheExactFile_AndExpire()
        {
            var session = await _storage.StartMultipartAsync("lesson.mp4", null);
            var url = new Uri(_storage.GetPartUrls(session.Key, session.UploadId, [1]).Single().Url);
            var query = System.Web.HttpUtility.ParseQueryString(url.Query);
            var exp = long.Parse(query["exp"]!);
            Assert.True(_storage.IsValidPartSignature(session.UploadId, session.Key, 1, exp, query["sig"]));
            Assert.False(_storage.IsValidPartSignature(session.UploadId, session.Key, 2, exp, query["sig"])); // another part
            Assert.False(_storage.IsValidPartSignature(session.UploadId, "other.mp4", 1, exp, query["sig"])); // another file

            var read = new Uri(_storage.GetReadUrl("abc.pdf", TimeSpan.FromMinutes(30), "application/pdf"));
            var readQuery = System.Web.HttpUtility.ParseQueryString(read.Query);
            var readExp = long.Parse(readQuery["exp"]!);
            Assert.True(_storage.IsValidReadSignature("abc.pdf", readExp, "application/pdf", readQuery["sig"]));
            Assert.False(_storage.IsValidReadSignature("other.pdf", readExp, "application/pdf", readQuery["sig"]));

            var expired = new Uri(_storage.GetReadUrl("abc.pdf", TimeSpan.FromMinutes(-1), null));
            var expiredQuery = System.Web.HttpUtility.ParseQueryString(expired.Query);
            Assert.False(_storage.IsValidReadSignature("abc.pdf", long.Parse(expiredQuery["exp"]!), null, expiredQuery["sig"]));
        }

        [Fact]
        public async Task Endpoints_AcceptSignedPartsWithAnETag_AndServeSignedFilesWithRanges()
        {
            var session = await _storage.StartMultipartAsync("lesson.mp4", "video/mp4");
            var partUrl = new Uri(_storage.GetPartUrls(session.Key, session.UploadId, [1]).Single().Url);
            var q = System.Web.HttpUtility.ParseQueryString(partUrl.Query);

            var controller = new iucs.readernest.api.Controllers.LocalStorageController(_storage)
            {
                ControllerContext = new Microsoft.AspNetCore.Mvc.ControllerContext { HttpContext = new DefaultHttpContext() },
            };
            controller.HttpContext.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes("video-bytes"));
            var put = await controller.PutPart(session.UploadId, 1, session.Key, long.Parse(q["exp"]!), q["sig"], default);
            Assert.IsType<Microsoft.AspNetCore.Mvc.OkResult>(put);
            var eTag = controller.Response.Headers.ETag.ToString();
            Assert.False(string.IsNullOrEmpty(eTag));

            // A tampered signature is refused.
            var forged = await controller.PutPart(session.UploadId, 1, session.Key, long.Parse(q["exp"]!), "00" + q["sig"], default);
            Assert.Equal(403, Assert.IsType<Microsoft.AspNetCore.Mvc.ObjectResult>(forged).StatusCode);

            await _storage.CompleteMultipartAsync(session.Key, session.UploadId, [new UploadedPart { PartNumber = 1, ETag = eTag }]);
            var readUrl = new Uri(_storage.GetReadUrl(session.Key, TimeSpan.FromMinutes(30), "video/mp4"));
            var r = System.Web.HttpUtility.ParseQueryString(readUrl.Query);
            var file = Assert.IsType<Microsoft.AspNetCore.Mvc.PhysicalFileResult>(
                controller.GetFile(session.Key, long.Parse(r["exp"]!), r["ct"], r["sig"]));
            Assert.True(file.EnableRangeProcessing);
            Assert.Equal("video/mp4", file.ContentType);

            Assert.Equal(403, Assert.IsType<Microsoft.AspNetCore.Mvc.ObjectResult>(
                controller.GetFile(session.Key, long.Parse(r["exp"]!), "text/html", r["sig"])).StatusCode); // can't swap the content type
        }

        [Fact]
        public async Task Sync_WithoutS3Settings_ReportsWhyAndFinishes()
        {
            var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Storage:LocalPath"] = _root }).Build();
            var progress = new StorageSyncProgress();
            await StorageMigration.SyncAsync("local", Microsoft.Extensions.DependencyInjection.ServiceCollectionContainerBuilderExtensions.BuildServiceProvider(new Microsoft.Extensions.DependencyInjection.ServiceCollection()), config, progress, default);

            Assert.False(progress.Running);
            Assert.NotNull(progress.FinishedAtUtc);
            Assert.Contains("S3 isn't configured", progress.Error);
        }

        [Fact]
        public void Sync_OnlyMovesPortalUploads_NotOtherThingsInTheBucket()
        {
            Assert.True(LocalFileStorage.IsPortalKey("0f8c2b9e4d1a4c6b8e2f7a9d3c5b1e0f.pdf"));
            Assert.False(LocalFileStorage.IsPortalKey("recordings/class-123.mp4"));
            Assert.False(LocalFileStorage.IsPortalKey("../escape.pdf"));
            Assert.False(LocalFileStorage.IsPortalKey(".hidden"));
        }

        [Fact]
        public async Task AbandonedUploads_AreClearedWhenANewUploadStarts()
        {
            var old = await _storage.StartMultipartAsync("old.mp4", null);
            await _storage.SavePartAsync(old.UploadId, old.Key, 1, new MemoryStream(new byte[1024]), default);
            var folder = Path.Combine(_root, ".multipart", old.UploadId);
            foreach (var file in Directory.EnumerateFiles(folder))
            {
                File.SetLastWriteTimeUtc(file, DateTime.UtcNow.AddDays(-2));
            }

            var fresh = await _storage.StartMultipartAsync("new.mp4", null);

            Assert.False(Directory.Exists(folder));
            Assert.True(Directory.Exists(Path.Combine(_root, ".multipart", fresh.UploadId)));
        }

        [Fact]
        public void SyncProgress_SnapshotIsIndependentOfTheRunningSync()
        {
            var live = new StorageSyncProgress { Running = true, Total = 3 };
            live.AddFailure("a.pdf: boom", 20);
            var snap = live.Snapshot();
            live.AddFailure("b.pdf: boom", 20);
            live.Copied = 2;

            Assert.Single(snap.FailedFiles);
            Assert.Equal(0, snap.Copied);
            Assert.Equal(2, live.FailedFiles.Count);
        }

        [Fact]
        public async Task OnLocal_AFileNotCopiedYet_IsServedFromS3_NotReportedMissing()
        {
            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Storage:LocalPath"] = _root,
                    ["Jwt:SigningKey"] = "test-signing-key-test-signing-key",
                    ["Api:BaseUrl"] = "https://api.example.test",
                    ["Storage:S3:Endpoint"] = "objects.example.test",
                    ["Storage:S3:AccessKey"] = "key",
                    ["Storage:S3:SecretKey"] = "secret",
                    ["Storage:S3:BucketName"] = "readernest",
                })
                .Build();
            Assert.True(S3FileStorage.IsConfigured(config));
            var withFallback = new LocalFileStorage(new TestEnvironment(), config, new HttpContextAccessor(), new S3FileStorage(config));

            // Only in S3: the link goes to S3.
            Assert.Contains("objects.example.test", withFallback.GetReadUrl("0f8c2b9e4d1a4c6b8e2f7a9d3c5b1e0f.pdf", TimeSpan.FromHours(1), "application/pdf"));

            // Copied to this server: served from here.
            var stored = await withFallback.StoreAsync(new MemoryStream([1, 2, 3]), "deck.pdf");
            Assert.StartsWith("https://api.example.test/api/storage/local/files/", withFallback.GetReadUrl(stored.RelativePath, TimeSpan.FromHours(1), null));
        }

        private sealed class TestEnvironment : IWebHostEnvironment
        {
            public string WebRootPath { get; set; } = Path.GetTempPath();
            public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
            public string ApplicationName { get; set; } = "tests";
            public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
            public string ContentRootPath { get; set; } = Path.GetTempPath();
            public string EnvironmentName { get; set; } = "Test";
        }
    }
}
