# Long-Duration Sessions & Recording Storage (Sprint 1 verification)

## Session duration

- Class durations are fixed at 30/45/60 minutes, but demos with multiple parents
  and back-to-back batches keep rooms open longer; the self-hosted Jitsi
  deployment (`meet.techmisai.com`) has **no duration cap** — the public
  meet.jit.si 5-minute limit was the reason self-hosting was chosen (see
  JITSI_ARCHITECTURE.md).
- Browser-side stability over 60+ minutes is bounded by JVB memory and TURN
  throughput, not the platform: sessions hold as long as the JVB stays healthy.
  Concurrency limits are established by load testing, not assumed:
  `load-tests/live-classroom-load.js` (k6) covers the platform/API side of a
  class start; media-plane soak uses jitsi-meet-torture directly against the
  Jitsi deployment (execution scheduled with the Sprint 5 load-test pass).

## Recording storage cost & lifecycle

Assumptions: Jibri produces ~1 GB/hour at 720p (observed range 0.7–1.4 GB/h).

| Monthly recorded hours | Raw storage | With 15-day retention (avg) | Object storage cost @ ~$0.02/GB-mo |
|---|---|---|---|
| 200 h (pilot) | ~200 GB | ~100 GB | ~$2/mo |
| 1,000 h | ~1 TB | ~0.5 TB | ~$10/mo |
| 5,000 h | ~5 TB | ~2.5 TB | ~$50/mo |

- **Retention is the platform's control**: `SessionRecording.ExpiresAtUtc` is set
  to +15 days at registration; expired recordings disappear from parent-facing
  lists automatically, and a storage lifecycle rule (delete after 16 days)
  keeps the bucket bounded — cost stays roughly constant regardless of tenure.
- Egress dominates cost only if parents stream heavily; view-only playback from
  the same region keeps this in single-digit dollars at pilot scale.

## Conclusion

Long-duration support is a Jitsi-deployment property (already satisfied by
self-hosting); storage cost is bounded by the enforced 15-day expiry and is
negligible at contracted scale. Final concurrency numbers come from the
Sprint 5 load-test execution on the client's provisioned infrastructure.
