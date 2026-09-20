using System.Security.Claims;
using iucs.readernest.api.Auth;
using iucs.readernest.application.Common.Interfaces;
using iucs.readernest.application.Dto.Resources;
using iucs.readernest.application.Services;
using iucs.readernest.domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace iucs.readernest.api.Controllers
{
    [ApiController]
    [Route("api/resources")]
    public class ResourcesController : ControllerBase
    {
        private const long MaxUploadBytes = 100 * 1024 * 1024;

        // Browser-to-bucket uploads (recordings) skip the API's body limit entirely; this is just a sanity cap.
        private const long MaxLargeUploadBytes = 20L * 1024 * 1024 * 1024;

        // Keys are always server-generated as <32 hex><ext>; anything else is not one of ours.
        private static readonly System.Text.RegularExpressions.Regex StoredKeyPattern =
            new(@"^[0-9a-f]{32}\.[a-z0-9]{2,5}$", System.Text.RegularExpressions.RegexOptions.Compiled);

        private readonly IResourceService _resourceService;
        private readonly IFileStorage _fileStorage;
        private readonly IDirectUploadStorage _directUploads;

        public ResourcesController(IResourceService resourceService, IFileStorage fileStorage, IDirectUploadStorage directUploads)
        {
            _resourceService = resourceService;
            _fileStorage = fileStorage;
            _directUploads = directUploads;
        }

        // Admin console only: Teacher and Parent also carry ContentAccessManagement:View
        // (they need SOME grant in that module to reach their own scoped /mine and portal
        // routes), so HasPermission alone doesn't exclude them from this unscoped,
        // see/download-everything screen — the role check is what actually does. AdmissionTeam
        // is included deliberately (not just SubAdmin): it's a distinct backend role from
        // SubAdmin for real delegated-portal accounts (confirmed live), and /admission/resources
        // already exists in the menu system for any admin who grants ContentAccessManagement to
        // an admission account — without it here that account would 403 on the whole page.
        [HttpGet]
        [Authorize(Roles = $"{nameof(UserRole.Admin)},{nameof(UserRole.SubAdmin)},{nameof(UserRole.AdmissionTeam)}")]
        [HasPermission(PermissionModule.ContentAccessManagement, PermissionAction.View)]
        public async Task<ActionResult<IReadOnlyList<ResourceDto>>> List(
            [FromQuery] ResourceType? type,
            CancellationToken cancellationToken)
        {
            return Ok(await _resourceService.ListAsync(type, cancellationToken));
        }

        /// <summary>Teacher portal: resources tied to the signed-in teacher's own batches/courses.</summary>
        [HttpGet("mine")]
        [Authorize(Roles = nameof(UserRole.Teacher))]
        public async Task<ActionResult<IReadOnlyList<ResourceDto>>> Mine(
            [FromQuery] ResourceType? type,
            CancellationToken cancellationToken)
        {
            var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            return Ok(await _resourceService.ListForTeacherUserAsync(userId, type, cancellationToken));
        }

        /// <summary>Teacher portal: upload a resource to one of the teacher's own batches.</summary>
        [HttpPost("mine")]
        [Authorize(Roles = nameof(UserRole.Teacher))]
        [RequestSizeLimit(MaxUploadBytes)]
        public async Task<ActionResult<ResourceDto>> UploadMine(
            [FromForm] CreateResourceRequest request,
            IFormFile file,
            CancellationToken cancellationToken)
        {
            if (file.Length == 0)
            {
                return BadRequest(new ProblemDetails { Status = 400, Title = "Bad Request", Detail = "The uploaded file is empty." });
            }

            await using var stream = file.OpenReadStream();
            var stored = await _fileStorage.StoreAsync(stream, file.FileName, cancellationToken);
            var mimeType = string.IsNullOrWhiteSpace(file.ContentType) ? null : file.ContentType;

            var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var resource = await _resourceService.CreateForTeacherUserAsync(
                userId, request, stored.RelativePath, mimeType, stored.SizeBytes, cancellationToken);

            return CreatedAtAction(nameof(Mine), null, resource);
        }

        /// <summary>Teacher portal: download a resource the teacher owns (403 otherwise).</summary>
        [HttpGet("{id:guid}/mine/download")]
        [Authorize(Roles = nameof(UserRole.Teacher))]
        public async Task<IActionResult> DownloadMine(Guid id, CancellationToken cancellationToken)
        {
            var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var resource = await _resourceService.GetForTeacherDownloadAsync(userId, id, cancellationToken);
            var stream = await _fileStorage.OpenReadAsync(resource.FileUrl, cancellationToken);

            if (stream is null)
            {
                return NotFound(new ProblemDetails { Status = 404, Title = "Not Found", Detail = "The stored file is missing." });
            }

            var mimeType = string.IsNullOrWhiteSpace(resource.MimeType) ? "application/octet-stream" : resource.MimeType;
            return File(stream, mimeType, $"{resource.Title}{Path.GetExtension(resource.FileUrl)}");
        }

        [HttpPost]
        [Authorize(Roles = $"{nameof(UserRole.Admin)},{nameof(UserRole.SubAdmin)},{nameof(UserRole.AdmissionTeam)}")]
        [HasPermission(PermissionModule.ContentAccessManagement, PermissionAction.Create)]
        [RequestSizeLimit(MaxUploadBytes)]
        public async Task<ActionResult<ResourceDto>> Upload(
            [FromForm] CreateResourceRequest request,
            IFormFile file,
            CancellationToken cancellationToken)
        {
            if (file.Length == 0)
            {
                return BadRequest(new ProblemDetails { Status = 400, Title = "Bad Request", Detail = "The uploaded file is empty." });
            }

            await using var stream = file.OpenReadStream();
            var stored = await _fileStorage.StoreAsync(stream, file.FileName, cancellationToken);

            // Browsers/clients may send an empty Content-Type on the file part;
            // never persist "" (as opposed to null) or the download response fails to set one.
            var mimeType = string.IsNullOrWhiteSpace(file.ContentType) ? null : file.ContentType;
            var resource = await _resourceService.CreateAsync(
                request, stored.RelativePath, mimeType, stored.SizeBytes, cancellationToken);

            return CreatedAtAction(nameof(List), null, resource);
        }

        // ---- Large uploads (recordings): the browser sends parts straight to the bucket ----

        /// <summary>Starts a multipart upload; the client then PUTs each part to a presigned URL.</summary>
        [HttpPost("uploads")]
        [Authorize(Roles = $"{nameof(UserRole.Admin)},{nameof(UserRole.SubAdmin)},{nameof(UserRole.AdmissionTeam)}")]
        [HasPermission(PermissionModule.ContentAccessManagement, PermissionAction.Create)]
        public async Task<ActionResult<LargeUploadDto>> StartLargeUpload(StartLargeUploadRequest request, CancellationToken cancellationToken)
        {
            if (request.SizeBytes > MaxLargeUploadBytes)
            {
                return BadRequest(new ProblemDetails { Status = 400, Title = "Bad Request", Detail = "That file is larger than the 20 GB limit." });
            }

            var upload = await _directUploads.StartMultipartAsync(request.FileName, request.ContentType, cancellationToken);
            return Ok(new LargeUploadDto
            {
                Key = upload.Key,
                UploadId = upload.UploadId,
                PartSizeBytes = upload.PartSizeBytes,
                TotalParts = (int)Math.Ceiling(request.SizeBytes / (double)upload.PartSizeBytes),
            });
        }

        [HttpPost("uploads/parts")]
        [Authorize(Roles = $"{nameof(UserRole.Admin)},{nameof(UserRole.SubAdmin)},{nameof(UserRole.AdmissionTeam)}")]
        [HasPermission(PermissionModule.ContentAccessManagement, PermissionAction.Create)]
        public ActionResult<IReadOnlyList<LargeUploadPartUrlDto>> LargeUploadPartUrls(LargeUploadPartsRequest request)
        {
            if (!StoredKeyPattern.IsMatch(request.Key) || request.PartNumbers.Any(n => n < 1 || n > 10000))
            {
                return BadRequest(new ProblemDetails { Status = 400, Title = "Bad Request", Detail = "Invalid upload reference." });
            }

            return Ok(_directUploads.GetPartUrls(request.Key, request.UploadId, request.PartNumbers)
                .Select(p => new LargeUploadPartUrlDto { PartNumber = p.PartNumber, Url = p.Url }).ToList());
        }

        /// <summary>Stitches the uploaded parts together and creates the resource, exactly like a normal upload.</summary>
        [HttpPost("uploads/complete")]
        [Authorize(Roles = $"{nameof(UserRole.Admin)},{nameof(UserRole.SubAdmin)},{nameof(UserRole.AdmissionTeam)}")]
        [HasPermission(PermissionModule.ContentAccessManagement, PermissionAction.Create)]
        public async Task<ActionResult<ResourceDto>> CompleteLargeUpload(CompleteLargeUploadRequest request, CancellationToken cancellationToken)
        {
            if (!StoredKeyPattern.IsMatch(request.Key))
            {
                return BadRequest(new ProblemDetails { Status = 400, Title = "Bad Request", Detail = "Invalid upload reference." });
            }

            var size = await _directUploads.CompleteMultipartAsync(
                request.Key,
                request.UploadId,
                request.Parts.Select(p => new UploadedPart { PartNumber = p.PartNumber, ETag = p.ETag }).ToList(),
                cancellationToken);
            var mimeType = string.IsNullOrWhiteSpace(request.ContentType) ? null : request.ContentType;
            var resource = await _resourceService.CreateAsync(request, request.Key, mimeType, size, cancellationToken);
            return CreatedAtAction(nameof(List), null, resource);
        }

        [HttpPost("uploads/abort")]
        [Authorize(Roles = $"{nameof(UserRole.Admin)},{nameof(UserRole.SubAdmin)},{nameof(UserRole.AdmissionTeam)}")]
        [HasPermission(PermissionModule.ContentAccessManagement, PermissionAction.Create)]
        public async Task<IActionResult> AbortLargeUpload(AbortLargeUploadRequest request, CancellationToken cancellationToken)
        {
            if (!StoredKeyPattern.IsMatch(request.Key))
            {
                return BadRequest(new ProblemDetails { Status = 400, Title = "Bad Request", Detail = "Invalid upload reference." });
            }

            await _directUploads.AbortMultipartAsync(request.Key, request.UploadId, cancellationToken);
            return NoContent();
        }

        [HttpGet("{id:guid}/download")]
        [Authorize(Roles = $"{nameof(UserRole.Admin)},{nameof(UserRole.SubAdmin)},{nameof(UserRole.AdmissionTeam)}")]
        [HasPermission(PermissionModule.ContentAccessManagement, PermissionAction.View)]
        public async Task<IActionResult> Download(Guid id, CancellationToken cancellationToken)
        {
            var resource = await _resourceService.GetForDownloadAsync(id, cancellationToken);
            var stream = await _fileStorage.OpenReadAsync(resource.FileUrl, cancellationToken);

            if (stream is null)
            {
                return NotFound(new ProblemDetails { Status = 404, Title = "Not Found", Detail = "The stored file is missing." });
            }

            var mimeType = string.IsNullOrWhiteSpace(resource.MimeType) ? "application/octet-stream" : resource.MimeType;
            return File(stream, mimeType, $"{resource.Title}{Path.GetExtension(resource.FileUrl)}");
        }

        [HttpPut("{id:guid}")]
        [Authorize(Roles = $"{nameof(UserRole.Admin)},{nameof(UserRole.SubAdmin)},{nameof(UserRole.AdmissionTeam)}")]
        [HasPermission(PermissionModule.ContentAccessManagement, PermissionAction.Edit)]
        public async Task<ActionResult<ResourceDto>> Update(
            Guid id,
            UpdateResourceRequest request,
            CancellationToken cancellationToken)
        {
            return Ok(await _resourceService.UpdateAsync(id, request, cancellationToken));
        }

        [HttpPost("{id:guid}/grants")]
        [Authorize(Roles = $"{nameof(UserRole.Admin)},{nameof(UserRole.SubAdmin)},{nameof(UserRole.AdmissionTeam)}")]
        [HasPermission(PermissionModule.ContentAccessManagement, PermissionAction.Edit)]
        public async Task<IActionResult> GrantAccess(
            Guid id,
            GrantResourceAccessRequest request,
            CancellationToken cancellationToken)
        {
            await _resourceService.GrantAccessAsync(id, request, cancellationToken);
            return NoContent();
        }
    }
}
