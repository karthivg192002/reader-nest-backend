using iucs.readernest.api.Auth;
using iucs.readernest.application.Dto.Common;
using iucs.readernest.application.Dto.Enrollment;
using iucs.readernest.application.Dto.Users;
using iucs.readernest.application.Services;
using iucs.readernest.domain.Entities.Users;
using iucs.readernest.domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
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

        // A personal meeting room is meant to be copied once (MyMeetingRoomShortLink's own doc
        // comment: "Never expires... reused indefinitely") and reused for weeks -- shared over
        // WhatsApp/email hours or days before it's actually opened, exactly like a Google Meet
        // personal room link. The JWT minted for it must not become a silent expiry of its own
        // regardless of the gap between sharing and joining: MeetingRoomJoin (the anonymous
        // redirect target every shared link ultimately lands on) re-mints on every hit, so this
        // is meant to be scoped to "now" at actual join time, not share time -- but a messaging
        // app's own link-preview prefetch (or an in-app browser that resolves a link once and
        // reuses that resolution) can make the *effective* mint moment earlier than the real
        // click, so a short window here silently becomes a "link expired" report hours later.
        // Confirmed live (2026-09-14): shared at 12pm for a 6pm join -- the exact AddHours(6)
        // this replaces -- reported as "expired" on arrival. Mirrors the same "still bounded,
        // not literally forever" 5-year outer expiry SessionService.
        // CreateGuestLinkForParticipantAsync uses for demo join links (its own GetGuestJoinAsync
        // skips the usual per-session expiry check for that link shape entirely) -- the identical
        // fix for the identical bug class.
        private static readonly TimeSpan PersonalRoomTokenLifetime = TimeSpan.FromDays(365 * 5);

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

        /// <summary>Removes a mistaken/test child record. Refused if it still has an unpaid invoice;
        /// refused for an active batch enrolment too unless withdrawFromBatches withdraws it first.</summary>
        [HttpDelete("students/{childId:guid}")]
        [HasPermission(PermissionModule.UserManagement, PermissionAction.Delete)]
        public async Task<IActionResult> RemoveStudent(
            Guid childId,
            [FromQuery] bool withdrawFromBatches,
            CancellationToken cancellationToken)
        {
            await _enrollmentService.RemoveChildAsync(childId, withdrawFromBatches, cancellationToken);
            return NoContent();
        }

        /// <summary>
        /// Everything a hard delete of this Student would permanently remove (enrollments,
        /// invoices/payments/refunds, attendance, engagement, progress reports, awards, fee
        /// suspensions) — read-only, powers the "are you sure" confirmation popup before
        /// <see cref="HardDeleteStudent"/> actually runs. Unlike <see cref="RemoveStudent"/> above,
        /// this ignores the unpaid-invoice/active-enrolment guards entirely — it's a different,
        /// deliberately more permissive action.
        /// </summary>
        [HttpGet("students/{childId:guid}/hard-delete-preview")]
        [HasPermission(PermissionModule.UserManagement, PermissionAction.Delete)]
        public async Task<ActionResult<application.Dto.Users.DataDeletionPreviewDto>> PreviewHardDeleteStudent(
            Guid childId,
            [FromServices] IUserDeletionService deletionService,
            CancellationToken cancellationToken)
        {
            return Ok(await deletionService.PreviewStudentDeletionAsync(childId, cancellationToken));
        }

        /// <summary>
        /// Permanently deletes this Student and every row that's actually theirs — see
        /// <see cref="IUserDeletionService.DeleteStudentAsync"/>. Every removed row is snapshotted
        /// to DataDeletionLog first (deleted-by/deleted-at + a full JSON copy), so the data still
        /// exists there for future reference even though it's gone from its live table. Frontend
        /// must call <see cref="PreviewHardDeleteStudent"/> first and get explicit confirmation —
        /// there is no undo once this returns.
        /// </summary>
        [HttpDelete("students/{childId:guid}/hard-delete")]
        [HasPermission(PermissionModule.UserManagement, PermissionAction.Delete)]
        public async Task<IActionResult> HardDeleteStudent(
            Guid childId,
            [FromServices] IUserDeletionService deletionService,
            CancellationToken cancellationToken)
        {
            var deletedByUserId = Guid.Parse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)!.Value);
            await deletionService.DeleteStudentAsync(childId, deletedByUserId, cancellationToken);
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
                user.Email, moderator: true, DateTime.UtcNow.Add(PersonalRoomTokenLifetime));

            return Ok(new { roomId = user.PersonalMeetingRoomId, domain, token, ownerId = user.Id });
        }

        /// <summary>
        /// A short, shareable link to the same room MyMeetingRoom builds -- the long form
        /// (domain/room#jwt=&lt;huge signed token&gt;) reads as broken/suspicious pasted into
        /// WhatsApp or email. Points at the in-app <c>/join/personal/{id}</c> guest page (backed
        /// by <see cref="MeetingRoomGuestJoin"/>) rather than straight at Jitsi, so a guest who
        /// opens it lands in the same branded, interactive classroom (whiteboard/quiz/roster) the
        /// owner gets, not a bare Jitsi tab -- see MeetingRoomGuestJoin's own doc comment for why.
        /// This mints a real /m/{slug} row (the same short-link mechanism
        /// DemoBookingsController.GetJoinLink uses); whatever resolves the slug lands on that SPA
        /// route, which itself re-resolves a fresh join fresh on every open. Never expires,
        /// matching the room's own "reused indefinitely" design.
        /// </summary>
        [HttpGet("me/meeting-room/short-link")]
        [Microsoft.AspNetCore.Authorization.Authorize]
        public async Task<ActionResult<object>> MyMeetingRoomShortLink(
            [FromServices] iucs.readernest.domain.Repository.IUnitOfWork unitOfWork,
            [FromServices] IShortLinkService shortLinks,
            [FromServices] Microsoft.Extensions.Configuration.IConfiguration configuration,
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

            var apiBaseUrl = $"{Request.Scheme}://{Request.Host}";
            // The site this request came from (when it is an origin CORS already trusts) rather than the
            // one fixed Frontend:BaseUrl -- on UAT that setting still names production, so the invite
            // link used to open the wrong site.
            var frontendBaseUrl = iucs.readernest.application.Helper.FrontendUrl.Resolve(
                configuration["Frontend:BaseUrl"],
                Request.Headers.Origin.ToString(),
                configuration.GetSection("Cors:AllowedOrigins").Get<string[]>());
            var stableUrl = $"{frontendBaseUrl}/join/personal/{userId}";
            var slug = await shortLinks.CreateAsync(stableUrl, DateTime.UtcNow.AddYears(10), userId, cancellationToken);
            return Ok(new { url = $"{apiBaseUrl}/m/{slug}" });
        }

        /// <summary>
        /// Public, anonymous lookup backing the in-app guest-join page (<c>/join/personal/{id}</c>,
        /// PersonalRoomGuestJoin.tsx) that a fresh <see cref="MyMeetingRoomShortLink"/> now points
        /// at -- returns everything that page needs to render the SAME embedded, interactive
        /// classroom (whiteboard/quiz/roster/chat) the owner gets via JitsiLive, instead of the
        /// old behaviour of 302-redirecting straight to a bare Jitsi tab with none of that (see
        /// MeetingRoomJoin below, kept as-is for any already-shared link of the old shape).
        /// Deliberately non-moderator on the Jitsi side (moderator: false) -- this is the invite
        /// handed to whoever the owner shares the link with, not the owner's own access. The
        /// ClassroomHub token is the SAME CreateGuestClassroomHubToken shape a class Guest Link
        /// already uses (see SessionService.GetGuestJoinAsync), just scoped to the owner's own
        /// account id instead of a real ClassSession id -- ClassroomHub.JoinSession's "guest-
        /// classroom" branch only ever compares that claim against the room being joined, so it
        /// works identically here with no hub changes needed beyond the owner's own bypass.
        /// </summary>
        [HttpGet("{id:guid}/meeting-room/guest-join")]
        [AllowAnonymous]
        [EnableRateLimiting("demo-join")]
        public async Task<ActionResult<object>> MeetingRoomGuestJoin(
            Guid id,
            [FromServices] iucs.readernest.domain.Repository.IUnitOfWork unitOfWork,
            [FromServices] application.Common.Interfaces.IJitsiTokenService jitsiTokenService,
            [FromServices] application.Common.Interfaces.ITokenService tokenService,
            CancellationToken cancellationToken)
        {
            var user = await unitOfWork.Repository<domain.Entities.Users.User>()
                .FirstOrDefaultAsync(u => u.Id == id, cancellationToken);
            if (user is null || string.IsNullOrEmpty(user.PersonalMeetingRoomId))
            {
                return NotFound("This room no longer exists.");
            }

            var jitsiConfigJson = await unitOfWork.Repository<domain.Entities.Integrations.Integration>().Query()
                .Where(i => i.Key == "jitsi")
                .Select(i => i.ConfigJson)
                .FirstOrDefaultAsync(cancellationToken);
            var domain = application.Helper.JitsiLinkBuilder.ResolveDomain(jitsiConfigJson);
            var expiresAtUtc = DateTime.UtcNow.Add(PersonalRoomTokenLifetime);
            var token = jitsiTokenService.CreateToken(
                domain, jitsiConfigJson, user.PersonalMeetingRoomId, "Guest",
                participantEmail: null, moderator: false, expiresAtUtc);
            // Room-owner id doubles as this room's ClassroomHub "sessionId" -- see
            // PersonalMeetingRoom.tsx and ClassroomHub.JoinSession's isPersonalRoomOwner check.
            var hubToken = tokenService.CreateGuestClassroomHubToken(user.Id, childId: null, "Guest", expiresAtUtc);

            return Ok(new
            {
                room = user.PersonalMeetingRoomId,
                domain,
                token,
                hubToken = hubToken.AccessToken,
                ownerId = user.Id,
                ownerName = $"{user.FirstName} {user.LastName}".Trim(),
            });
        }

        /// <summary>
        /// Public, anonymous redirect an OLD (pre-in-app-guest-page) guest invite link points at
        /// -- resolves this room's live Jitsi URL fresh on every click (a brand new
        /// PersonalRoomTokenLifetime-lived token; never a stale baked-in one) and 302s straight
        /// there, exactly like DemoBookingsController.Join does for a parent's demo link. Kept
        /// only for backward compatibility with a link minted before MyMeetingRoomShortLink
        /// started pointing at MeetingRoomGuestJoin's in-app page instead -- any link minted from
        /// here on gets the richer in-app join. Deliberately non-moderator, same reasoning as
        /// MeetingRoomGuestJoin above. Never expires by design -- see the remarks on
        /// MyMeetingRoomShortLink above and on PersonalRoomTokenLifetime itself.
        /// </summary>
        [HttpGet("{id:guid}/meeting-room/join")]
        [AllowAnonymous]
        [EnableRateLimiting("demo-join")]
        public async Task<IActionResult> MeetingRoomJoin(
            Guid id,
            [FromServices] iucs.readernest.domain.Repository.IUnitOfWork unitOfWork,
            [FromServices] application.Common.Interfaces.IJitsiTokenService jitsiTokenService,
            CancellationToken cancellationToken)
        {
            // A share channel's own link-preview bot, or an intermediary proxy, has no business
            // caching this redirect -- see the matching comment on GET /m/{slug} in Program.cs.
            Response.Headers.CacheControl = "no-store, no-cache, must-revalidate";
            Response.Headers.Pragma = "no-cache";

            var user = await unitOfWork.Repository<domain.Entities.Users.User>()
                .FirstOrDefaultAsync(u => u.Id == id, cancellationToken);
            if (user is null || string.IsNullOrEmpty(user.PersonalMeetingRoomId))
            {
                return NotFound("This room no longer exists.");
            }

            var jitsiConfigJson = await unitOfWork.Repository<domain.Entities.Integrations.Integration>().Query()
                .Where(i => i.Key == "jitsi")
                .Select(i => i.ConfigJson)
                .FirstOrDefaultAsync(cancellationToken);
            var domain = application.Helper.JitsiLinkBuilder.ResolveDomain(jitsiConfigJson);
            var token = jitsiTokenService.CreateToken(
                domain, jitsiConfigJson, user.PersonalMeetingRoomId, "Guest",
                participantEmail: null, moderator: false, DateTime.UtcNow.Add(PersonalRoomTokenLifetime));
            var targetUrl = application.Helper.JitsiLinkBuilder.BuildJoinUrl(user.PersonalMeetingRoomId, jitsiConfigJson, token)!;

            return Redirect(targetUrl);
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

        /// <summary>
        /// Everything a hard delete of this Parent (and every one of their children) would
        /// permanently remove — read-only, powers the "are you sure" confirmation popup before
        /// <see cref="HardDeleteParent"/> actually runs. Only ever meaningful for a Parent-role
        /// account; see <see cref="IUserDeletionService"/>'s own doc comment for why Teacher/
        /// Admin/Sub Admin accounts don't get this option at all.
        /// </summary>
        [HttpGet("{id:guid}/hard-delete-preview")]
        [HasPermission(PermissionModule.UserManagement, PermissionAction.Delete)]
        public async Task<ActionResult<application.Dto.Users.DataDeletionPreviewDto>> PreviewHardDeleteParent(
            Guid id,
            [FromServices] IUserDeletionService deletionService,
            CancellationToken cancellationToken)
        {
            return Ok(await deletionService.PreviewParentDeletionAsync(id, cancellationToken));
        }

        /// <summary>
        /// Permanently deletes this Parent account, every one of their children, and every row
        /// that's actually theirs — see <see cref="IUserDeletionService.DeleteParentAsync"/>.
        /// Financial records (Invoice/PaymentTransaction/Refund) ARE included, deliberately —
        /// this is a stricter action than <see cref="Delete"/>'s soft delete above, not a
        /// variant of it. Every removed row is snapshotted to DataDeletionLog first (deleted-by/
        /// deleted-at + a full JSON copy). Frontend must call
        /// <see cref="PreviewHardDeleteParent"/> first and get explicit confirmation — there is
        /// no undo once this returns.
        /// </summary>
        [HttpDelete("{id:guid}/hard-delete")]
        [HasPermission(PermissionModule.UserManagement, PermissionAction.Delete)]
        public async Task<IActionResult> HardDeleteParent(
            Guid id,
            [FromServices] IUserDeletionService deletionService,
            CancellationToken cancellationToken)
        {
            var deletedByUserId = Guid.Parse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)!.Value);
            await deletionService.DeleteParentAsync(id, deletedByUserId, cancellationToken);
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

        /// <summary>
        /// Shows the PIN the system last issued to this user (decrypted from the PIN vault) without
        /// changing it. Admin role only -- deliberately stricter than Reset PIN, since this reveals a
        /// credential rather than replacing one -- and every call is audit-logged.
        /// </summary>
        [HttpGet("{id:guid}/pin")]
        [Authorize(Roles = nameof(UserRole.Admin))]
        public async Task<ActionResult<RevealedPinDto>> RevealPin(Guid id, CancellationToken cancellationToken)
        {
            var pin = await _userService.RevealPinAsync(id, cancellationToken);
            Response.Headers.CacheControl = "no-store";
            return Ok(new RevealedPinDto { Pin = pin });
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
