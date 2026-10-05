using iucs.readernest.api.Services;
using iucs.readernest.application.Common.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.StaticFiles;

namespace iucs.readernest.api.Controllers
{
    /// <summary>
    /// The "object store" endpoints LocalFileStorage hands out instead of S3 presigned URLs: the
    /// browser PUTs each part of a large upload here, and plays/downloads files through a signed,
    /// expiring, range-capable read URL. Anonymous on purpose -- like an S3 presigned URL, the
    /// HMAC signature in the link IS the permission (it's only ever issued to a caller who passed
    /// the normal permission checks on ResourcesController / ParentPortalController). Answers 404
    /// when storage isn't set to Local.
    /// </summary>
    [ApiController]
    [Route("api/storage/local")]
    [AllowAnonymous]
    public class LocalStorageController : ControllerBase
    {
        private static readonly FileExtensionContentTypeProvider ContentTypes = new();

        private readonly LocalFileStorage? _storage;

        public LocalStorageController(IFileStorage storage)
        {
            _storage = storage as LocalFileStorage;
        }

        [HttpPut("parts/{uploadId}/{partNumber:int}")]
        [RequestSizeLimit(LocalFileStorage.PartSizeBytes + 1024 * 1024)]
        public async Task<IActionResult> PutPart(
            string uploadId, int partNumber, [FromQuery] string key, [FromQuery] long exp, [FromQuery] string? sig,
            CancellationToken cancellationToken)
        {
            if (_storage is null)
            {
                return NotFound();
            }

            if (!_storage.IsValidPartSignature(uploadId, key, partNumber, exp, sig))
            {
                return StatusCode(StatusCodes.Status403Forbidden, "This upload link has expired or is invalid.");
            }

            var eTag = await _storage.SavePartAsync(uploadId, key, partNumber, Request.Body, cancellationToken);
            Response.Headers.ETag = eTag;
            return Ok();
        }

        [HttpGet("files/{key}")]
        public IActionResult GetFile(string key, [FromQuery] long exp, [FromQuery] string? ct, [FromQuery] string? sig)
        {
            if (_storage is null)
            {
                return NotFound();
            }

            if (!_storage.IsValidReadSignature(key, exp, ct, sig))
            {
                return StatusCode(StatusCodes.Status403Forbidden, "This link has expired or is invalid.");
            }

            var path = _storage.ExistingFilePath(key);
            if (path is null)
            {
                return NotFound();
            }

            var contentType = !string.IsNullOrWhiteSpace(ct)
                ? ct
                : ContentTypes.TryGetContentType(key, out var guessed) ? guessed : "application/octet-stream";
            // Inline (plays in the browser rather than downloading), with Range support so a long
            // recording or video can be seeked without fetching it all.
            Response.Headers.ContentDisposition = "inline";
            return PhysicalFile(path, contentType, enableRangeProcessing: true);
        }
    }
}
