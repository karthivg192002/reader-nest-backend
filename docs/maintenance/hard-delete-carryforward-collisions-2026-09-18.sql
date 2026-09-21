-- ============================================================================
-- THIRD-PASS, IRREVERSIBLE hard delete: 16 CarriedForward class_sessions
-- that duplicate their batch's own already-scheduled slot.
-- ============================================================================
--
-- Root cause (now fixed in code -- see SessionService.MarkNoShowCoreAsync /
-- NextAvailableCarryForwardSlotAsync, and the new unique index
-- ix_class_sessions_batch_id_scheduled_start_at_utc added by migration
-- 20260918011659_PreventDuplicateClassSessionSlots):
--
-- When a class is marked as a no-show, the app creates a new "CarriedForward"
-- session one week later at the same time of day, so the missed class isn't
-- lost. But it never checked whether the batch's own regular weekly schedule
-- already had a normal "Scheduled" session at that exact slot -- which it
-- almost always does, since a no-show doesn't change what day/time a batch
-- normally meets. The result: 16 batches each ended up with two rows for the
-- same class -- one Scheduled, one CarriedForward -- at the exact same
-- batch_id + scheduled_start_at_utc.
--
-- These are separate from, and were found only after, the two earlier
-- cleanup passes (hard-delete-migration-duplicates-2026-09-17.sql,
-- hard-delete-manual-review-2026-09-18.sql), which fixed a different bug
-- (Bulk Import Students / GenerateScheduleAsync). All 16 rows here were
-- verified individually: CarriedForward status, no recording, no
-- attendance, no demo booking, no payout item, and not referenced by any
-- further reschedule/carry-forward chain -- so removing them is a pure
-- no-op from the data's perspective; the batch's regular Scheduled session
-- for that slot is untouched and is the one that actually happens.
--
-- NOT INCLUDED: a 17th "duplicate group" surfaced by the same scan, batch
-- "Sannvika Grampurrohit-5" (01a05504-e008-7144-8c21-6cb1dc2358de), 6 rows
-- all status TeacherNoShow at THREE pairs of matching timestamps -- but
-- every one of those timestamps has a bogus year (year "6", e.g.
-- 0006-09-25 instead of 2026-09-25). That's a distinct, unrelated data
-- quality issue (a date stored/parsed wrong somewhere, not this carry-
-- forward collision), and deleting rows there needs its own investigation,
-- not a blind copy of this script.
-- ============================================================================


-- ---------------------------------------------------------------------------
-- STEP 1 -- READ ONLY. Confirm all 16 rows are what's described above before
-- touching anything.
-- ---------------------------------------------------------------------------
CREATE TEMP TABLE tmp_session_ids (id uuid);
INSERT INTO tmp_session_ids VALUES
    ('01a0afca-61c4-77aa-82b6-bf3108ed0263'), ('01a0afdc-b193-7fef-9b5a-4a17112481fb'),
    ('01a0b191-34af-77cc-9da9-6e33ca1cd738'), ('01a0afdc-b1cc-7a82-b077-984f54815ff6'),
    ('01a0afdc-b1be-7ffb-85d5-c0df71f07cf3'), ('01a0b010-aca0-70f0-bac8-c3e6650bb61c'),
    ('01a0b1ac-ac47-798d-817c-f2ccd417c623'), ('01a0afe5-d9b7-7524-a3c7-f6f33fac4b51'),
    ('01a0b001-5150-72ac-9eb3-9225fd27c4a2'), ('01a0afca-5d2d-7ff9-8338-0f8081ca2248'),
    ('01a0afdc-b17e-7af0-9019-59f3b512a746'), ('01a0afdc-b1aa-748c-98e6-1b5bfd1adc8d'),
    ('01a0b02c-2470-79a3-9b47-3dfb9d2dfb51'), ('01a0aff8-2964-7117-b7c0-f0ab60875d6c'),
    ('01a0b063-136c-7ac8-a077-71fdcadd08e5'), ('01a0b1d1-4b99-78ac-94e6-d7972d477426');
-- Expect 16 rows.
SELECT count(*) AS session_ids_loaded FROM tmp_session_ids;

SELECT
    cs.id, b.name AS batch_name, cs.scheduled_start_at_utc, cs.status,
    EXISTS (SELECT 1 FROM session_recordings sr WHERE sr.class_session_id = cs.id) AS has_recording,
    EXISTS (SELECT 1 FROM session_attendances sa WHERE sa.class_session_id = cs.id) AS has_attendance,
    EXISTS (SELECT 1 FROM class_sessions other WHERE other.rescheduled_from_session_id = cs.id OR other.carried_forward_from_session_id = cs.id) AS is_referenced_by_another_session,
    -- The Scheduled sibling this CarriedForward row duplicates, so you can see the pair together.
    (SELECT string_agg(sib.id::text || ' (' || sib.status || ')', ', ')
     FROM class_sessions sib
     WHERE sib.batch_id = cs.batch_id AND sib.scheduled_start_at_utc = cs.scheduled_start_at_utc AND sib.id <> cs.id) AS sibling_rows_same_slot
FROM class_sessions cs
JOIN batches b ON b.id = cs.batch_id
JOIN tmp_session_ids t ON t.id = cs.id
ORDER BY batch_name;

-- Any row above with has_recording/has_attendance/is_referenced_by_another_session
-- = true, or without a Scheduled sibling at sibling_rows_same_slot, STOP --
-- that row isn't the clean duplicate it's expected to be.

