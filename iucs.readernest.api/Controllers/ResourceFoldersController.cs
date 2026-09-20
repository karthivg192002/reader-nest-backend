using System.Security.Claims;
using iucs.readernest.api.Auth;
using iucs.readernest.application.Dto.Resources;
using iucs.readernest.application.Services;
using iucs.readernest.domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace iucs.readernest.api.Controllers
{
    /// <summary>
    /// Content and Resources folders and folder sharing. Everything here needs Content Access
    /// Management rights (Admin implicitly; an RM only if their role is granted it), so parents and
    /// teachers can never create folders or decide who sees a folder.
    /// </summary>
    [ApiController]
    [Route("api/resource-folders")]
    [Authorize(Roles = $"{nameof(UserRole.Admin)},{nameof(UserRole.SubAdmin)},{nameof(UserRole.AdmissionTeam)}")]
    public class ResourceFoldersController : ControllerBase
    {
        private readonly IResourceFolderService _folders;

        public ResourceFoldersController(IResourceFolderService folders)
        {
            _folders = folders;
        }

        [HttpGet]
        [HasPermission(PermissionModule.ContentAccessManagement, PermissionAction.View)]
        public async Task<ActionResult<IReadOnlyList<ResourceFolderDto>>> List(CancellationToken cancellationToken)
        {
            return Ok(await _folders.ListAsync(cancellationToken));
        }

        [HttpPost]
        [HasPermission(PermissionModule.ContentAccessManagement, PermissionAction.Create)]
        public async Task<ActionResult<ResourceFolderDto>> Create(CreateResourceFolderRequest request, CancellationToken cancellationToken)
        {
            return Ok(await _folders.CreateAsync(request, cancellationToken));
        }

        [HttpPut("{id:guid}")]
        [HasPermission(PermissionModule.ContentAccessManagement, PermissionAction.Edit)]
        public async Task<ActionResult<ResourceFolderDto>> Update(Guid id, UpdateResourceFolderRequest request, CancellationToken cancellationToken)
        {
            return Ok(await _folders.UpdateAsync(id, request, cancellationToken));
        }

        [HttpDelete("{id:guid}")]
        [HasPermission(PermissionModule.ContentAccessManagement, PermissionAction.Delete)]
        public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
        {
            await _folders.DeleteAsync(id, cancellationToken);
            return NoContent();
        }

        /// <summary>Parents this folder is shared with.</summary>
        [HttpGet("{id:guid}/access")]
        [HasPermission(PermissionModule.ContentAccessManagement, PermissionAction.View)]
        public async Task<ActionResult<IReadOnlyList<ResourceFolderAccessDto>>> Access(Guid id, CancellationToken cancellationToken)
        {
            return Ok(await _folders.ListAccessAsync(id, cancellationToken));
        }

        /// <summary>Share with / unshare from any number of parents in one call.</summary>
        [HttpPut("{id:guid}/access")]
        [HasPermission(PermissionModule.ContentAccessManagement, PermissionAction.Edit)]
        public async Task<ActionResult<IReadOnlyList<ResourceFolderAccessDto>>> SetAccess(
            Guid id, SetResourceFolderAccessRequest request, CancellationToken cancellationToken)
        {
            var actor = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            return Ok(await _folders.SetAccessAsync(id, actor, request, cancellationToken));
        }

        /// <summary>Moves a file into a folder (or back to the top level).</summary>
        [HttpPost("~/api/resources/{resourceId:guid}/move")]
        [HasPermission(PermissionModule.ContentAccessManagement, PermissionAction.Edit)]
        public async Task<ActionResult<ResourceDto>> MoveResource(Guid resourceId, MoveResourceRequest request, CancellationToken cancellationToken)
        {
            return Ok(await _folders.MoveResourceAsync(resourceId, request, cancellationToken));
        }
    }
}
