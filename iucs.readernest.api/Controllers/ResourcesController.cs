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

        private readonly IResourceService _resourceService;
        private readonly IFileStorage _fileStorage;

        public ResourcesController(IResourceService resourceService, IFileStorage fileStorage)
        {
            _resourceService = resourceService;
            _fileStorage = fileStorage;
        }

        [HttpGet]
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
            var absolutePath = _fileStorage.GetAbsolutePath(resource.FileUrl);

            if (!System.IO.File.Exists(absolutePath))
            {
                return NotFound(new ProblemDetails { Status = 404, Title = "Not Found", Detail = "The stored file is missing." });
            }

            var mimeType = string.IsNullOrWhiteSpace(resource.MimeType) ? "application/octet-stream" : resource.MimeType;
            return PhysicalFile(absolutePath, mimeType, $"{resource.Title}{Path.GetExtension(resource.FileUrl)}");
        }

        [HttpPost]
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
            // never persist "" (as opposed to null) or PhysicalFile fails to parse it downstream.
            var mimeType = string.IsNullOrWhiteSpace(file.ContentType) ? null : file.ContentType;
            var resource = await _resourceService.CreateAsync(
                request, stored.RelativePath, mimeType, stored.SizeBytes, cancellationToken);

            return CreatedAtAction(nameof(List), null, resource);
        }

        [HttpGet("{id:guid}/download")]
        [HasPermission(PermissionModule.ContentAccessManagement, PermissionAction.View)]
        public async Task<IActionResult> Download(Guid id, CancellationToken cancellationToken)
        {
            var resource = await _resourceService.GetForDownloadAsync(id, cancellationToken);
            var absolutePath = _fileStorage.GetAbsolutePath(resource.FileUrl);

            if (!System.IO.File.Exists(absolutePath))
            {
                return NotFound(new ProblemDetails { Status = 404, Title = "Not Found", Detail = "The stored file is missing." });
            }

            var mimeType = string.IsNullOrWhiteSpace(resource.MimeType) ? "application/octet-stream" : resource.MimeType;
            return PhysicalFile(absolutePath, mimeType, $"{resource.Title}{Path.GetExtension(resource.FileUrl)}");
        }

        [HttpPost("{id:guid}/grants")]
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
