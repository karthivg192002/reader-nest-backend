-- ============================================================================
-- SECOND-PASS, IRREVERSIBLE hard delete: the 32 children (16 groups) / 6
-- class_sessions (3 groups) previously flagged NEEDS MANUAL REVIEW and
-- deliberately excluded from the first cleanup pass
-- (hard-delete-migration-duplicates-2026-09-17.sql, which removed 95
-- children + 85 sessions confirmed to be empty ghost duplicates).
-- ============================================================================
--
-- *** THIS SCRIPT PERMANENTLY DESTROYS DATA THAT INCLUDES REAL, ACTIVE
-- STUDENT RECORDS. THIS IS NOT A "safe duplicate cleanup" LIKE THE FIRST
-- SCRIPT. ***
--
-- Every one of the 16 child groups here has at least one row with
-- has_active_enrollment = true (a real, currently-running course
-- enrollment), and one group (Nayesha Kashyap, parent Kalpana Sharma) has a
-- row with has_outstanding_invoice = true (an unpaid bill). All 6 sessions
-- here are real reschedule/completion pairs, not accidental duplicates --
-- one group even includes a Completed session with an attached video
-- recording. See client_decision_needed_children.csv /
-- client_decision_needed_sessions.csv for the full detail on every row
-- before running this.
--
-- This script exists because the decision was made to delete these rows
-- anyway rather than wait for client input on which copy to keep. That
-- decision means the affected families lose access to whichever active
-- enrollment/invoice/recording belonged to the deleted copy.
--
-- Same FK-safe deletion order as the first script (every FK in this DB is
-- DeleteBehavior.Restrict -- see ReaderNestDbContext.cs ~line 148 -- so
-- nothing cascades automatically), including the two self-referencing
-- class_sessions FKs (rescheduled_from_session_id,
-- carried_forward_from_session_id) discovered and fixed during the first
-- run.
--
-- HOW TO USE: same as the first script. Run STEP 1 (read-only) and read
-- every row -- this is your last chance to see exactly what's being
-- destroyed. Run STEP 2 inside BEGIN/COMMIT, check the RETURNING counts
-- (32 children, 6 sessions), COMMIT only if they match.
-- ============================================================================


-- ---------------------------------------------------------------------------
-- STEP 1 -- READ ONLY. Full scope report.
-- ---------------------------------------------------------------------------
CREATE TEMP TABLE tmp_child_ids (id uuid);
INSERT INTO tmp_child_ids VALUES
    ('01a054ea-86ff-7be8-87fc-84c5209d4941'), ('01a0a006-edd8-7ed3-b086-5c79617e120f'),
    ('01a0a008-754f-7ce1-8953-c899d66cea30'), ('01a054ea-883b-7bd8-ac45-5a7f24d0cda5'),
    ('01a08fcc-4e28-7f8e-b7d7-f1927efad533'), ('01a054ea-8bb7-7a90-9653-68e554626488'),
    ('01a0a00e-1016-7093-b07d-c4e987874cf6'), ('01a0a02d-c3c7-7068-b73d-918814b4e581'),
    ('01a054ea-8d27-70eb-b631-ba661772ed51'), ('01a08f0d-977c-7130-a845-a2b3e81f40bf'),
    ('01a0a3e0-7c79-78f7-b3f9-56cc8a8c962b'), ('01a054ea-88fd-7ca6-b929-cc67eaaea1e7'),
    ('01a08f8f-31fa-713e-93be-09e2dbe02e5b'), ('01a07b4f-332f-71ff-bf03-35560f95b175'),
    ('01a07b8c-b8fb-77c5-8a91-ce05cabc9807'), ('01a054ea-8873-7add-adf0-9836d79a105e'),
    ('01a08569-434b-70f6-95b5-c0aa78bb13a5'), ('01a054ea-88ef-7063-8a3c-5e50fd226fab'),
    ('01a080bb-441b-7639-81f5-733044232475'), ('01a054ea-85fb-7ea0-a7a9-fe079e18ecab'),
    ('01a0a453-1ac2-7e30-bb51-c2287976570f'), ('01a0a467-a2d8-74ab-8333-c29f460c1697'),
    ('01a054ea-8d00-713f-835a-c0a394e79fa1'), ('01a093dd-a465-75a8-8c4d-0f2e810ad362'),
    ('01a093dd-e94b-7f1f-a4e9-2ef5a1d2f84b'), ('01a054ea-89dd-7d9b-8ffd-012fa352eb64'),
    ('01a09b59-5464-70ec-a613-453f757c0409'), ('01a054ea-88d6-7d0e-a38a-b65340470950'),
    ('01a0969a-1fc9-71d4-b914-4abb39d8d059'), ('01a054ea-86cc-73c4-8f5a-2ff9b0571e72'),
    ('01a0a451-7bf9-74eb-aa27-f59776ce4df9'), ('01a0a4aa-d173-7f05-9ff4-10fd830ce40d');