CREATE TEMP TABLE tmp_demo_booking_ids AS
    SELECT id FROM demo_bookings WHERE class_session_id IN (SELECT id FROM tmp_session_ids);
CREATE TEMP TABLE tmp_invoice_ids AS
    SELECT invoice_id FROM demo_bookings WHERE id IN (SELECT id FROM tmp_demo_booking_ids) AND invoice_id IS NOT NULL;
CREATE TEMP TABLE tmp_payment_transaction_ids AS
    SELECT id FROM payment_transactions WHERE invoice_id IN (SELECT id FROM tmp_invoice_ids);

SELECT 'refunds' AS table_name, count(*) FROM refunds WHERE payment_transaction_id IN (SELECT id FROM tmp_payment_transaction_ids)
UNION ALL SELECT 'payment_transactions', count(*) FROM payment_transactions WHERE id IN (SELECT id FROM tmp_payment_transaction_ids)
UNION ALL SELECT 'fee_suspensions', count(*) FROM fee_suspensions WHERE invoice_id IN (SELECT id FROM tmp_invoice_ids)
UNION ALL SELECT 'demo_feedbacks', count(*) FROM demo_feedbacks WHERE demo_booking_id IN (SELECT id FROM tmp_demo_booking_ids)
UNION ALL SELECT 'demo_participants', count(*) FROM demo_participants WHERE demo_booking_id IN (SELECT id FROM tmp_demo_booking_ids)
UNION ALL SELECT 'demo_bookings', count(*) FROM demo_bookings WHERE id IN (SELECT id FROM tmp_demo_booking_ids)
UNION ALL SELECT 'invoices', count(*) FROM invoices WHERE id IN (SELECT id FROM tmp_invoice_ids)
UNION ALL SELECT 'payout_items', count(*) FROM payout_items WHERE class_session_id IN (SELECT id FROM tmp_session_ids)
UNION ALL SELECT 'leave_request_sessions', count(*) FROM leave_request_sessions WHERE class_session_id IN (SELECT id FROM tmp_session_ids)
UNION ALL SELECT 'class_session_event_logs', count(*) FROM class_session_event_logs WHERE class_session_id IN (SELECT id FROM tmp_session_ids)
UNION ALL SELECT 'engagement_events', count(*) FROM engagement_events WHERE class_session_id IN (SELECT id FROM tmp_session_ids)
UNION ALL SELECT 'session_attendances', count(*) FROM session_attendances WHERE class_session_id IN (SELECT id FROM tmp_session_ids)
UNION ALL SELECT 'student_awards', count(*) FROM student_awards WHERE class_session_id IN (SELECT id FROM tmp_session_ids)
UNION ALL SELECT 'session_presentations', count(*) FROM session_presentations WHERE class_session_id IN (SELECT id FROM tmp_session_ids)
UNION ALL SELECT 'session_recordings', count(*) FROM session_recordings WHERE class_session_id IN (SELECT id FROM tmp_session_ids)
UNION ALL SELECT 'class_sessions.rescheduled_from_session_id pointers (self-FK, will be nulled)', count(*) FROM class_sessions WHERE rescheduled_from_session_id IN (SELECT id FROM tmp_session_ids)
UNION ALL SELECT 'class_sessions.carried_forward_from_session_id pointers (self-FK, will be nulled)', count(*) FROM class_sessions WHERE carried_forward_from_session_id IN (SELECT id FROM tmp_session_ids)
UNION ALL SELECT 'class_sessions (final target)', count(*) FROM class_sessions WHERE id IN (SELECT id FROM tmp_session_ids);

DROP TABLE tmp_session_ids, tmp_demo_booking_ids, tmp_invoice_ids, tmp_payment_transaction_ids;


-- ---------------------------------------------------------------------------
-- STEP 2 -- THE HARD DELETE. Irreversible.
-- ---------------------------------------------------------------------------
BEGIN;

DROP TABLE IF EXISTS tmp_session_ids, tmp_demo_booking_ids, tmp_invoice_ids, tmp_payment_transaction_ids CASCADE;

CREATE TEMP TABLE tmp_session_ids (id uuid) ON COMMIT DROP;
INSERT INTO tmp_session_ids VALUES
    ('01a0afca-61c4-77aa-82b6-bf3108ed0263'), ('01a0afdc-b193-7fef-9b5a-4a17112481fb'),
    ('01a0b191-34af-77cc-9da9-6e33ca1cd738'), ('01a0afdc-b1cc-7a82-b077-984f54815ff6'),
    ('01a0afdc-b1be-7ffb-85d5-c0df71f07cf3'), ('01a0b010-aca0-70f0-bac8-c3e6650bb61c'),
    ('01a0b1ac-ac47-798d-817c-f2ccd417c623'), ('01a0afe5-d9b7-7524-a3c7-f6f33fac4b51'),
    ('01a0b001-5150-72ac-9eb3-9225fd27c4a2'), ('01a0afca-5d2d-7ff9-8338-0f8081ca2248'),
    ('01a0afdc-b17e-7af0-9019-59f3b512a746'), ('01a0afdc-b1aa-748c-98e6-1b5bfd1adc8d'),
    ('01a0b02c-2470-79a3-9b47-3dfb9d2dfb51'), ('01a0aff8-2964-7117-b7c0-f0ab60875d6c'),
    ('01a0b063-136c-7ac8-a077-71fdcadd08e5'), ('01a0b1d1-4b99-78ac-94e6-d7972d477426');

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
-- Expect 16 rows RETURNING above.

-- COMMIT; only if the count matches exactly (16).
-- ROLLBACK; otherwise.
