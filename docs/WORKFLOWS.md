# Teacher & Admission Workflow Mapping (Sprint 0)

## Admission funnel (demo → enrollment)
1. **Demo booked** — admission team (or parent, one-time only) books a demo:
   creates a `ClassSession(Type=Demo)` + `DemoBooking` with lead contact details
   and optional extra `DemoParticipant` invitees. Status: `DemoScheduled`.
2. **Demo delivered** — teacher launches from dashboard; attendance captured on join.
3. **Feedback gate** — teacher must submit `DemoFeedback` (academic level, strengths,
   improvement areas, recommended course, suggested batch type) before closing the demo.
   Status: `DemoCompleted`.
4. **Follow-up** — admission team reviews feedback, shares payment link
   (`DemoBooking.PaymentLinkUrl` → `Invoice`). Status: `FollowUpInProgress`.
5. **Conversion** — on payment: admin creates the parent account (credentials emailed),
   parent completes the mandatory enrollment form, child is enrolled into a batch.
   Status: `Enrolled` (or `NotInterested`).

## Teacher operating loop
1. **Today/upcoming** — `/api/sessions/mine`; one-click class launch (no manual links).
2. **Deliver** — live classroom with whiteboard; per-student controls; attendance auto-captured.
3. **Close** — session completed (`/api/sessions/{id}/complete`); payout item accrues (Sprint 3);
   when the batch's course session count completes, the batch auto-moves to Dormant,
   starting the 15-day recording window.
4. **Leave** — request ≥ 6 hours before the affected session (hard block otherwise);
   admin approves/rejects; approved leave surfaces on the academic calendar and
   triggers reschedules (carried-forward sessions).
5. **No-show** — teacher no-show: admin alert + payout deduction; student no-show:
   waiting amount to teacher payout; both carry the session forward independently.
