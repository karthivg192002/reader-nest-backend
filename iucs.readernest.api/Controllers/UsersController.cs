using iucs.readernest.api.Auth;
using iucs.readernest.application.Dto.Common;
using iucs.readernest.application.Dto.Enrollment;
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
        private readonly IEnrollmentService _enrollmentService;

        public UsersController(IUserService userService, IRoleService roleService, IEnrollmentService enrollmentService)
        {
            _userService = userService;
            _roleService = roleService;
            _enrollmentService = enrollmentService;
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

        /// <summary>Students directory: enrolled children with their parent and course, for the Users → Students tab.</summary>
        [HttpGet("students")]
        [HasPermission(PermissionModule.UserManagement, PermissionAction.View)]
        public async Task<ActionResult<IReadOnlyList<StudentDto>>> ListStudents(CancellationToken cancellationToken)
        {
            return Ok(await _enrollmentService.ListAllStudentsAsync(cancellationToken));
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

        /// <summary>
        /// Regenerates the account's temporary password and (re)sends the onboarding
        /// welcome message over Email or WhatsApp — used to get parents/teachers their
        /// first-login credentials. Returns 400 with a reason if delivery fails.
        /// </summary>
        [HttpPost("{id:guid}/resend-credentials")]
        [HasPermission(PermissionModule.UserManagement, PermissionAction.Edit)]
        public async Task<IActionResult> ResendCredentials(
            Guid id,
            ResendCredentialsRequest request,
            CancellationToken cancellationToken)
        {
            await _userService.ResendCredentialsAsync(id, request.Channel, cancellationToken);
            return NoContent();
        }

        /// <summary>Which credential-delivery channels are enabled (Settings → Integrations), so the UI shows only usable Send buttons.</summary>
        [HttpGet("credential-channels")]
        [HasPermission(PermissionModule.UserManagement, PermissionAction.View)]
        public async Task<ActionResult<CredentialChannelsDto>> GetCredentialChannels(CancellationToken cancellationToken)
        {
            return Ok(await _userService.GetCredentialChannelsAsync(cancellationToken));
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
            await _userService.SetPermissionsAsync(id, permissions, cancellationToken: cancellationToken);
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

        /// <summary>
        /// Assigns the named DB role to the user: replaces their grants with its
        /// matrix and records the assignment, which drives their post-login default route.
        /// </summary>
        [HttpPut("{id:guid}/permissions/preset/{preset}")]
        [HasPermission(PermissionModule.UserManagement, PermissionAction.Edit)]
        public async Task<IActionResult> ApplyPermissionPreset(Guid id, string preset, CancellationToken cancellationToken)
        {
            var role = await _roleService.FindByNameAsync(preset, cancellationToken);
            if (role is null)
            {
                var roles = await _roleService.ListAsync(cancellationToken);
                return NotFound(new Microsoft.AspNetCore.Mvc.ProblemDetails
                {
                    Status = 404,
                    Title = "Not Found",
                    Detail = $"Unknown permission preset '{preset}'. Available: {string.Join(", ", roles.Select(r => r.Name))}.",
                });
            }

            await _userService.SetPermissionsAsync(id, role.Permissions, role.Id, cancellationToken);
            return NoContent();
        }
    }
}