-- Expect 32 rows.
SELECT count(*) AS child_ids_loaded FROM tmp_child_ids;

CREATE TEMP TABLE tmp_session_ids (id uuid);
INSERT INTO tmp_session_ids VALUES
    ('01a07f9e-c0eb-7b5c-b69e-f088e6648eb4'), ('01a08605-ee9e-717d-994d-c4db415b12dd'),
    ('01a0a3ab-cbcb-7f29-9081-a0c97b51e14d'), ('01a0a3ac-931d-7cee-9a95-3893282651d5'),
    ('01a0a641-cb96-7a34-91a5-cea8b079aeae'), ('01a0a643-167d-7dc9-978a-7aa99046f2a9');
-- Expect 6 rows.
SELECT count(*) AS session_ids_loaded FROM tmp_session_ids;

-- Row-level detail -- READ EVERY ROW. This is what's about to be destroyed.
SELECT
    c.id, c.first_name || ' ' || c.last_name AS child_name, c.parent_profile_id, c.academic_level,
    EXISTS (SELECT 1 FROM batch_enrollments be WHERE be.child_id = c.id AND be.status = 'Active') AS has_active_enrollment,
    EXISTS (SELECT 1 FROM invoices i WHERE i.child_id = c.id AND i.status IN ('Pending','PartiallyPaid','Overdue')) AS has_outstanding_invoice
FROM children c JOIN tmp_child_ids t ON t.id = c.id
ORDER BY child_name;

SELECT
    cs.id, b.name AS batch_name, cs.scheduled_start_at_utc, cs.status,
    EXISTS (SELECT 1 FROM session_recordings sr WHERE sr.class_session_id = cs.id AND (sr.expires_at_utc IS NULL OR sr.expires_at_utc > now())) AS has_recording
FROM class_sessions cs JOIN batches b ON b.id = cs.batch_id JOIN tmp_session_ids t ON t.id = cs.id
ORDER BY batch_name, cs.scheduled_start_at_utc;

CREATE TEMP TABLE tmp_demo_booking_ids AS
    SELECT id FROM demo_bookings WHERE class_session_id IN (SELECT id FROM tmp_session_ids);
CREATE TEMP TABLE tmp_invoice_ids AS
    SELECT id FROM invoices WHERE child_id IN (SELECT id FROM tmp_child_ids)
    UNION
    SELECT invoice_id FROM demo_bookings WHERE id IN (SELECT id FROM tmp_demo_booking_ids) AND invoice_id IS NOT NULL;
CREATE TEMP TABLE tmp_payment_transaction_ids AS
    SELECT id FROM payment_transactions WHERE invoice_id IN (SELECT id FROM tmp_invoice_ids);

