using System.Security.Claims;
using iucs.readernest.api.Auth;
using iucs.readernest.application.Dto.Academics;
using iucs.readernest.application.Dto.Sessions;
using iucs.readernest.application.Services;
using iucs.readernest.domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace iucs.readernest.api.Controllers
{
    [ApiController]
    [Route("api/sessions")]
    public class SessionsController : ControllerBase
    {
        private readonly ISessionService _sessionService;

        public SessionsController(ISessionService sessionService)
        {
            _sessionService = sessionService;
        }

        // Staff console only: Teacher and Parent also carry SessionCalendarManagement:View
        // (they need it for their own scoped routes — /mine and the parent portal schedule),
        // so HasPermission alone would hand either of them the whole institution's calendar,
        // every teacher's classes included. The role check is what actually scopes this.
        [HttpGet]
        [Authorize(Roles = $"{nameof(UserRole.Admin)},{nameof(UserRole.SubAdmin)},{nameof(UserRole.AdmissionTeam)}")]
        [HasPermission(PermissionModule.SessionCalendarManagement, PermissionAction.View)]
        public async Task<ActionResult<IReadOnlyList<ClassSessionDto>>> List(
            [FromQuery] DateTime fromUtc,
            [FromQuery] DateTime toUtc,
            [FromQuery] Guid? teacherProfileId,
            [FromQuery] Guid? batchId,
            CancellationToken cancellationToken)
        {
            return Ok(await _sessionService.ListAsync(fromUtc, toUtc, teacherProfileId, batchId, cancellationToken));
        }

        /// <summary>Teacher dashboard agenda: the caller's own sessions.</summary>
        [HttpGet("mine")]
        [Authorize(Roles = nameof(UserRole.Teacher))]
        public async Task<ActionResult<IReadOnlyList<ClassSessionDto>>> Mine(
            [FromQuery] DateTime fromUtc,
            [FromQuery] DateTime toUtc,
            CancellationToken cancellationToken)
        {
            var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            return Ok(await _sessionService.ListForTeacherUserAsync(userId, fromUtc, toUtc, cancellationToken));
        }

        /// <summary>Any session by id — staff-scoped for the same reason as the list above.</summary>
        [HttpGet("{id:guid}")]
        [Authorize(Roles = $"{nameof(UserRole.Admin)},{nameof(UserRole.SubAdmin)},{nameof(UserRole.AdmissionTeam)}")]
        [HasPermission(PermissionModule.SessionCalendarManagement, PermissionAction.View)]
        public async Task<ActionResult<ClassSessionDto>> Get(Guid id, CancellationToken cancellationToken)
        {
            return Ok(await _sessionService.GetAsync(id, cancellationToken));
        }

        /// <summary>
        /// The room + (once configured) a signed, session-scoped join token for the live
        /// classroom. Authorized the same way as the ClassroomHub's JoinSession — Admin, the
        /// assigned teacher, or a parent with a child enrolled in the session's batch — so a
        /// forwarded/leaked room name alone is never enough to join once the Jitsi deployment
        /// enforces token verification.
        /// </summary>
        [HttpGet("{id:guid}/jitsi-join")]
        [Authorize]
        public async Task<ActionResult<JitsiJoinDto>> GetJitsiJoin(Guid id, CancellationToken cancellationToken)
        {
            var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            return Ok(await _sessionService.GetJitsiJoinAsync(id, userId, cancellationToken));
        }

        /// <summary>Non-secret Jitsi settings (domain, auto-record) for whoever is about to join a live class.</summary>
        [HttpGet("classroom-settings")]
        [Authorize]
        public async Task<ActionResult<ClassroomSettingsDto>> GetClassroomSettings(CancellationToken cancellationToken)
        {
            return Ok(await _sessionService.GetClassroomSettingsAsync(cancellationToken));
        }

        [HttpPost]
        [HasPermission(PermissionModule.SessionCalendarManagement, PermissionAction.Create)]
        public async Task<ActionResult<ClassSessionDto>> Schedule(ScheduleSessionRequest request, CancellationToken cancellationToken)
        {
            var session = await _sessionService.ScheduleAsync(request, cancellationToken);
            return CreatedAtAction(nameof(Get), new { id = session.Id }, session);
        }

        [HttpPost("{id:guid}/reschedule")]
        [HasPermission(PermissionModule.SessionCalendarManagement, PermissionAction.Edit)]
        public async Task<ActionResult<ClassSessionDto>> Reschedule(
            Guid id,
            RescheduleSessionRequest request,
            CancellationToken cancellationToken)
        {
            return Ok(await _sessionService.RescheduleAsync(id, request, cancellationToken));
        }

        [HttpPost("{id:guid}/cancel")]
        [HasPermission(PermissionModule.SessionCalendarManagement, PermissionAction.Edit)]
        public async Task<ActionResult<ClassSessionDto>> Cancel(
            Guid id,
            CancelSessionRequest request,
            CancellationToken cancellationToken)
        {
            return Ok(await _sessionService.CancelAsync(id, request, cancellationToken));
        }

        /// <summary>
        /// Marks a session completed with an optional class summary;
        /// auto-moves the batch to Dormant when the course finishes.
        /// </summary>
        [HttpPost("{id:guid}/complete")]
        [Authorize(Roles = $"{nameof(UserRole.Admin)},{nameof(UserRole.Teacher)}")]
        public async Task<ActionResult<ClassSessionDto>> Complete(
            Guid id,
            [FromBody(EmptyBodyBehavior = Microsoft.AspNetCore.Mvc.ModelBinding.EmptyBodyBehavior.Allow)] CompleteSessionRequest? request,
            CancellationToken cancellationToken)
        {
            return Ok(await _sessionService.CompleteAsync(id, request, cancellationToken));
        }

        /// <summary>
        /// Marks a teacher/student no-show: the payout impact accrues and a
        /// carried-forward replacement session is returned.
        /// </summary>
        [HttpPost("{id:guid}/no-show")]
        [Authorize(Roles = $"{nameof(UserRole.Admin)},{nameof(UserRole.Teacher)}")]
        public async Task<ActionResult<ClassSessionDto>> MarkNoShow(
            Guid id,
            MarkNoShowRequest request,
            CancellationToken cancellationToken)
        {
            return Ok(await _sessionService.MarkNoShowAsync(id, request, cancellationToken));
        }

        /// <summary>Registers a finished recording; parent visibility expires after 15 days.</summary>
        [HttpPost("{id:guid}/recordings")]
        [Authorize(Roles = $"{nameof(UserRole.Admin)},{nameof(UserRole.Teacher)}")]
        public async Task<ActionResult<SessionRecordingDto>> AddRecording(
            Guid id,
            RegisterRecordingRequest request,
            CancellationToken cancellationToken)
        {
            return Ok(await _sessionService.AddRecordingAsync(id, request, cancellationToken));
        }

        /// <summary>
        /// Machine-to-machine: the Jibri finalize-recording hook on the video server calls this
        /// once a recording finishes, identifying the class by Jitsi room name (it has no
        /// ClassSession id, and no logged-in user to authorize as) — see
        /// docs/JITSI_ARCHITECTURE.md. Deliberately anonymous at the ASP.NET auth layer: the
        /// bearer token in the Authorization header is validated inside the service against the
        /// same appId/appSecret as room-join tokens, which is the actual authorization here.
        /// Returns 204 rather than a recording body when the room isn't a known ClassSession
        /// (personal/demo rooms) — not an error, just nothing to attach.
        /// </summary>
        [HttpPost("recordings/finalize")]
        [AllowAnonymous]
        public async Task<ActionResult<SessionRecordingDto>> FinalizeJibriRecording(
            FinalizeJibriRecordingRequest request,
            CancellationToken cancellationToken)
        {
            string? bearerToken = null;
            var header = Request.Headers.Authorization.ToString();
            if (header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            {
                bearerToken = header["Bearer ".Length..];
            }

            var recording = await _sessionService.FinalizeJibriRecordingAsync(
                request.RoomName, bearerToken, request.StorageUrl, request.DurationSeconds, cancellationToken);
            return recording is null ? NoContent() : Ok(recording);
        }

        /// <summary>
        /// Parents see their own child's recordings only via the scoped parent-portal resources
        /// endpoint. Admin/Teacher pass unconditionally; a Sub Admin (e.g. Coordinator) additionally
        /// needs SessionCalendarManagement:View — the same grant their preset already carries for
        /// calendar work — checked manually here rather than via [HasPermission], which would deny
        /// Teacher (Teacher has no permission claims at all; see AuthService.LoadPermissionClaimsAsync).
        /// </summary>
        [HttpGet("{id:guid}/recordings")]
        [Authorize(Roles = $"{nameof(UserRole.Admin)},{nameof(UserRole.Teacher)},{nameof(UserRole.SubAdmin)}")]
        public async Task<ActionResult<IReadOnlyList<SessionRecordingDto>>> ListRecordings(
            Guid id,
            CancellationToken cancellationToken)
        {
            if (User.IsInRole(nameof(UserRole.SubAdmin)) &&
                !User.HasClaim(JwtTokenService.PermissionClaimType, $"{PermissionModule.SessionCalendarManagement}:{PermissionAction.View}"))
            {
                return Forbid();
            }

            return Ok(await _sessionService.ListRecordingsAsync(id, cancellationToken));
        }

        /// <summary>Deletes a registered recording. Admin only — unregisters the row; the underlying file in storage is left untouched.</summary>
        [HttpDelete("{id:guid}/recordings/{recordingId:guid}")]
        [Authorize(Roles = nameof(UserRole.Admin))]
        public async Task<IActionResult> DeleteRecording(
            Guid id,
            Guid recordingId,
            CancellationToken cancellationToken)
        {
            await _sessionService.DeleteRecordingAsync(id, recordingId, cancellationToken);
            return NoContent();
        }

        /// <summary>Engagement signals from the live classroom (quiz, activity, whiteboard, attention).</summary>
        [HttpPost("{id:guid}/engagement")]
        [Authorize]
        public async Task<IActionResult> RecordEngagement(
            Guid id,
            RecordEngagementRequest request,
            CancellationToken cancellationToken)
        {
            await _sessionService.RecordEngagementAsync(id, request, cancellationToken);
            return NoContent();
        }

        [HttpGet("{id:guid}/engagement")]
        [Authorize(Roles = $"{nameof(UserRole.Admin)},{nameof(UserRole.Teacher)}")]
        public async Task<ActionResult<IReadOnlyList<EngagementSummaryDto>>> EngagementSummary(
            Guid id,
            CancellationToken cancellationToken)
        {
            return Ok(await _sessionService.GetEngagementSummaryAsync(id, cancellationToken));
        }

        /// <summary>Student/teacher attendance capture (join-based; rejoin updates, never duplicates).</summary>
        [HttpPost("{id:guid}/attendance")]
        [Authorize(Roles = $"{nameof(UserRole.Admin)},{nameof(UserRole.Teacher)}")]
        public async Task<ActionResult<IReadOnlyList<SessionAttendanceDto>>> CaptureAttendance(
            Guid id,
            CaptureAttendanceRequest request,
            [FromServices] IAcademicOpsService academicOps,
            CancellationToken cancellationToken)
        {
            return Ok(await academicOps.CaptureAttendanceAsync(id, request, cancellationToken));
        }

        [HttpGet("{id:guid}/attendance")]
        [Authorize(Roles = $"{nameof(UserRole.Admin)},{nameof(UserRole.Teacher)}")]
        public async Task<ActionResult<IReadOnlyList<SessionAttendanceDto>>> ListAttendance(
            Guid id,
            [FromServices] IAcademicOpsService academicOps,
            CancellationToken cancellationToken)
        {
            return Ok(await academicOps.ListAttendanceAsync(id, cancellationToken));
        }

        /// <summary>
        /// Calendar sync: iCalendar feed of scheduled sessions for external calendars.
        /// Staff-scoped like the list it wraps; Teacher/Parent sync their own schedule
        /// through calendar/mine.ics instead.
        /// </summary>
        [HttpGet("calendar.ics")]
        [Authorize(Roles = $"{nameof(UserRole.Admin)},{nameof(UserRole.SubAdmin)},{nameof(UserRole.AdmissionTeam)}")]
        [HasPermission(PermissionModule.SessionCalendarManagement, PermissionAction.View)]
        public async Task<IActionResult> CalendarFeed(
            [FromQuery] Guid? teacherProfileId,
            [FromQuery] Guid? batchId,
            CancellationToken cancellationToken)
        {
            var from = DateTime.UtcNow.AddDays(-30);
            var to = DateTime.UtcNow.AddDays(120);
            var sessions = await _sessionService.ListAsync(from, to, teacherProfileId, batchId, cancellationToken);
            return IcsFile(sessions);
        }

        /// <summary>
        /// Personal calendar-sync URL for the signed-in user. External calendar apps
        /// can't send a JWT, so the feed authenticates with a long-lived token that
        /// is created here on first request.
        /// </summary>
        [HttpGet("calendar/feed-url")]
        [Authorize]
        public async Task<ActionResult<object>> MyCalendarFeedUrl(
            [FromServices] iucs.readernest.domain.Repository.IUnitOfWork unitOfWork,
            CancellationToken cancellationToken)
        {
            var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var user = await unitOfWork.Repository<domain.Entities.Users.User>()
                .FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);
            if (user is null)
            {
                return NotFound();
            }

            // Only Teacher/Parent have a meaningful "personal schedule" to sync — Admin/
            // SubAdmin/AdmissionTeam have no owning batch/session scope, so MyCalendarFeed's
            // role switch would otherwise fall through to an unfiltered org-wide session
            // list for them. Refusing the token here means one can never be issued.
            if (user.Role is not (UserRole.Teacher or UserRole.Parent))
            {
                return BadRequest(new ProblemDetails
                {
                    Status = 400,
                    Title = "Bad Request",
                    Detail = "A personal calendar feed is only available to Teacher and Parent accounts.",
                });
            }

            if (user.CalendarFeedToken is null)
            {
                user.CalendarFeedToken = Guid.NewGuid();
                unitOfWork.Repository<domain.Entities.Users.User>().Update(user);
                await unitOfWork.SaveChangesAsync(cancellationToken);
            }

            return Ok(new { url = $"/api/sessions/calendar/mine.ics?token={user.CalendarFeedToken:N}" });
        }

        /// <summary>Role-scoped personal iCalendar feed: teachers get their classes, parents their children's.</summary>
        [HttpGet("calendar/mine.ics")]
        [AllowAnonymous]
        public async Task<IActionResult> MyCalendarFeed(
            [FromQuery] string token,
            [FromServices] iucs.readernest.domain.Repository.IUnitOfWork unitOfWork,
            [FromServices] IParentPortalService parentPortal,
            CancellationToken cancellationToken)
        {
            if (!Guid.TryParseExact(token, "N", out var feedToken) && !Guid.TryParse(token, out feedToken))
            {
                return Unauthorized();
            }

            var user = await unitOfWork.Repository<domain.Entities.Users.User>()
                .FirstOrDefaultAsync(u => u.CalendarFeedToken == feedToken, cancellationToken);
            if (user is null)
            {
                return Unauthorized();
            }

            var from = DateTime.UtcNow.AddDays(-30);
            var to = DateTime.UtcNow.AddDays(120);
            // No unscoped fallback: Admin/SubAdmin/AdmissionTeam have no owning session
            // scope, and MyCalendarFeedUrl no longer issues them a token — but a token
            // already on a legacy record must never fall through to every session in
            // the institution, so it gets an empty feed instead.
            var sessions = user.Role switch
            {
                UserRole.Teacher => await _sessionService.ListForTeacherUserAsync(user.Id, from, to, cancellationToken),
                UserRole.Parent => await parentPortal.GetScheduleAsync(user.Id, from, to, cancellationToken),
                _ => [],
            };

            return IcsFile(sessions);
        }

        private FileContentResult IcsFile(IReadOnlyList<ClassSessionDto> sessions)
        {
            var builder = new System.Text.StringBuilder();
            builder.AppendLine("BEGIN:VCALENDAR");
            builder.AppendLine("VERSION:2.0");
            builder.AppendLine("PRODID:-//Meet to Manage//Sessions//EN");
            foreach (var session in sessions)
            {
                builder.AppendLine("BEGIN:VEVENT");
                builder.AppendLine($"UID:{session.Id}@reader-nest");
                builder.AppendLine($"DTSTART:{session.ScheduledStartAtUtc:yyyyMMdd'T'HHmmss'Z'}");
                builder.AppendLine($"DTEND:{session.ScheduledEndAtUtc:yyyyMMdd'T'HHmmss'Z'}");
                builder.AppendLine($"SUMMARY:{session.BatchName ?? session.Type.ToString()} — {session.TeacherName}");
                builder.AppendLine($"STATUS:{(session.Status == SessionStatus.Cancelled ? "CANCELLED" : "CONFIRMED")}");
                builder.AppendLine("END:VEVENT");
            }

            builder.AppendLine("END:VCALENDAR");
            return File(System.Text.Encoding.UTF8.GetBytes(builder.ToString()), "text/calendar", "reader-nest-sessions.ics");
        }
    }
}
