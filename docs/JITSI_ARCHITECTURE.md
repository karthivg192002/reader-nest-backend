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
- **Production hardening (Sprint 2)**: JWT-secured rooms (prosody `token_verification`)
  so only authenticated portal users can join; secure domain to enforce lobby/waiting room.
- **Recording (blocked on infra decision)**: Jibri pool writing to client cloud storage;
  `SessionRecording.ExpiresAtUtc` drives the 15-day parent access window.
- Teacher controls (mute participant, disable camera, lobby) map to IFrame API
  commands: `muteEveryone`, `sendEndpointTextMessage`, `toggleLobby`, participant actions.

## Open items
- Client to choose hosting region + sizing for JVB (load test in Sprint 3 informs this).
- Recording infrastructure (Jibri VM pool) — provision with the client cloud account.