SELECT 'refunds' AS table_name, count(*) FROM refunds WHERE payment_transaction_id IN (SELECT id FROM tmp_payment_transaction_ids)
UNION ALL SELECT 'payment_transactions', count(*) FROM payment_transactions WHERE id IN (SELECT id FROM tmp_payment_transaction_ids)
UNION ALL SELECT 'fee_suspensions', count(*) FROM fee_suspensions WHERE child_id IN (SELECT id FROM tmp_child_ids) OR invoice_id IN (SELECT id FROM tmp_invoice_ids)
UNION ALL SELECT 'demo_feedbacks', count(*) FROM demo_feedbacks WHERE demo_booking_id IN (SELECT id FROM tmp_demo_booking_ids)
UNION ALL SELECT 'demo_participants', count(*) FROM demo_participants WHERE demo_booking_id IN (SELECT id FROM tmp_demo_booking_ids)
UNION ALL SELECT 'demo_bookings', count(*) FROM demo_bookings WHERE id IN (SELECT id FROM tmp_demo_booking_ids)
UNION ALL SELECT 'invoices', count(*) FROM invoices WHERE id IN (SELECT id FROM tmp_invoice_ids)
UNION ALL SELECT 'progress_reports', count(*) FROM progress_reports WHERE child_id IN (SELECT id FROM tmp_child_ids)
UNION ALL SELECT 'subscriptions', count(*) FROM subscriptions WHERE child_id IN (SELECT id FROM tmp_child_ids)
UNION ALL SELECT 'batch_enrollments (INCLUDES ACTIVE ONES)', count(*) FROM batch_enrollments WHERE child_id IN (SELECT id FROM tmp_child_ids)
UNION ALL SELECT 'enrollment_forms', count(*) FROM enrollment_forms WHERE child_id IN (SELECT id FROM tmp_child_ids)
UNION ALL SELECT 'payout_items', count(*) FROM payout_items WHERE class_session_id IN (SELECT id FROM tmp_session_ids)
UNION ALL SELECT 'leave_request_sessions', count(*) FROM leave_request_sessions WHERE class_session_id IN (SELECT id FROM tmp_session_ids)
UNION ALL SELECT 'class_session_event_logs', count(*) FROM class_session_event_logs WHERE child_id IN (SELECT id FROM tmp_child_ids) OR class_session_id IN (SELECT id FROM tmp_session_ids)
UNION ALL SELECT 'engagement_events', count(*) FROM engagement_events WHERE child_id IN (SELECT id FROM tmp_child_ids) OR class_session_id IN (SELECT id FROM tmp_session_ids)
UNION ALL SELECT 'session_attendances', count(*) FROM session_attendances WHERE child_id IN (SELECT id FROM tmp_child_ids) OR class_session_id IN (SELECT id FROM tmp_session_ids)
UNION ALL SELECT 'student_awards', count(*) FROM student_awards WHERE child_id IN (SELECT id FROM tmp_child_ids) OR class_session_id IN (SELECT id FROM tmp_session_ids)
UNION ALL SELECT 'session_presentations', count(*) FROM session_presentations WHERE class_session_id IN (SELECT id FROM tmp_session_ids)
UNION ALL SELECT 'session_recordings (INCLUDES A COMPLETED CLASS RECORDING)', count(*) FROM session_recordings WHERE class_session_id IN (SELECT id FROM tmp_session_ids)
UNION ALL SELECT 'class_sessions.rescheduled_from_session_id pointers (self-FK, will be nulled)', count(*) FROM class_sessions WHERE rescheduled_from_session_id IN (SELECT id FROM tmp_session_ids)
UNION ALL SELECT 'class_sessions.carried_forward_from_session_id pointers (self-FK, will be nulled)', count(*) FROM class_sessions WHERE carried_forward_from_session_id IN (SELECT id FROM tmp_session_ids)
UNION ALL SELECT 'children (final target)', count(*) FROM children WHERE id IN (SELECT id FROM tmp_child_ids)
UNION ALL SELECT 'class_sessions (final target)', count(*) FROM class_sessions WHERE id IN (SELECT id FROM tmp_session_ids);

