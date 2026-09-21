-- ============================================================================
-- STEP 2 (SCOPED) -- fixes ONLY the 4 batches confirmed to match Apurva
-- Rathore's reported bug. Deliberately excludes:
--   - the 3 "PALLAVI MAAM" group batches (Group B) -- unconfirmed, held back
--     pending confirmation that Pallavi was genuinely reassigned off them
--   - "QA Flow Test Batch" / "aashman jain" (Group C) -- test data, not
--     part of the reported issue
--
-- Wrapped in a transaction: review the RETURNING rows, then COMMIT or
-- ROLLBACK. Re-run STEP 1 from fix-drifted-session-teachers.sql afterward
-- to confirm these 4 batches no longer appear in the drift list.
-- ============================================================================

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
  AND cs.batch_id IN (
      '01a05508-af2a-73bd-b2cf-f0240e70eb50', -- Sahas Anhad Singh -> Parul Ghai
      '01a05509-6be5-7a59-8153-335f360c2b0d', -- Mohammed Ibaadullah khan 5 -> Divya Kukreja
      '01a05509-7b7e-7323-acc1-33b6d1753697', -- Ishaany patra -> Pallavi Jayini
      '01a05508-2754-7ae9-a116-36e9fbd2e9ec'  -- NYREKAH MAINI -> Priya Pandey
  )
RETURNING cs.id AS session_id, cs.batch_id, cs.teacher_profile_id AS new_teacher_profile_id, cs.scheduled_start_at_utc;

-- Expect exactly the rows from Group A in the earlier review (~100+ rows across
-- these 4 batches, every future Scheduled/CarriedForward session). If the count
-- or the batch_ids look different from that, ROLLBACK and let me know before
-- trying again.
--
--   COMMIT;
--   -- or --
--   ROLLBACK;
