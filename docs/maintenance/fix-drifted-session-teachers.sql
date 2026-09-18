-- ============================================================================
-- One-time data repair: ClassSession rows stuck on a batch's OLD teacher
-- ============================================================================
--
-- Background: reassigning a batch's teacher used to only update the Batch
-- row itself. Every ClassSession already generated for that batch kept
-- pointing at whoever the teacher was when GenerateScheduleAsync created it,
-- because it sets TeacherProfileId once and never revisits it. The old
-- teacher's own "My Classes" / dashboard (GET /api/sessions/mine, which
-- filters on ClassSession.TeacherProfileId) kept showing every one of that
-- batch's not-yet-delivered sessions -- a real cross-account data exposure,
-- not just a stale display: the old teacher could still open and start a
-- class that's no longer theirs.
--
-- The application code that reassigns a batch's teacher going forward is
-- already fixed (BatchService.UpdateAsync) -- it now moves every affected
-- future session's TeacherProfileId in the same transaction as the
-- reassignment. This script is the one-time catch-up for sessions that
-- drifted BEFORE that fix existed: it applies the exact same rule --
-- Scheduled or CarriedForward status, not yet started -- retroactively.
--
-- HOW TO USE
--   1. Run STEP 1 alone first. Read every row. If it's empty, there is
--      nothing to fix -- stop here, do not run STEP 2.
--   2. Only if STEP 1's rows genuinely look wrong (batch's current teacher
--      differs from the session's teacher), run STEP 2 inside the SAME
--      transaction block (BEGIN ... COMMIT) so you can ROLLBACK instead of
--      COMMIT if anything looks off after the UPDATE's own RETURNING output.
--   3. STEP 3 (optional) records the fix in the app's own audit trail, the
--      same nameof(ClassSession) / "teacherReassigned" shape BatchService
--      already writes for a normal reassignment, so this shows up in
--      Audit Log like any other teacher-reassignment event.
--
-- Safe by construction: STEP 2's WHERE clause only ever touches a row
-- whose batch_id points at a batch whose CURRENT teacher_profile_id no
-- longer matches the session's own -- i.e. only genuinely drifted rows,
-- never a session that's correctly aligned already.
-- ============================================================================


-- ---------------------------------------------------------------------------
-- STEP 1 -- READ ONLY. Find every drifted session. Review this output first.
-- ---------------------------------------------------------------------------
SELECT
    cs.id                          AS session_id,
    b.id                           AS batch_id,
    b.name                         AS batch_name,
    cs.scheduled_start_at_utc,
    cs.status,
    old_u.first_name || ' ' || old_u.last_name  AS session_currently_shows_teacher,
    new_u.first_name || ' ' || new_u.last_name  AS batch_actual_current_teacher,
    cs.teacher_profile_id          AS drifted_teacher_profile_id,
    b.teacher_profile_id           AS correct_teacher_profile_id
FROM class_sessions cs
JOIN batches b            ON b.id = cs.batch_id
JOIN teacher_profiles old_tp ON old_tp.id = cs.teacher_profile_id
JOIN users old_u           ON old_u.id = old_tp.user_id
JOIN teacher_profiles new_tp ON new_tp.id = b.teacher_profile_id
JOIN users new_u           ON new_u.id = new_tp.user_id
WHERE cs.is_deleted = false
  AND b.is_deleted = false
  AND cs.status IN ('Scheduled', 'CarriedForward')
  AND cs.scheduled_start_at_utc > now()
  AND cs.teacher_profile_id <> b.teacher_profile_id
ORDER BY cs.scheduled_start_at_utc;


-- ---------------------------------------------------------------------------
-- STEP 2 -- THE FIX. Only run after reviewing STEP 1's output above.
-- Wrapped in a transaction so you can inspect the RETURNING rows and
-- ROLLBACK instead of COMMIT if anything looks wrong.
-- ---------------------------------------------------------------------------
BEGIN;

UPDATE class_sessions cs
SET teacher_profile_id = b.teacher_profile_id,
    updated_at_utc = now()
FROM batches b
WHERE b.id = cs.batch_id
  AND cs.is_deleted = false
  AND b.is_deleted = false
  AND cs.status IN ('Scheduled', 'CarriedForward')
  AND cs.scheduled_start_at_utc > now()
  AND cs.teacher_profile_id <> b.teacher_profile_id
RETURNING cs.id AS session_id, cs.batch_id, cs.teacher_profile_id AS new_teacher_profile_id;

-- Review the RETURNING rows above. If they match STEP 1 exactly:
--   COMMIT;
-- If anything looks wrong:
--   ROLLBACK;


-- ---------------------------------------------------------------------------
-- STEP 3 -- OPTIONAL. Only after COMMIT above. One audit_logs entry per
-- affected batch, matching BatchService's own "teacherReassigned" shape,
-- so this repair is visible in Audit Log like a normal reassignment.
-- Run this as its own statement, after STEP 2 has been committed.
-- ---------------------------------------------------------------------------
-- INSERT INTO audit_logs (id, actor_user_id, action, entity_name, entity_id, changes_json, created_at_utc, is_deleted)
-- SELECT
--     gen_random_uuid(),
--     NULL, -- system/maintenance action, not an interactive admin click
--     'Update',
--     'ClassSession',
--     b.id::text,
--     '{"batchId":"' || b.id || '","teacherReassigned":true,"note":"one-time data repair for pre-existing drift","sessionCount":' ||
--         (SELECT count(*) FROM class_sessions cs2 WHERE cs2.batch_id = b.id AND cs2.teacher_profile_id = b.teacher_profile_id) || '}',
--     now(),
--     false
-- FROM batches b
-- WHERE b.id IN (/* paste the distinct batch_id values from STEP 1's output here */);
