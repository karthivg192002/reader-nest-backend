# Jitsi Meet Integration Architecture (Sprint 0 decision)

## Decision
Embed Jitsi Meet in the web portals via the **IFrame API** (`external_api.js`),
starting on the free public `meet.jit.si` for development, with **self-hosting
(Docker: jitsi-meet + prosody + jicofo + jvb) on the client's cloud for production**.

## Options considered
| Option | Pros | Cons |
|---|---|---|
| meet.jit.si (public) | Zero setup; fine for dev/POC | 5-min unauthenticated limit introduced by 8x8; no branding; no recording control |
| **Self-hosted Jitsi (chosen for prod)** | Full white-label + client cloud ownership (hard requirement); unlimited durations; Jibri recording under our control; JWT room auth | Ops burden: JVB scaling, TURN, Jibri capacity |
| JaaS (8x8) | Managed, SLA, recording built in | Per-MAU cost; data outside client cloud; weaker white-label story |

Client ownership requirements (source code, cloud, API ownership) point firmly at
self-hosting; JaaS remains the fallback if ops capacity becomes a risk.

## Integration design
- `ClassSession.MeetingRoomId` (generated `trn-…`, never manual links) is the room name.
- Frontend `JitsiRoom` component loads `external_api.js` and joins with the user's
  display name; teachers join as moderators.
- **JWT-secured rooms (done, live in production)**: Prosody runs with `authentication = "token"`
  and `allow_empty_token = false`, so only users holding a token minted by the backend can join.
  The backend sets the `moderator` claim per role (teachers and admins only -- see
  `SessionService`), which is what decides who can mute others / start recording. A parent joining
  before the teacher does not become moderator.
- **Still open**: a secure-domain lobby / waiting room (not enabled).
- **Recording (auto-start blocked on infra decision)**: the "jitsi" Integration's
  `autoRecord` config field (Settings → Integrations → Jitsi Meet, default `"true"`)
  gates whether `JitsiLive` calls `startRecording` on host join — on = auto, off =
  the teacher relies entirely on the manual "Recording" registration on My Classes
  (or Jitsi's own toolbar button, which still auto-registers via `recordingLinkAvailable`
  either way). The auto-start command itself is a no-op until a Jibri pool exists on
  the deployment, so admins should leave it off until Jibri is provisioned to avoid a
  confusing "nothing happened" toolbar state. `SessionRecording.ExpiresAtUtc` drives
  the 15-day parent access window regardless of how the recording was registered.
- Teacher controls (mute participant, disable camera, lobby) map to IFrame API
  commands: `muteEveryone`, `sendEndpointTextMessage`, `toggleLobby`, participant actions.

## Open items
- Client to choose hosting region + sizing for JVB (load test in Sprint 3 informs this).
- Recording infrastructure (Jibri VM pool) — provision with the client cloud account.
