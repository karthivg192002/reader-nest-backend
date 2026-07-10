# UAT Plan Skeleton (Sprint 0 → executed in Stabilization Week)

## Entry criteria
- Sprint 5 full regression green; P0/P1 defect count at agreed threshold.
- Staging environment on client cloud with production-like data set and both
  department payment accounts in test mode.

## UAT tracks (one per portal, client stakeholders assigned per track)
| Track | Key scenarios |
|---|---|
| Admin | User lifecycle + credential email, course/batch setup, calendar, fee suspension + restore, payouts view, reports/CSV |
| Sub Admin | Permission matrix enforcement (positive + negative), Coordinator & Management presets |
| Admission | Demo booking (multi-parent), teacher feedback review, conversion funnel, payment links |
| Teacher | Launch class, whiteboard + activities, mandatory demo feedback gate, leave (6-hour rule), payout visibility |
| Parent | First-login enrollment form gate, multi-child dashboard, join class, worksheets/recordings (15-day window), Pay Now flow |
| Live classroom | Join quality, controls, gamification, recording availability |

## Defect handling
Severity 1–2 fixed within the stabilization week; severity 3+ triaged into hypercare backlog.

## Exit criteria / sign-off
All tracks executed, zero open Sev-1/2, operational readiness checklist
(role access, academic ops, billing ops, reporting) signed by the client sponsor.

## Templates
- Test case: ID / portal / scenario / steps / expected / actual / status / severity.
- Defect: ID / env / steps / expected vs actual / severity / owner / fix sprint.
