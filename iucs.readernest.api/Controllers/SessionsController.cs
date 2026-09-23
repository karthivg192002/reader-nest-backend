using System.Security.Claims;
using iucs.readernest.api.Auth;
using iucs.readernest.application.Common.Interfaces;
using iucs.readernest.application.Dto.Academics;
using iucs.readernest.application.Dto.Sessions;
using iucs.readernest.application.Services;
using iucs.readernest.domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;

namespace iucs.readernest.api.Controllers
{
    [ApiController]
    [Route("api/sessions")]
    public class SessionsController : ControllerBase
    {
        private const long MaxPresentationUploadBytes = 100 * 1024 * 1024;

        private readonly ISessionService _sessionService;
        private readonly IFileStorage _fileStorage;
        private readonly IAcademicOpsService _academicOpsService;

        public SessionsController(ISessionService sessionService, IFileStorage fileStorage, IAcademicOpsService academicOpsService)
        {
            _sessionService = sessionService;
            _fileStorage = fileStorage;
            _academicOpsService = academicOpsService;
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
            return Ok(await _sessionService.ListAsync(
                AsUtc(fromUtc), AsUtc(toUtc), teacherProfileId, batchId, cancellationToken));
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
            return Ok(await _sessionService.ListForTeacherUserAsync(userId, AsUtc(fromUtc), AsUtc(toUtc), cancellationToken));
        }

        // Model binding parses a bare date/no-offset query value (e.g. "2026-01-01") as
        // Kind=Unspecified, which Npgsql then refuses to compare against a timestamptz
        // column (a 500, not a friendly 400). The frontend always sends a full ISO instant
        // via toISOString(), which binds as Kind=Utc already, so this is a no-op for real
        // traffic and only hardens the endpoint against a malformed/hand-built query string.
        private static DateTime AsUtc(DateTime value) =>
            value.Kind == DateTimeKind.Utc ? value : DateTime.SpecifyKind(value, DateTimeKind.Utc);

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

        /// <summary>
        /// The session's batch roster for the "Copy Guest Link" student picker (RM/Admin/
        /// Coordinator Sessions and Calendar screens). Same permission as viewing the session
        /// itself — deliberately not CourseBatchManagement:View, so an account with only session
        /// access isn't 403'd just for this popup. Also role-restricted same as List/Get above:
        /// Teacher and Parent carry SessionCalendarManagement:View too (for their own scoped
        /// /mine and parent-schedule routes), and HasPermission alone doesn't scope by session
        /// ownership — without the role check, a teacher or parent could pull the guest-link
        /// student picker (and mint a link) for a class that isn't theirs.
        /// </summary>
        [HttpGet("{id:guid}/guest-link/students")]
        [Authorize(Roles = $"{nameof(UserRole.Admin)},{nameof(UserRole.SubAdmin)},{nameof(UserRole.AdmissionTeam)}")]
        [HasPermission(PermissionModule.SessionCalendarManagement, PermissionAction.View)]
        public async Task<ActionResult<IReadOnlyList<GuestLinkStudentDto>>> ListGuestLinkStudents(Guid id, CancellationToken cancellationToken)
        {
            return Ok(await _sessionService.GetGuestLinkStudentsAsync(id, cancellationToken));
        }

