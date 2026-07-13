using iucs.readernest.api.Auth;
using iucs.readernest.application.Common;
using iucs.readernest.application.Dto.Common;
using iucs.readernest.application.Dto.Users;
using iucs.readernest.application.Services;
using iucs.readernest.domain.Enums;
using Microsoft.AspNetCore.Mvc;

namespace iucs.readernest.api.Controllers
{
    [ApiController]
    [Route("api/users")]
    public class UsersController : ControllerBase
    {
        private readonly IUserService _userService;
        private readonly IRoleService _roleService;

        public UsersController(IUserService userService, IRoleService roleService)
        {
            _userService = userService;
            _roleService = roleService;
        }

        [HttpGet]
        [HasPermission(PermissionModule.UserManagement, PermissionAction.View)]
        public async Task<ActionResult<PagedResult<UserDto>>> List(
            [FromQuery] UserRole? role,
            [FromQuery] string? search,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 20,
            CancellationToken cancellationToken = default)
        {
            return Ok(await _userService.ListAsync(role, search, page, pageSize, cancellationToken));
        }

        /// <summary>Teacher options for assignment dropdowns; visible to any module that schedules.</summary>
        [HttpGet("teachers")]
        [HasPermission(PermissionModule.UserManagement, PermissionAction.View)]
        public async Task<ActionResult<IReadOnlyList<TeacherOptionDto>>> ListTeachers(CancellationToken cancellationToken)
        {
            return Ok(await _userService.ListTeachersAsync(cancellationToken));
        }

        [HttpGet("{id:guid}")]
        [HasPermission(PermissionModule.UserManagement, PermissionAction.View)]
        public async Task<ActionResult<UserDto>> Get(Guid id, CancellationToken cancellationToken)
        {
            return Ok(await _userService.GetAsync(id, cancellationToken));
        }

        [HttpPost]
        [HasPermission(PermissionModule.UserManagement, PermissionAction.Create)]
        public async Task<ActionResult<UserDto>> Create(CreateUserRequest request, CancellationToken cancellationToken)
        {
            var user = await _userService.CreateAsync(request, cancellationToken);
            return CreatedAtAction(nameof(Get), new { id = user.Id }, user);
        }

        [HttpPut("{id:guid}")]
        [HasPermission(PermissionModule.UserManagement, PermissionAction.Edit)]
        public async Task<ActionResult<UserDto>> Update(Guid id, UpdateUserRequest request, CancellationToken cancellationToken)
        {
            return Ok(await _userService.UpdateAsync(id, request, cancellationToken));
        }

        [HttpPut("{id:guid}/status")]
        [HasPermission(PermissionModule.UserManagement, PermissionAction.Edit)]
        public async Task<ActionResult<UserDto>> SetStatus(
            Guid id,
            UpdateUserStatusRequest request,
            CancellationToken cancellationToken)
        {
            return Ok(await _userService.SetStatusAsync(id, request.Status, cancellationToken));
        }

        [HttpGet("{id:guid}/permissions")]
        [HasPermission(PermissionModule.UserManagement, PermissionAction.View)]
        public async Task<ActionResult<IReadOnlyList<PermissionDto>>> GetPermissions(Guid id, CancellationToken cancellationToken)
        {
            return Ok(await _userService.GetPermissionsAsync(id, cancellationToken));
        }

        [HttpPut("{id:guid}/permissions")]
        [HasPermission(PermissionModule.UserManagement, PermissionAction.Edit)]
        public async Task<IActionResult> SetPermissions(
            Guid id,
            List<PermissionDto> permissions,
            CancellationToken cancellationToken)
        {
            await _userService.SetPermissionsAsync(id, permissions, cancellationToken);
            return NoContent();
        }

        /// <summary>Named Sub Admin presets, maintained in the DB roles table (seeded with Academic Coordinator, Management).</summary>
        [HttpGet("permission-presets")]
        [HasPermission(PermissionModule.UserManagement, PermissionAction.View)]
        public async Task<ActionResult<IReadOnlyList<string>>> ListPermissionPresets(CancellationToken cancellationToken)
        {
            var roles = await _roleService.ListAsync(cancellationToken);
            return Ok(roles.Select(r => r.Name).ToList());
        }

        /// <summary>Replaces the user's grants with the DB role's matrix.</summary>
        [HttpPut("{id:guid}/permissions/preset/{preset}")]
        [HasPermission(PermissionModule.UserManagement, PermissionAction.Edit)]
        public async Task<IActionResult> ApplyPermissionPreset(Guid id, string preset, CancellationToken cancellationToken)
        {
            var permissions = await _roleService.ResolvePermissionsAsync(preset, cancellationToken);
            if (permissions is null)
            {
                var roles = await _roleService.ListAsync(cancellationToken);
                return NotFound(new Microsoft.AspNetCore.Mvc.ProblemDetails
                {
                    Status = 404,
                    Title = "Not Found",
                    Detail = $"Unknown permission preset '{preset}'. Available: {string.Join(", ", roles.Select(r => r.Name))}.",
                });
            }

            await _userService.SetPermissionsAsync(id, permissions, cancellationToken);
            return NoContent();
        }
    }
}