DROP TABLE tmp_child_ids, tmp_session_ids, tmp_demo_booking_ids, tmp_invoice_ids, tmp_payment_transaction_ids;


-- ---------------------------------------------------------------------------
-- STEP 2 -- THE HARD DELETE. Irreversible.
-- ---------------------------------------------------------------------------
BEGIN;

DROP TABLE IF EXISTS tmp_child_ids, tmp_session_ids, tmp_demo_booking_ids, tmp_invoice_ids, tmp_payment_transaction_ids CASCADE;

CREATE TEMP TABLE tmp_child_ids (id uuid) ON COMMIT DROP;
INSERT INTO tmp_child_ids VALUES
    ('01a054ea-86ff-7be8-87fc-84c5209d4941'), ('01a0a006-edd8-7ed3-b086-5c79617e120f'),
    ('01a0a008-754f-7ce1-8953-c899d66cea30'), ('01a054ea-883b-7bd8-ac45-5a7f24d0cda5'),
    ('01a08fcc-4e28-7f8e-b7d7-f1927efad533'), ('01a054ea-8bb7-7a90-9653-68e554626488'),
    ('01a0a00e-1016-7093-b07d-c4e987874cf6'), ('01a0a02d-c3c7-7068-b73d-918814b4e581'),
    ('01a054ea-8d27-70eb-b631-ba661772ed51'), ('01a08f0d-977c-7130-a845-a2b3e81f40bf'),
    ('01a0a3e0-7c79-78f7-b3f9-56cc8a8c962b'), ('01a054ea-88fd-7ca6-b929-cc67eaaea1e7'),
    ('01a08f8f-31fa-713e-93be-09e2dbe02e5b'), ('01a07b4f-332f-71ff-bf03-35560f95b175'),
    ('01a07b8c-b8fb-77c5-8a91-ce05cabc9807'), ('01a054ea-8873-7add-adf0-9836d79a105e'),
    ('01a08569-434b-70f6-95b5-c0aa78bb13a5'), ('01a054ea-88ef-7063-8a3c-5e50fd226fab'),
    ('01a080bb-441b-7639-81f5-733044232475'), ('01a054ea-85fb-7ea0-a7a9-fe079e18ecab'),
    ('01a0a453-1ac2-7e30-bb51-c2287976570f'), ('01a0a467-a2d8-74ab-8333-c29f460c1697'),
    ('01a054ea-8d00-713f-835a-c0a394e79fa1'), ('01a093dd-a465-75a8-8c4d-0f2e810ad362'),
    ('01a093dd-e94b-7f1f-a4e9-2ef5a1d2f84b'), ('01a054ea-89dd-7d9b-8ffd-012fa352eb64'),
    ('01a09b59-5464-70ec-a613-453f757c0409'), ('01a054ea-88d6-7d0e-a38a-b65340470950'),
    ('01a0969a-1fc9-71d4-b914-4abb39d8d059'), ('01a054ea-86cc-73c4-8f5a-2ff9b0571e72'),
    ('01a0a451-7bf9-74eb-aa27-f59776ce4df9'), ('01a0a4aa-d173-7f05-9ff4-10fd830ce40d');

CREATE TEMP TABLE tmp_session_ids (id uuid) ON COMMIT DROP;
INSERT INTO tmp_session_ids VALUES
    ('01a07f9e-c0eb-7b5c-b69e-f088e6648eb4'), ('01a08605-ee9e-717d-994d-c4db415b12dd'),
    ('01a0a3ab-cbcb-7f29-9081-a0c97b51e14d'), ('01a0a3ac-931d-7cee-9a95-3893282651d5'),
    ('01a0a641-cb96-7a34-91a5-cea8b079aeae'), ('01a0a643-167d-7dc9-978a-7aa99046f2a9');

