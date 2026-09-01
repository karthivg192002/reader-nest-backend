using iucs.readernest.api.Auth;
using iucs.readernest.application.Dto.Common;
using iucs.readernest.application.Dto.Enrollment;
using iucs.readernest.application.Dto.Users;
using iucs.readernest.application.Services;
using iucs.readernest.domain.Entities.Users;
using iucs.readernest.domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

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

        /// <summary>
        /// Teacher options (name/department only, nothing sensitive) for assignment dropdowns.
        /// [Authorize]-only, not UserManagement-gated -- Batches, Calendar, Availability and
        /// Demo Scheduling all populate a teacher picker from this and only need
        /// CourseBatchManagement/SessionCalendarManagement/Admission respectively, not
        /// UserManagement. Confirmed live: a role granted only those modules got a 403 here
        /// on pages that have nothing to do with user management.
        /// </summary>
        [HttpGet("teachers")]
        [Authorize]
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

        /// <summary>Relationship Manager's special enrolment notes on a child's profile.</summary>
        [HttpPut("students/{childId:guid}/notes")]
        [HasPermission(PermissionModule.UserManagement, PermissionAction.Edit)]
        public async Task<IActionResult> UpdateStudentNotes(
            Guid childId,
            UpdateChildNotesRequest request,
            CancellationToken cancellationToken)
        {
            await _enrollmentService.UpdateChildNotesAsync(childId, request.Notes, cancellationToken);
            return NoContent();
        }

        /// <summary>The signed-in user's own account (any role) — for the Profile screen.</summary>
        [HttpGet("me")]
        [Microsoft.AspNetCore.Authorization.Authorize]
        public async Task<ActionResult<UserDto>> Me(CancellationToken cancellationToken)
        {
            var userId = Guid.Parse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)!.Value);
            return Ok(await _userService.GetAsync(userId, cancellationToken));
        }

        /// <summary>Self-service update of the signed-in user's own name, phone and timezone.</summary>
        [HttpPut("me")]
        [Microsoft.AspNetCore.Authorization.Authorize]
        public async Task<ActionResult<UserDto>> UpdateMe(UpdateUserRequest request, CancellationToken cancellationToken)
        {
            var userId = Guid.Parse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)!.Value);
            return Ok(await _userService.UpdateAsync(userId, request, cancellationToken));
        }

        /// <summary>
        /// The signed-in member's permanent personal meeting room (Zoom-style): one
        /// stable room id, startable any time. Minted on first request. Also returns a
        /// signed join token the same way GET /api/sessions/{id}/jitsi-join does for a class
        /// session — without one, a token-enforcing Jitsi deployment refuses the join outright
        /// (see JitsiLinkBuilder.BuildJoinUrl's own doc comment), which is exactly what three
        /// separate frontend call sites building a bare room URL from just this endpoint's
        /// roomId were exposed to.
        /// </summary>
        [HttpGet("me/meeting-room")]
        [Microsoft.AspNetCore.Authorization.Authorize]
        public async Task<ActionResult<object>> MyMeetingRoom(
            [FromServices] iucs.readernest.domain.Repository.IUnitOfWork unitOfWork,
            [FromServices] application.Common.Interfaces.IJitsiTokenService jitsiTokenService,
            CancellationToken cancellationToken)
        {
            var userId = Guid.Parse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)!.Value);
            var user = await unitOfWork.Repository<domain.Entities.Users.User>()
                .FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);
            if (user is null)
            {
                return NotFound();
            }

            if (string.IsNullOrEmpty(user.PersonalMeetingRoomId))
            {
                user.PersonalMeetingRoomId = $"trn-personal-{Guid.NewGuid():N}";
                unitOfWork.Repository<domain.Entities.Users.User>().Update(user);
                await unitOfWork.SaveChangesAsync(cancellationToken);
            }

            var jitsiConfigJson = await unitOfWork.Repository<domain.Entities.Integrations.Integration>().Query()
                .Where(i => i.Key == "jitsi")
                .Select(i => i.ConfigJson)
                .FirstOrDefaultAsync(cancellationToken);
            var domain = application.Helper.JitsiLinkBuilder.ResolveDomain(jitsiConfigJson);
            // Always moderator: this is the member's own permanent room, nobody else's.
            var token = jitsiTokenService.CreateToken(
                domain, jitsiConfigJson, user.PersonalMeetingRoomId, $"{user.FirstName} {user.LastName}".Trim(),
                user.Email, moderator: true, DateTime.UtcNow.AddHours(6));

            return Ok(new { roomId = user.PersonalMeetingRoomId, domain, token });
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

        /// <summary>
        /// Converts the account to a different base type (Parent/Teacher/Admission Team/Sub Admin),
        /// swapping the type-specific profile. Refuses when the account already has real
        /// operational history (a parent with children, a teacher with class sessions).
        /// </summary>
        [HttpPut("{id:guid}/role")]
        [HasPermission(PermissionModule.UserManagement, PermissionAction.Edit)]
        public async Task<ActionResult<UserDto>> ChangeRole(Guid id, ChangeUserRoleRequest request, CancellationToken cancellationToken)
        {
            return Ok(await _userService.ChangeRoleAsync(id, request.Role, cancellationToken));
        }

        /// <summary>Soft-deletes the account (excluded from all future queries; email becomes reusable).</summary>
        [HttpDelete("{id:guid}")]
        [HasPermission(PermissionModule.UserManagement, PermissionAction.Delete)]
        public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
        {
            var currentUserId = Guid.Parse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)!.Value);
            await _userService.DeleteAsync(id, currentUserId, cancellationToken);
            return NoContent();
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

        /// <summary>
        /// Regenerates the account's PIN and returns it directly instead of sending it —
        /// for when the admin wants to relay it themselves (a call, in person) rather than
        /// rely on a delivery channel reaching this person right now.
        /// </summary>
        [HttpPost("{id:guid}/reset-pin")]
        [HasPermission(PermissionModule.UserManagement, PermissionAction.Edit)]
        public async Task<ActionResult<ResetPinResultDto>> ResetPin(Guid id, CancellationToken cancellationToken)
        {
            var temporaryPin = await _userService.ResetPinAsync(id, cancellationToken);
            return Ok(new ResetPinResultDto { TemporaryPin = temporaryPin });
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
            var currentUserId = Guid.Parse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)!.Value);
            await _userService.SetPermissionsAsync(id, currentUserId, permissions, cancellationToken: cancellationToken);
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
            if (NonSubAdminPresetNames.Names.Contains(preset.Trim()))
            {
                return BadRequest(new Microsoft.AspNetCore.Mvc.ProblemDetails
                {
                    Status = 400,
                    Title = "Bad Request",
                    Detail = $"'{preset}' is a fixed-portal system role, not a Sub Admin preset, and can't be assigned to a Sub Admin account.",
                });
            }

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

            var currentUserId = Guid.Parse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)!.Value);
            await _userService.SetPermissionsAsync(id, currentUserId, role.Permissions, role.Id, cancellationToken);
            return NoContent();
        }

        private const long MaxBulkImportBytes = 5 * 1024 * 1024;

        /// <summary>Bulk-create Parent or Teacher accounts from an uploaded .csv/.xlsx.
        /// Columns: Email, FirstName, LastName, Phone, DepartmentName (Teacher rows only).</summary>
        [HttpPost("bulk-import")]
        [HasPermission(PermissionModule.UserManagement, PermissionAction.Create)]
        [RequestSizeLimit(MaxBulkImportBytes)]
        public async Task<ActionResult<BulkImportResult>> BulkImport(
            IFormFile file, [FromForm] UserRole role, CancellationToken cancellationToken)
        {
            if (file.Length == 0)
            {
                return BadRequest("The uploaded file is empty.");
            }

            await using var stream = file.OpenReadStream();
            return Ok(await _userService.BulkImportAsync(stream, file.FileName, role, cancellationToken));
        }

        [HttpGet("export")]
        [HasPermission(PermissionModule.UserManagement, PermissionAction.View)]
        public async Task<IActionResult> Export([FromQuery] UserRole? role, CancellationToken cancellationToken)
        {
            var csv = await _userService.ExportCsvAsync(role, cancellationToken);
            var suffix = role?.ToString().ToLowerInvariant() ?? "all";
            return File(System.Text.Encoding.UTF8.GetBytes(csv), "text/csv", $"users-{suffix}-{DateTime.UtcNow:yyyyMMdd}.csv");
        }

        /// <summary>Bulk-create Students (Child records) from an uploaded .csv/.xlsx. Each row's
        /// ParentEmail must match an existing Parent account. Columns: ParentEmail,
        /// StudentFullName, DateOfBirth (YYYY-MM-DD, optional), AcademicLevel (optional).</summary>
        [HttpPost("students/bulk-import")]
        [HasPermission(PermissionModule.UserManagement, PermissionAction.Create)]
        [RequestSizeLimit(MaxBulkImportBytes)]
        public async Task<ActionResult<BulkImportResult>> BulkImportStudents(IFormFile file, CancellationToken cancellationToken)
        {
            if (file.Length == 0)
            {
                return BadRequest("The uploaded file is empty.");
            }

            await using var stream = file.OpenReadStream();
            return Ok(await _enrollmentService.BulkImportStudentsAsync(stream, file.FileName, cancellationToken));
        }

        [HttpGet("students/export")]
        [HasPermission(PermissionModule.UserManagement, PermissionAction.View)]
        public async Task<IActionResult> ExportStudents(CancellationToken cancellationToken)
        {
            var csv = await _enrollmentService.ExportStudentsCsvAsync(cancellationToken);
            return File(System.Text.Encoding.UTF8.GetBytes(csv), "text/csv", $"students-{DateTime.UtcNow:yyyyMMdd}.csv");
        }
    }
}
