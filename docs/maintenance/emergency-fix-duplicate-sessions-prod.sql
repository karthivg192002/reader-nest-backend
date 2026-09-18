-- ============================================================================
-- EMERGENCY: unblock the PreventDuplicateClassSessionSlots migration on
-- production by removing whatever duplicate (batch_id, scheduled_start_at_utc)
-- class_sessions rows currently exist there. Production crashed on startup
-- (23505 unique_violation while creating ix_class_sessions_batch_id_
-- scheduled_start_at_utc) because this data was never cleaned there --
-- only reader_nest_backup was cleaned.
--
-- Unlike the earlier hard-delete-*.sql scripts (which used specific ids
-- pulled from a backup snapshot), this one finds ITS OWN target rows live,
-- since production has been running with the uncorrected bug longer than
-- the backup snapshot and may have more duplicates than we saw there.
--
-- SAFETY RULE (same as every prior pass): a duplicate group is only ever
-- auto-resolved if every row in it is "safe" -- no Completed/InProgress
-- status, no attached recording, no attendance taken. If ANY row in a
-- group is unsafe, the WHOLE group is left untouched and reported
-- separately for manual review; nothing about it is guessed.
--
-- Within a safe group, the KEPT row is: prefer a non-CarriedForward row
-- (the batch's real regularly-scheduled class) over a CarriedForward row
-- (the no-show placement that collided with it); if there's no such
-- distinction, keep the earliest-created row. Every other row in the group
-- is removed, using the same FK-safe multi-table order as the earlier
-- scripts (every FK in this DB is DeleteBehavior.Restrict).
--
-- HOW TO USE
--   1. Run STEP 1 (read-only). It reports (a) any UNSAFE groups being
--      skipped and why, and (b) the exact rows about to be removed.
--      Sanity check the counts.
--   2. Run STEP 2 inside BEGIN/COMMIT. Check the RETURNING count against
--      STEP 1's "rows to remove" count, then COMMIT or ROLLBACK.
--   3. Once committed, restart the API -- the migration should now apply
--      cleanly since no duplicate (batch_id, scheduled_start_at_utc) pairs
--      remain among active rows.
-- ============================================================================


-- ---------------------------------------------------------------------------
-- STEP 1 -- READ ONLY.
-- ---------------------------------------------------------------------------
WITH dupe_groups AS (
    SELECT batch_id, scheduled_start_at_utc
    FROM class_sessions
    WHERE is_deleted = false AND batch_id IS NOT NULL
    GROUP BY batch_id, scheduled_start_at_utc
    HAVING count(*) > 1
),
group_rows AS (
    SELECT cs.*, EXISTS (
        SELECT 1 FROM session_recordings sr WHERE sr.class_session_id = cs.id
    ) AS has_recording, EXISTS (
        SELECT 1 FROM session_attendances sa WHERE sa.class_session_id = cs.id
    ) AS has_attendance
    FROM class_sessions cs
    JOIN dupe_groups dg ON dg.batch_id = cs.batch_id AND dg.scheduled_start_at_utc = cs.scheduled_start_at_utc
    WHERE cs.is_deleted = false
),
group_safety AS (
    SELECT batch_id, scheduled_start_at_utc,
        bool_or(status IN ('Completed', 'InProgress') OR has_recording OR has_attendance) AS is_unsafe
    FROM group_rows
    GROUP BY batch_id, scheduled_start_at_utc
)
SELECT 'UNSAFE -- skipped, needs manual review' AS outcome,
    gr.id, b.name AS batch_name, gr.scheduled_start_at_utc, gr.status, gr.has_recording, gr.has_attendance
FROM group_rows gr
JOIN group_safety gs ON gs.batch_id = gr.batch_id AND gs.scheduled_start_at_utc = gr.scheduled_start_at_utc
LEFT JOIN batches b ON b.id = gr.batch_id
WHERE gs.is_unsafe
ORDER BY batch_name, gr.scheduled_start_at_utc;

WITH dupe_groups AS (
    SELECT batch_id, scheduled_start_at_utc
    FROM class_sessions
    WHERE is_deleted = false AND batch_id IS NOT NULL
    GROUP BY batch_id, scheduled_start_at_utc
    HAVING count(*) > 1
),
group_rows AS (
    SELECT cs.*, EXISTS (
        SELECT 1 FROM session_recordings sr WHERE sr.class_session_id = cs.id
    ) AS has_recording, EXISTS (
        SELECT 1 FROM session_attendances sa WHERE sa.class_session_id = cs.id
    ) AS has_attendance
    FROM class_sessions cs
    JOIN dupe_groups dg ON dg.batch_id = cs.batch_id AND dg.scheduled_start_at_utc = cs.scheduled_start_at_utc
    WHERE cs.is_deleted = false
),
group_safety AS (
    SELECT batch_id, scheduled_start_at_utc,
        bool_or(status IN ('Completed', 'InProgress') OR has_recording OR has_attendance) AS is_unsafe
    FROM group_rows
    GROUP BY batch_id, scheduled_start_at_utc
),
ranked AS (
    SELECT gr.*,
        row_number() OVER (
            PARTITION BY gr.batch_id, gr.scheduled_start_at_utc
            ORDER BY (gr.status = 'CarriedForward'), gr.created_at_utc
        ) AS keep_rank
    FROM group_rows gr
    JOIN group_safety gs ON gs.batch_id = gr.batch_id AND gs.scheduled_start_at_utc = gr.scheduled_start_at_utc
    WHERE NOT gs.is_unsafe
)
SELECT 'REMOVE (safe duplicate)' AS outcome,
    r.id, b.name AS batch_name, r.scheduled_start_at_utc, r.status, r.created_at_utc
FROM ranked r
LEFT JOIN batches b ON b.id = r.batch_id
WHERE r.keep_rank > 1
ORDER BY batch_name, r.scheduled_start_at_utc;

-- Read both result sets above. Any "UNSAFE" rows need a human decision, not
-- this script. Count the "REMOVE" rows -- that's what STEP 2 should return.


-- ---------------------------------------------------------------------------
-- STEP 2 -- THE FIX. Irreversible. Only removes rows from groups where every
-- member was judged safe above; unsafe groups are left completely alone.
-- ---------------------------------------------------------------------------
BEGIN;

DROP TABLE IF EXISTS tmp_session_ids, tmp_demo_booking_ids, tmp_invoice_ids, tmp_payment_transaction_ids CASCADE;

CREATE TEMP TABLE tmp_session_ids (id uuid) ON COMMIT DROP;
INSERT INTO tmp_session_ids
WITH dupe_groups AS (
    SELECT batch_id, scheduled_start_at_utc
    FROM class_sessions
    WHERE is_deleted = false AND batch_id IS NOT NULL
    GROUP BY batch_id, scheduled_start_at_utc
    HAVING count(*) > 1
),
group_rows AS (
    SELECT cs.*, EXISTS (
        SELECT 1 FROM session_recordings sr WHERE sr.class_session_id = cs.id
    ) AS has_recording, EXISTS (
        SELECT 1 FROM session_attendances sa WHERE sa.class_session_id = cs.id
    ) AS has_attendance
    FROM class_sessions cs
    JOIN dupe_groups dg ON dg.batch_id = cs.batch_id AND dg.scheduled_start_at_utc = cs.scheduled_start_at_utc
    WHERE cs.is_deleted = false
),
group_safety AS (
    SELECT batch_id, scheduled_start_at_utc,
        bool_or(status IN ('Completed', 'InProgress') OR has_recording OR has_attendance) AS is_unsafe
    FROM group_rows
    GROUP BY batch_id, scheduled_start_at_utc
),
ranked AS (
    SELECT gr.*,
        row_number() OVER (
            PARTITION BY gr.batch_id, gr.scheduled_start_at_utc
            ORDER BY (gr.status = 'CarriedForward'), gr.created_at_utc
        ) AS keep_rank
    FROM group_rows gr
    JOIN group_safety gs ON gs.batch_id = gr.batch_id AND gs.scheduled_start_at_utc = gr.scheduled_start_at_utc
    WHERE NOT gs.is_unsafe
)
SELECT id FROM ranked WHERE keep_rank > 1;

SELECT count(*) AS rows_to_remove FROM tmp_session_ids;
-- Compare this to STEP 1's "REMOVE (safe duplicate)" row count before going further.

CREATE TEMP TABLE tmp_demo_booking_ids ON COMMIT DROP AS
    SELECT id FROM demo_bookings WHERE class_session_id IN (SELECT id FROM tmp_session_ids);
CREATE TEMP TABLE tmp_invoice_ids ON COMMIT DROP AS
    SELECT invoice_id FROM demo_bookings WHERE id IN (SELECT id FROM tmp_demo_booking_ids) AND invoice_id IS NOT NULL;
CREATE TEMP TABLE tmp_payment_transaction_ids ON COMMIT DROP AS
    SELECT id FROM payment_transactions WHERE invoice_id IN (SELECT id FROM tmp_invoice_ids);

DELETE FROM refunds WHERE payment_transaction_id IN (SELECT id FROM tmp_payment_transaction_ids);
DELETE FROM payment_transactions WHERE id IN (SELECT id FROM tmp_payment_transaction_ids);
DELETE FROM fee_suspensions WHERE invoice_id IN (SELECT id FROM tmp_invoice_ids);
DELETE FROM demo_feedbacks WHERE demo_booking_id IN (SELECT id FROM tmp_demo_booking_ids);
DELETE FROM demo_participants WHERE demo_booking_id IN (SELECT id FROM tmp_demo_booking_ids);
DELETE FROM demo_bookings WHERE id IN (SELECT id FROM tmp_demo_booking_ids);
DELETE FROM invoices WHERE id IN (SELECT id FROM tmp_invoice_ids);
DELETE FROM payout_items WHERE class_session_id IN (SELECT id FROM tmp_session_ids);
DELETE FROM leave_request_sessions WHERE class_session_id IN (SELECT id FROM tmp_session_ids);
DELETE FROM class_session_event_logs WHERE class_session_id IN (SELECT id FROM tmp_session_ids);
DELETE FROM engagement_events WHERE class_session_id IN (SELECT id FROM tmp_session_ids);
DELETE FROM session_attendances WHERE class_session_id IN (SELECT id FROM tmp_session_ids);
DELETE FROM student_awards WHERE class_session_id IN (SELECT id FROM tmp_session_ids);
DELETE FROM session_presentations WHERE class_session_id IN (SELECT id FROM tmp_session_ids);
DELETE FROM session_recordings WHERE class_session_id IN (SELECT id FROM tmp_session_ids);

UPDATE class_sessions SET rescheduled_from_session_id = NULL
WHERE rescheduled_from_session_id IN (SELECT id FROM tmp_session_ids);
UPDATE class_sessions SET carried_forward_from_session_id = NULL
WHERE carried_forward_from_session_id IN (SELECT id FROM tmp_session_ids);

DELETE FROM class_sessions WHERE id IN (SELECT id FROM tmp_session_ids)
RETURNING id, batch_id, scheduled_start_at_utc;
-- Compare this RETURNING count to the "rows_to_remove" count printed above.

-- If they match:
--   COMMIT;
-- If not, or anything looks wrong:
--   ROLLBACK;


-- ---------------------------------------------------------------------------
-- STEP 3 -- After COMMIT, verify no duplicate (batch_id, scheduled_start_at_utc)
-- pairs remain among active rows (should return 0 rows) before restarting
-- the API / re-running the migration.
-- ---------------------------------------------------------------------------
-- SELECT batch_id, scheduled_start_at_utc, count(*)
-- FROM class_sessions
-- WHERE is_deleted = false AND batch_id IS NOT NULL
-- GROUP BY batch_id, scheduled_start_at_utc
-- HAVING count(*) > 1;