CREATE TEMP TABLE tmp_demo_booking_ids ON COMMIT DROP AS
    SELECT id FROM demo_bookings WHERE class_session_id IN (SELECT id FROM tmp_session_ids);
CREATE TEMP TABLE tmp_invoice_ids ON COMMIT DROP AS
    SELECT id FROM invoices WHERE child_id IN (SELECT id FROM tmp_child_ids)
    UNION
    SELECT invoice_id FROM demo_bookings WHERE id IN (SELECT id FROM tmp_demo_booking_ids) AND invoice_id IS NOT NULL;
CREATE TEMP TABLE tmp_payment_transaction_ids ON COMMIT DROP AS
    SELECT id FROM payment_transactions WHERE invoice_id IN (SELECT id FROM tmp_invoice_ids);

DELETE FROM refunds WHERE payment_transaction_id IN (SELECT id FROM tmp_payment_transaction_ids);
DELETE FROM payment_transactions WHERE id IN (SELECT id FROM tmp_payment_transaction_ids);
DELETE FROM fee_suspensions WHERE child_id IN (SELECT id FROM tmp_child_ids) OR invoice_id IN (SELECT id FROM tmp_invoice_ids);
DELETE FROM demo_feedbacks WHERE demo_booking_id IN (SELECT id FROM tmp_demo_booking_ids);
DELETE FROM demo_participants WHERE demo_booking_id IN (SELECT id FROM tmp_demo_booking_ids);
DELETE FROM demo_bookings WHERE id IN (SELECT id FROM tmp_demo_booking_ids);
DELETE FROM invoices WHERE id IN (SELECT id FROM tmp_invoice_ids);
DELETE FROM progress_reports WHERE child_id IN (SELECT id FROM tmp_child_ids);
DELETE FROM subscriptions WHERE child_id IN (SELECT id FROM tmp_child_ids);
DELETE FROM batch_enrollments WHERE child_id IN (SELECT id FROM tmp_child_ids);
DELETE FROM enrollment_forms WHERE child_id IN (SELECT id FROM tmp_child_ids);
DELETE FROM payout_items WHERE class_session_id IN (SELECT id FROM tmp_session_ids);
DELETE FROM leave_request_sessions WHERE class_session_id IN (SELECT id FROM tmp_session_ids);
DELETE FROM class_session_event_logs WHERE child_id IN (SELECT id FROM tmp_child_ids) OR class_session_id IN (SELECT id FROM tmp_session_ids);
DELETE FROM engagement_events WHERE child_id IN (SELECT id FROM tmp_child_ids) OR class_session_id IN (SELECT id FROM tmp_session_ids);
DELETE FROM session_attendances WHERE child_id IN (SELECT id FROM tmp_child_ids) OR class_session_id IN (SELECT id FROM tmp_session_ids);
DELETE FROM student_awards WHERE child_id IN (SELECT id FROM tmp_child_ids) OR class_session_id IN (SELECT id FROM tmp_session_ids);
DELETE FROM session_presentations WHERE class_session_id IN (SELECT id FROM tmp_session_ids);
DELETE FROM session_recordings WHERE class_session_id IN (SELECT id FROM tmp_session_ids);

UPDATE class_sessions SET rescheduled_from_session_id = NULL
WHERE rescheduled_from_session_id IN (SELECT id FROM tmp_session_ids);
UPDATE class_sessions SET carried_forward_from_session_id = NULL
WHERE carried_forward_from_session_id IN (SELECT id FROM tmp_session_ids);

DELETE FROM children WHERE id IN (SELECT id FROM tmp_child_ids)
RETURNING id, first_name || ' ' || last_name AS child_name;
-- Expect 32 rows RETURNING above.

DELETE FROM class_sessions WHERE id IN (SELECT id FROM tmp_session_ids)
RETURNING id, batch_id, scheduled_start_at_utc;
-- Expect 6 rows RETURNING above.

-- COMMIT; only if both counts match exactly (32, then 6).
-- ROLLBACK; otherwise.