        /// <summary>
        /// Mints a shareable Guest Link for this session — pass ChildId to bind it to one
        /// specific enrolled student (auto attendance, skips prejoin), or omit it for a generic
        /// guest link. Same permission and role restriction as
        /// <see cref="ListGuestLinkStudents"/> above (see its own doc comment for why the role
        /// check matters here too, not just HasPermission).
        ///
        /// Confirmed live: the raw "/guest-join?token=&lt;JWT&gt;" URL this used to hand back
        /// directly is 300+ characters of base64 with no spaces — reads as broken/suspicious
        /// pasted into WhatsApp, and (per the incident that found this) a paste/send race in the
        /// messaging app can truncate it mid-token, which then fails server-side with an opaque
        /// error instead of a clean "invalid link". Wraps it in a genuinely short /m/{slug} link
        /// instead, same fix DemoBookingsController.GetJoinLink already applies to demo join
        /// links — the short link's own expiry matches the token's (GuestLinkDto.ExpiresAtUtc),
        /// so it never outlives what it points to.
        /// </summary>
        [HttpPost("{id:guid}/guest-link")]
        [Authorize(Roles = $"{nameof(UserRole.Admin)},{nameof(UserRole.SubAdmin)},{nameof(UserRole.AdmissionTeam)}")]
        [HasPermission(PermissionModule.SessionCalendarManagement, PermissionAction.View)]
        public async Task<ActionResult<object>> CreateGuestLink(
            Guid id,
            CreateGuestLinkRequest request,
            [FromServices] IShortLinkService shortLinks,
            [FromServices] IConfiguration configuration,
            CancellationToken cancellationToken)
        {
            var guestLink = await _sessionService.CreateGuestLinkAsync(id, request.ChildId, cancellationToken);
            var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var frontendBaseUrl = iucs.readernest.application.Helper.FrontendUrl.Resolve(
                configuration["Frontend:BaseUrl"],
                Request.Headers.Origin.ToString(),
                configuration.GetSection("Cors:AllowedOrigins").Get<string[]>());
            var joinUrl = $"{frontendBaseUrl}/guest-join?token={Uri.EscapeDataString(guestLink.Token)}";
            var slug = await shortLinks.CreateAsync(joinUrl, guestLink.ExpiresAtUtc, userId, cancellationToken);
            var apiBaseUrl = $"{Request.Scheme}://{Request.Host}";
            return Ok(new { url = $"{apiBaseUrl}/m/{slug}" });
        }

        /// <summary>
        /// Anonymous landing call the frontend's own "/guest-join" bridge page makes to resolve
        /// a Guest Link token into a live Jitsi join. Deliberately unauthenticated — the
        /// caretaker opening the link never logs in — the opaque, signed token itself (see
        /// JwtTokenService.CreateGuestJoinToken) is the only credential. Marks attendance for the
        /// bound student, when there is one, same split responsibility ClassroomHub.JoinSession
        /// already uses between ISessionService (resolve/join) and IAcademicOpsService
        /// (attendance) — SessionService can't depend on IAcademicOpsService directly, which
        /// already depends back on ISessionService.
        /// </summary>
        [HttpPost("guest-join")]
        [AllowAnonymous]
        public async Task<ActionResult<GuestJoinDto>> GuestJoin(GuestJoinRequest request, CancellationToken cancellationToken)
        {
            var join = await _sessionService.GetGuestJoinAsync(request.Token, cancellationToken);
            if (join.ChildId is Guid childId)
            {
                await _academicOpsService.CaptureGuestJoinAttendanceAsync(join.SessionId, childId, cancellationToken);
            }
            return Ok(join);
        }

        /// <summary>
        /// Machine-to-machine: nginx on the Jitsi server proxies Jibri's own top-level
        /// navigation to /&lt;room&gt; here (see docs/JITSI_ARCHITECTURE.md's recording-observer
        /// section) instead of the bare Jitsi Meet SPA, so the recording captures our whiteboard/
        /// quiz overlay, not just raw video tiles. Deliberately anonymous — Jibri's headless
        /// Chrome has no logged-in user — but the token it hands back only ever admits a
        /// moderator-less participant into one specific, currently-InProgress room, same bounded
        /// trust model as recordings/finalize. Returns 204 when the room has no InProgress
        /// session right now (e.g. a personal room, or a startup race).
        /// </summary>
        /// <summary>
        /// Anonymous by necessity (Jibri's headless Chrome has no logged-in user), which
        /// otherwise means anyone on the internet who learns a personal room id -- it isn't
        /// secret, it appears in copy-link URLs and confirmation emails -- could pull a live
        /// join token for whatever class happens to be running in that room. Actually restricted
        /// via <see cref="IsFromTrustedJibriHost"/>: only requests whose remote IP matches
        /// Jibri:AllowedIps get a token; everyone else gets 403 regardless of the room being
        /// live. Confirmed live-exploitable before this check existed -- a plain unauthenticated
        /// request against an in-progress room returned a real 4-hour Jitsi + hub token.
        /// </summary>
        [HttpGet("recordings/observer-join")]
        [AllowAnonymous]
        public async Task<ActionResult<RecordingObserverJoinDto>> GetRecordingObserverJoin(
            [FromQuery] string room,
            [FromServices] IConfiguration configuration,
            CancellationToken cancellationToken)
        {
            if (!IsFromTrustedJibriHost(configuration))
            {
                return Forbid();
            }

            var join = await _sessionService.GetLiveObserverJoinAsync(room, cancellationToken);
            return join is null ? NoContent() : Ok(join);
        }

