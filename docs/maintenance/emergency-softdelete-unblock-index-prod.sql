-- ============================================================================
-- EMERGENCY, REVERSIBLE unblock: soft-delete one row from each remaining
-- duplicate (batch_id, scheduled_start_at_utc) group so the partial unique
-- index (WHERE is_deleted = false) can finally be created and the API can
-- start. This does NOT permanently decide which copy is correct -- these
-- are exactly the groups flagged NEEDS MANUAL REVIEW earlier (both copies
-- show real activity: Completed/InProgress status, or a recording), which
-- is why they were never included in the earlier hard-delete passes.
--
-- Soft-deleted rows are NOT gone -- is_deleted = true just excludes them
-- from the unique index and normal app queries. Once someone reviews each
-- pair and confirms which copy is correct, either restore the wrong one's
-- sibling was right (is_deleted = false again) or leave this as the final
-- answer and let it be genuinely cleaned up later.
--
-- Keeps the row that looks more "complete" per group (Completed over
-- InProgress/Rescheduled/Scheduled; has a recording over not); soft-deletes
-- the other. This is a judgment call made to unblock production immediately,
-- not a verified-correct resolution -- flag both rows in each group to
-- whoever reviews this next.
-- ============================================================================

BEGIN;

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
    ) AS has_recording
    FROM class_sessions cs
    JOIN dupe_groups dg ON dg.batch_id = cs.batch_id AND dg.scheduled_start_at_utc = cs.scheduled_start_at_utc
    WHERE cs.is_deleted = false
),
ranked AS (
    SELECT gr.id,
        row_number() OVER (
            PARTITION BY gr.batch_id, gr.scheduled_start_at_utc
            ORDER BY
                CASE gr.status
                    WHEN 'Completed' THEN 1
                    WHEN 'InProgress' THEN 2
                    WHEN 'Rescheduled' THEN 3
                    ELSE 4
                END,
                gr.has_recording DESC,
                gr.created_at_utc
        ) AS keep_rank
    FROM group_rows gr
)
UPDATE class_sessions cs
SET is_deleted = true, deleted_at_utc = now(), updated_at_utc = now()
FROM ranked r
WHERE r.id = cs.id AND r.keep_rank > 1
RETURNING cs.id, cs.batch_id, cs.scheduled_start_at_utc, cs.status;

-- Verify: should now return 0 rows.
SELECT batch_id, scheduled_start_at_utc, count(*)
FROM class_sessions
WHERE is_deleted = false AND batch_id IS NOT NULL
GROUP BY batch_id, scheduled_start_at_utc
HAVING count(*) > 1;

-- If the verification above returned 0 rows: COMMIT;
-- Otherwise: ROLLBACK;