        /// <summary>
        /// Jibri:AllowedIps is a CSV of IPs/CIDRs in configuration (the Jitsi server's own
        /// outbound address by default) -- this call's own remote IP, resolved through
        /// UseForwardedHeaders in Program.cs so it reflects the real client rather than an
        /// intermediate reverse proxy, must match one of them. An empty/missing setting fails
        /// closed (denies everyone) rather than open, so a blank config can't silently reopen
        /// this to the whole internet the way [AllowAnonymous] alone did.
        /// </summary>
        private bool IsFromTrustedJibriHost(IConfiguration configuration)
        {
            var remoteIp = HttpContext.Connection.RemoteIpAddress;
            if (remoteIp is null)
            {
                return false;
            }

            var normalizedRemoteIp = remoteIp.IsIPv4MappedToIPv6 ? remoteIp.MapToIPv4() : remoteIp;
            var allowedEntries = (configuration["Jibri:AllowedIps"] ?? string.Empty)
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            foreach (var entry in allowedEntries)
            {
                if (System.Net.IPAddress.TryParse(entry, out var allowedIp)
                    && (allowedIp.IsIPv4MappedToIPv6 ? allowedIp.MapToIPv4() : allowedIp).Equals(normalizedRemoteIp))
                {
                    return true;
                }
            }

            return false;
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

        /// <summary>Edits a completed session's notes after the fact -- teacher feedback: "the
        /// report-writing option is also not visible after the session if we do not complete
        /// the report immediately." Re-emails the updated notes to the batch's parents the same
        /// way completing the class with notes already does.</summary>
        [HttpPut("{id:guid}/summary")]
        [Authorize(Roles = $"{nameof(UserRole.Admin)},{nameof(UserRole.Teacher)}")]
        public async Task<ActionResult<ClassSessionDto>> UpdateSummary(
            Guid id,
            UpdateSessionSummaryRequest request,
            CancellationToken cancellationToken)
        {
            return Ok(await _sessionService.UpdateSummaryAsync(id, request.Summary, cancellationToken));
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

        /// <summary>Admin-wide Recordings page: every registered recording across every class, one
        /// paged query instead of a completed-session list plus a per-session lookup for each one
        /// (confirmed live as that page's actual "Loading recordings..." bottleneck). Admin, plus
        /// a Sub Admin (Coordinator, IT Admin, ...) holding SessionCalendarManagement:View -- the
        /// same grant the per-session list above requires, and the same one the "Recordings" menu
        /// item is gated on. View only: delete stays Admin-only.</summary>
        [HttpGet("recordings")]
        [Authorize(Roles = $"{nameof(UserRole.Admin)},{nameof(UserRole.SubAdmin)}")]
        public async Task<ActionResult<iucs.readernest.application.Dto.Common.PagedResult<RecordingListItemDto>>> ListAllRecordings(
            [FromQuery] int page,
            [FromQuery] int pageSize,
            [FromQuery] DateOnly? date,
            CancellationToken cancellationToken)
        {
            if (User.IsInRole(nameof(UserRole.SubAdmin)) &&
                !User.HasClaim(JwtTokenService.PermissionClaimType, $"{PermissionModule.SessionCalendarManagement}:{PermissionAction.View}"))
            {
                return Forbid();
            }

            return Ok(await _sessionService.ListAllRecordingsAsync(page <= 0 ? 1 : page, pageSize <= 0 ? 20 : pageSize, date, null, cancellationToken));
        }

        /// <summary>A teacher's own Recordings page — same paged query as the admin-wide one
        /// above, scoped to this caller's own classes. TeacherRecordings.tsx had the identical
        /// completed-session-list-plus-per-session-lookup N+1 shape the admin page did; fixed the
        /// same way, just filtered rather than institution-wide.</summary>
        [HttpGet("mine/recordings")]
        [Authorize(Roles = nameof(UserRole.Teacher))]
        public async Task<ActionResult<iucs.readernest.application.Dto.Common.PagedResult<RecordingListItemDto>>> ListMyRecordings(
            [FromQuery] int page,
            [FromQuery] int pageSize,
            [FromQuery] DateOnly? date,
            CancellationToken cancellationToken)
        {
            var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            return Ok(await _sessionService.ListAllRecordingsAsync(page <= 0 ? 1 : page, pageSize <= 0 ? 20 : pageSize, date, userId, cancellationToken));
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

        /// <summary>Uploads (replacing any prior deck) the PDF the teacher wants to present live in
        /// this class — the "present a deck like Google Meet" flow. Only the session's own
        /// assigned teacher or an Admin may do this; enforced in the service, not by role alone,
        /// since a Teacher role check here wouldn't stop one teacher uploading into another's class.</summary>
        [HttpPost("{id:guid}/presentation")]
        [Authorize]
        [RequestSizeLimit(MaxPresentationUploadBytes)]
        public async Task<ActionResult<SessionPresentationDto>> UploadPresentation(
            Guid id,
            IFormFile file,
            CancellationToken cancellationToken)
        {
            if (file.Length == 0)
            {
                return BadRequest(new ProblemDetails { Status = 400, Title = "Bad Request", Detail = "The uploaded file is empty." });
            }
            var isPdf = string.Equals(Path.GetExtension(file.FileName), ".pdf", StringComparison.OrdinalIgnoreCase)
                || string.Equals(file.ContentType, "application/pdf", StringComparison.OrdinalIgnoreCase);
            if (!isPdf)
            {
                return BadRequest(new ProblemDetails { Status = 400, Title = "Bad Request", Detail = "Only PDF decks are supported — export your slides to PDF first." });
            }

            await using var stream = file.OpenReadStream();
            var stored = await _fileStorage.StoreAsync(stream, file.FileName, cancellationToken);

            var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var presentation = await _sessionService.UploadPresentationAsync(id, userId, stored.RelativePath, file.FileName, cancellationToken);
            return Ok(presentation);
        }

        /// <summary>Whether a deck has been uploaded for this session yet, and its file name — same
        /// participant access as joining the class itself.</summary>
        [HttpGet("{id:guid}/presentation")]
        [Authorize]
        public async Task<ActionResult<SessionPresentationDto?>> GetPresentation(Guid id, CancellationToken cancellationToken)
        {
            var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            return Ok(await _sessionService.GetPresentationAsync(id, userId, cancellationToken));
        }

        /// <summary>Streams the uploaded PDF itself — what the live classroom's viewer actually
        /// points pdf.js at. Same participant access as joining the class.</summary>
        [HttpGet("{id:guid}/presentation/file")]
        [Authorize]
        public async Task<IActionResult> DownloadPresentation(Guid id, CancellationToken cancellationToken)
        {
            var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var presentation = await _sessionService.GetPresentationForDownloadAsync(id, userId, cancellationToken);
            var stream = await _fileStorage.OpenReadAsync(presentation.StorageUrl, cancellationToken);

            if (stream is null)
            {
                return NotFound(new ProblemDetails { Status = 404, Title = "Not Found", Detail = "The stored file is missing." });
            }

            return File(stream, "application/pdf", presentation.OriginalFileName);
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
