-- ============================================================================
-- ONE-TIME, IRREVERSIBLE data repair: HARD DELETE of duplicate Child and
-- ClassSession rows and everything that references them.
-- ============================================================================
--
-- *** THIS SCRIPT PERMANENTLY DESTROYS DATA. THERE IS NO UNDO. ***
-- A soft-delete version of this cleanup (is_deleted = true, fully reversible)
-- already exists at docs/maintenance/cleanup-migration-duplicates-2026-09-17.sql
-- covering the exact same 95 children / 85 sessions. This script was written
-- because hard delete was explicitly requested in place of that one.
--
-- Background: the "Bulk Import Students" endpoint (EnrollmentService.
-- BulkImportStudentsAsync) had no check for an existing child under the same
-- parent before inserting a new one, so re-running/re-uploading a CSV
-- silently created duplicate copies of the same child. Separately, 84
-- batches ended up with every scheduled class duplicated from a
-- GenerateScheduleAsync double-submit race. Both root causes are already
-- fixed in application code; this script only cleans up rows created before
-- those fixes existed.
--
-- The 95 child ids and 85 session ids below are exactly the rows verified
-- safe against live production data in duplicate_children_review_v3.csv /
-- the session review (both dated 2026-09-17) -- rows with any active
-- enrollment, outstanding invoice, non-terminal session status, or an
-- unexpired recording were excluded. 32 additional child rows (13 groups)
-- and a handful of session rows were flagged NEEDS MANUAL REVIEW and are
-- deliberately NOT included here.
--
-- WHY THIS IS MORE THAN "DELETE FROM children ...":
-- Every foreign key in this database uses DeleteBehavior.Restrict
-- (iucs.readernest.domain/Data/ReaderNestDbContext.cs ~line 148) -- nothing
-- cascades automatically. A plain DELETE on children/class_sessions would
-- fail the moment ANY row in ANY dependent table still points at that id,
-- regardless of that row's own status. The full dependency graph (traced
-- from every entity under iucs.readernest.domain/Entities/) is:
--
--   Child (children)
--     -> batch_enrollments.child_id            [required]
--     -> enrollment_forms.child_id             [nullable]
--     -> fee_suspensions.child_id              [nullable]
--     -> invoices.child_id                     [nullable]
--          -> payment_transactions.invoice_id  [required]
--               -> refunds.payment_transaction_id [required]
--          -> fee_suspensions.invoice_id       [nullable]
--          -> demo_bookings.invoice_id         [nullable]  (cross-chain, see below)
--     -> subscriptions.child_id                [required]  (invoices.subscription_id points here, so invoices must go first)
--     -> progress_reports.child_id             [required]
--     -> class_session_event_logs.child_id     [nullable, dual FK]
--     -> engagement_events.child_id            [nullable, dual FK]
--     -> session_attendances.child_id          [nullable, dual FK]
--     -> student_awards.child_id               [nullable, dual FK]
--
--   ClassSession (class_sessions)
--     -> leave_request_sessions.class_session_id [required]
--     -> demo_bookings.class_session_id        [nullable]
--          -> demo_participants.demo_booking_id [required]
--          -> demo_feedbacks.demo_booking_id    [required]
--     -> payout_items.class_session_id         [nullable]  (Payout itself is out of scope/untouched)
--     -> class_session_event_logs.class_session_id [required, dual FK]
--     -> engagement_events.class_session_id    [required, dual FK]
--     -> session_attendances.class_session_id  [required, dual FK]
--     -> session_presentations.class_session_id [required]
--     -> session_recordings.class_session_id   [required]
--     -> student_awards.class_session_id       [nullable, dual FK]
--
-- No SoftDeleteInterceptor or cascade logic exists anywhere in the codebase
-- (checked AuditableEntityInterceptor and the rest of iucs.readernest.domain
-- / iucs.readernest.api) -- deletes must walk this graph manually, which is
-- exactly what STEP 2 below does, leaves-first.
--
-- HOW TO USE
--   1. Run STEP 1 (read-only) first. It reports, for every target id, how
--      many rows in each dependent table would be destroyed. Read it. Some
--      of these tables (invoices, payment_transactions, refunds,
--      subscriptions) hold real financial history, not just junk duplicate
--      rows -- this is the last point at which that's inspectable.
--   2. Run STEP 2 inside the transaction (BEGIN ... COMMIT) exactly as
--      written. Read every RETURNING count. Only type COMMIT if every count
--      matches STEP 1's report; otherwise type ROLLBACK.
--   3. STEP 3 (optional) records the cleanup in the app's own audit trail.
-- ============================================================================


-- ---------------------------------------------------------------------------
-- STEP 1 -- READ ONLY. Full scope report: how many rows in every dependent
-- table are about to be permanently destroyed.
-- ---------------------------------------------------------------------------
CREATE TEMP TABLE tmp_child_ids (id uuid);
INSERT INTO tmp_child_ids VALUES
    ('01a054ea-8d3d-7963-811e-21d1544976fd'), ('01a0914a-4eb4-7e69-b0f7-7638d5de7ca6'),
    ('01a0aa95-5f08-7f14-80c6-ac0abdd2609e'), ('01a099fb-19f8-7a23-a26e-9e9732dde244'),
    ('01a054ea-85d0-7f08-889d-1a65172b836a'), ('01a054ea-8c54-7c6e-96db-00ebb6bde978'),
    ('01a0ae08-746a-7a2d-9614-2f24030a17ad'), ('01a054ea-870f-7748-8138-0fd71d2103b4'),
    ('01a0a382-29f6-7768-ab24-8f87fd7f3e66'), ('01a0915a-91e7-70f2-8398-7fda59dd143d'),
    ('01a054ea-868b-718d-9e69-3eb038254840'), ('01a054ea-8b9f-7bbf-9985-8766a94966dc'),
    ('01a08566-27f4-7472-b60d-16e03538c4b1'), ('01a099f3-f7cb-7293-9b0d-96d1ede8c56c'),
    ('01a0a5da-5440-7c9c-b6e4-f82ffd790b33'), ('01a0af36-15d8-790c-a11f-61b5e16bcedc'),
    ('01a0a5b8-0499-70a4-a2c2-1b76d90b6060'), ('01a0af36-3ef6-78eb-a6fb-faa336ba4489'),
    ('01a054ea-8763-7126-8cb7-055c14728978'), ('01a054ea-85ee-7edc-bc70-eb7eb22af0d8'),
    ('01a09581-e5b1-75e8-87f5-367986e49881'), ('01a0a968-b663-7624-93b7-abbe6efe0651'),
    ('01a054ea-8694-79e7-bfb7-0ae99587a55a'), ('01a054ea-8af3-70ac-8cb6-b10a7c50d7d3'),
    ('01a093e5-a711-7c31-8942-6e2926c8cb65'), ('01a0915e-c1c7-7034-9357-930150898acd'),
    ('01a08ad1-392c-7887-bea7-9d5b55a098d4'), ('01a099d5-482d-748f-bb0c-f3ceb291d543'),
    ('01a095f1-22ef-7b15-8993-6212a0cd058d'), ('01a099f9-19d1-74bb-bb6f-8b8114db07d2'),
    ('01a084d6-5a73-7c14-8e2d-3ea41ca483c4'), ('01a0871e-fa14-726d-a4ac-aaac37a8fa67'),
    ('01a054ea-85f7-7185-ad18-04d1ee6208cc'), ('01a054ea-86a2-7183-bd08-f6e5ba135741'),
    ('01a054ea-8d4f-7f29-8084-58da9fb16253'), ('01a054ea-8d69-749a-88e1-bbfa59891e85'),
    ('01a093d7-5771-7264-8766-4cc9da2486d0'), ('01a054ea-85b9-7d5b-8091-8c0f1e6b1f4a'),
    ('01a054ea-85b1-7764-b466-a91d2a8343c2'), ('01a054ea-860a-7f1c-a206-4fa3b936a169'),
    ('01a099ee-8bcd-7819-8db0-e05812052500'), ('01a0adb5-e0e9-7dac-803c-3f00fe17ac23'),
    ('01a09401-8abd-719a-8f4d-038466abf2e8'), ('01a054ea-875d-7cad-8348-0c5e62756bcd'),
    ('01a0917c-6851-7f48-ac7f-37ab4913d6eb'), ('01a054ea-86b0-7493-9fa9-4d97a18f7adf'),
    ('01a054ea-8903-7821-9d1d-58d7476521be'), ('01a054ea-879a-7751-ade5-886562054fea'),
    ('01a054ea-87a1-7170-b5bc-e32e59d6dbb3'), ('01a09679-abb6-7beb-a461-60a3db9d9a2b'),
    ('01a093e2-3b84-7f08-832a-9446fa1dd1ab'), ('01a093ee-9705-7cf8-b4cd-3cbcea67dacb'),
    ('01a0aebd-2991-766a-8d76-148837233f1d'), ('01a054ea-86a6-74de-bc1a-383b86e53d1e'),
    ('01a09163-34c8-76ac-bd44-481a4e86575d'), ('01a09580-4933-79fe-b6f6-7f4d37b153d7'),
    ('01a0ad76-5fa8-7766-a244-aa7d896b706c'), ('01a054ea-8690-77a8-951f-e0008ca313cd'),
    ('01a054ea-87b6-7c26-ad58-3a787110cf9a'), ('01a0a00b-dbf0-779e-8c6b-118fb021166e'),
    ('01a054ea-87d6-7de3-8f4e-2bf292a266d2'), ('01a054ea-8757-79fd-b562-3082c36888b0'),
    ('01a054ea-85c1-7e06-aac7-25b9102f643b'), ('01a054ea-8ba4-79ad-be03-2cb90816d690'),
    ('01a0a0cf-b0cd-7fd7-bbf3-bb81cb2ba7bb'), ('01a054ea-86d8-7578-b6ab-501bdcaaa8da'),
    ('01a0a0c5-9e11-7d6b-882f-fee82fffe3a2'), ('01a0a89f-957c-7646-a910-e88571fedf6a'),
    ('01a054ea-8cda-740a-8749-4502e412f1ab'), ('01a099b4-264c-71ff-8b2b-07df45669c99'),
    ('01a08619-2585-73fa-a409-678df38e7c1a'), ('01a0871d-f9c3-73a4-a268-7a81e60e9b9c'),
    ('01a054ea-87e3-7e60-9c22-4a5e7013e335'), ('01a054ea-87df-7024-b62c-3fd04a596716'),
    ('01a0a013-81c7-758f-a555-960b78022db2'), ('01a054ea-87bc-7eb1-bd82-4690115142fa'),
    ('01a09b59-6922-7cb6-b0c9-0758c5cf7a4e'), ('01a099e0-98b8-7e7a-8880-df03c7f83395'),
    ('01a0a507-0ea8-7199-b207-a7d368bd7cde'), ('01a09672-ea71-73d1-8919-a17665949441'),
    ('01a0a0c5-bcd3-7357-92cc-fdc560566f3a'), ('01a054ea-8786-7934-b8ee-7bd1e8ab0692'),
    ('01a054ea-8867-74e6-bbe7-ef7e26e49e88'), ('01a054ea-85da-70c9-a25a-ac296eadb62c'),
    ('01a08ad5-1aac-7775-9e0c-00a1b95974fe'), ('01a0917f-a3dc-73e6-bfca-039cd57599b2'),
    ('01a0aa4f-d4d5-7542-be4c-0bfeefe55293'), ('01a054ea-890a-7596-adac-608742b2c6a5'),
    ('01a09b6c-9da9-72a3-893e-75a8e783f6df'), ('01a08ad5-3884-7666-ab2e-a1c09b86280f'),
    ('01a054ea-886b-7e8b-b223-47460df723af'), ('01a099db-24b1-7933-b55f-217961d3c1a2'),
    ('01a054ea-86e7-7dd5-9b79-ffc3f484d57e'), ('01a09b52-ba4b-7871-9fd3-658201cfd12d'),
    ('01a08725-7ebb-7293-af4b-768f8ae9b7f8');
-- Expect 95 rows.
SELECT count(*) AS child_ids_loaded FROM tmp_child_ids;

CREATE TEMP TABLE tmp_session_ids (id uuid);
INSERT INTO tmp_session_ids VALUES
    ('01a066c8-5ffa-790f-a633-c1a8d0120d8b'), ('01a07bf1-8fa2-71ce-ab34-2de611d1d508'),
    ('01a07f9e-c0e9-77a9-ae49-9a4397ae2a27'), ('01a07bbb-4a22-72cf-beeb-b3d0c277dd02'),
    ('01a0a4c5-de13-7fa5-ada8-5a385367a22d'), ('01a0a9de-84e7-7602-9373-6afd3a328bba'),
    ('01a0aa4c-93c0-7931-b335-d31be161b319'), ('01a09ea5-7d02-753e-b2ac-b11ad615c592'),
    ('01a0a495-3442-71d0-8a3d-01ab71eff5d4'), ('01a08605-daa4-7c5d-9606-9abc7aa1f37a'),
    ('01a08059-fa11-76d3-bb1a-3d24f9395bbd'), ('01a0af38-7627-7090-b6e9-b5adf4cc7a2e'),
    ('01a08ad3-a89c-7e4b-a0eb-09624609602c'), ('01a08ad3-b1ed-7e6f-98d4-3ee33df30c4d'),
    ('01a08adc-ec3d-7aa9-9bc9-a5a3d23d531e'), ('01a08adc-f080-78c0-b58a-b06a1c0edfb4'),
    ('01a08ae6-27ab-77fb-8f84-b08d642aec10'), ('01a08ae6-2c54-7d05-8118-2524dcf8af31'),
    ('01a08aef-6508-75eb-ab9d-42bf54eebfb7'), ('01a08aef-69b3-7755-b981-e43ed7e693b2'),
    ('01a08af8-bd7b-70ba-8787-c8d5eee07b97'), ('01a08af8-c2b6-79c8-a63c-4f063ea81b3f'),
    ('01a08b01-fb3a-7823-b236-ada86f6f9363'), ('01a08b02-0041-753a-b40b-ddf7ddceb69d'),
    ('01a08b0b-34a9-752c-ad1f-9ec160237ccf'), ('01a08b0b-39aa-704e-9919-4c95ab3fd50e'),
    ('01a08b14-73e3-7aed-9cfc-154b6f8e9b3a'), ('01a08b14-7b93-7b80-8053-768a511aba3c'),
    ('01a08b1d-aba7-7b62-9ad5-b74135d815b1'), ('01a0aeae-d457-7a94-9ef8-4aa6ee10eacc'),
    ('01a0af41-a9fc-7799-a216-11285f41b44d'), ('01a0a2c4-ed56-7476-9c27-c58e4584c8e5'),
    ('01a090f7-b156-7989-ae52-92ac3f602f0e'), ('01a0aa15-8bd8-78cf-988b-b9f30e43c773'),
    ('01a080aa-ebcc-765c-861d-f47f8c0cd0e5'), ('01a08af8-b14d-7f13-9f36-a75fdbdd88d7'),
    ('01a0953d-576e-7135-b4a6-38840f5f49ed'), ('01a0a4b3-868f-7216-9b0b-98d23a291995'),
    ('01a0af01-445a-711b-a502-a3c6a8db86ee'), ('01a0af01-54c5-7b4c-84f2-26a8086ad84a'),
    ('01a08061-f9f7-74ef-92ff-2eca2fbfc7c4'), ('01a0805d-7bd0-7cfc-b39c-1b73eb00e70d'),
    ('01a0805d-e790-7f0b-b1e7-151a1cbcd641'), ('01a0805f-5d72-75c7-8ece-54f0950bc80c'),
    ('01a07bd6-1140-7265-be09-9e913138dbe4'), ('01a08621-7323-7397-901e-1f194ad1e32a'),
    ('01a08059-fa11-7978-b055-add66d96093e'), ('01a08059-fa11-7d7b-812c-01b86ca048b7'),
    ('01a0aa31-0c01-7344-b6d6-45710d409a58'), ('01a0aa31-12f2-7e54-b570-3e3d89cdd4b2'),
    ('01a0805b-53f7-7caf-ab08-40da3376c89e'), ('01a0805f-3543-7b78-8a87-10a79bdecb93'),
    ('01a0805f-849f-78aa-85fb-f8bd1acbcde1'), ('01a0a58f-7572-70fc-89de-1763d9310b19'),
    ('01a0a9e7-b215-7448-8a6a-79431ad9f928'), ('01a08aef-64f5-7039-a203-c726d39a5265'),
    ('01a09012-9d47-7f43-8983-967b19a460ab'), ('01a0aef8-179a-7eec-b57f-79f7e8c527eb'),
    ('01a0953f-2c92-7aa7-b3b1-b2fffb391afe'), ('01a09040-6d25-7b0f-af9d-af5e6b724325'),
    ('01a08059-fa11-743c-aed2-dcc3f825cb7d'), ('01a0805b-53f7-711b-af3b-3b488e351c94'),
    ('01a0805f-3543-7ecf-afbc-b06c06f3669a'), ('01a0805f-849f-71aa-82e5-0c06df29f9cc'),
    ('01a0a7d5-476f-7951-839d-334cd463a241'), ('01a08f24-7a75-7b7d-b122-a0f4d8e409de'),
    ('01a08059-fa11-7432-b279-522377864023'), ('01a0a9f0-df15-718e-b881-7590201deed7'),
    ('01a080e1-eca3-767d-bb7d-c17bbd1019ff'), ('01a0a4ea-82a3-7f6b-99ba-20b84825e35f'),
    ('01a0a4ea-8abf-7cfe-9596-fae1e559294e'), ('01a0af38-64a0-75ab-a52a-b768e6e283c0'),
    ('01a0a58f-7187-7844-a94e-53b49c18b8d4'), ('01a0ae95-2c1e-7eea-b34b-f575e58d4cc8'),
    ('01a0a9fa-0f4f-7c4f-85bb-b337aeb07077'), ('01a0a546-2ed9-78b2-80d9-392e98e2887c'),
    ('01a0a2fb-e848-76c0-b0a0-5c296805e656'), ('01a0a826-59ca-7291-9eab-bc00dd4ec235'),
    ('01a0af01-5cb8-7759-bca7-b51ff1c029f0'), ('01a0af6f-7ab0-748c-9a35-543f6159f1e2'),
    ('01a09009-6974-767e-b794-fb2bc3407e14'), ('01a0a9cc-1295-7da6-b434-fbeb5d1bcd53'),
    ('01a0ac6c-5971-74cc-83d7-56d624b1af96'), ('01a0a5aa-fadb-7342-b554-a112239c7a8e'),
    ('01a099b2-b6b5-7d45-b413-9010e91ae5be');
-- Expect 85 rows.
SELECT count(*) AS session_ids_loaded FROM tmp_session_ids;

CREATE TEMP TABLE tmp_demo_booking_ids AS
    SELECT id FROM demo_bookings WHERE class_session_id IN (SELECT id FROM tmp_session_ids);

CREATE TEMP TABLE tmp_invoice_ids AS
    SELECT id FROM invoices WHERE child_id IN (SELECT id FROM tmp_child_ids)
    UNION
    SELECT invoice_id FROM demo_bookings
        WHERE id IN (SELECT id FROM tmp_demo_booking_ids) AND invoice_id IS NOT NULL;

CREATE TEMP TABLE tmp_payment_transaction_ids AS
    SELECT id FROM payment_transactions WHERE invoice_id IN (SELECT id FROM tmp_invoice_ids);

-- Full scope report -- READ EVERY LINE before touching STEP 2.
SELECT 'refunds' AS table_name, count(*) FROM refunds WHERE payment_transaction_id IN (SELECT id FROM tmp_payment_transaction_ids)
UNION ALL
SELECT 'payment_transactions', count(*) FROM payment_transactions WHERE id IN (SELECT id FROM tmp_payment_transaction_ids)
UNION ALL
SELECT 'fee_suspensions', count(*) FROM fee_suspensions WHERE child_id IN (SELECT id FROM tmp_child_ids) OR invoice_id IN (SELECT id FROM tmp_invoice_ids)
UNION ALL
SELECT 'demo_feedbacks', count(*) FROM demo_feedbacks WHERE demo_booking_id IN (SELECT id FROM tmp_demo_booking_ids)
UNION ALL
SELECT 'demo_participants', count(*) FROM demo_participants WHERE demo_booking_id IN (SELECT id FROM tmp_demo_booking_ids)
UNION ALL
SELECT 'demo_bookings', count(*) FROM demo_bookings WHERE id IN (SELECT id FROM tmp_demo_booking_ids)
UNION ALL
SELECT 'invoices', count(*) FROM invoices WHERE id IN (SELECT id FROM tmp_invoice_ids)
UNION ALL
SELECT 'progress_reports', count(*) FROM progress_reports WHERE child_id IN (SELECT id FROM tmp_child_ids)
UNION ALL
SELECT 'subscriptions', count(*) FROM subscriptions WHERE child_id IN (SELECT id FROM tmp_child_ids)
UNION ALL
SELECT 'batch_enrollments', count(*) FROM batch_enrollments WHERE child_id IN (SELECT id FROM tmp_child_ids)
UNION ALL
SELECT 'enrollment_forms', count(*) FROM enrollment_forms WHERE child_id IN (SELECT id FROM tmp_child_ids)
UNION ALL
SELECT 'payout_items', count(*) FROM payout_items WHERE class_session_id IN (SELECT id FROM tmp_session_ids)
UNION ALL
SELECT 'leave_request_sessions', count(*) FROM leave_request_sessions WHERE class_session_id IN (SELECT id FROM tmp_session_ids)
UNION ALL
SELECT 'class_session_event_logs', count(*) FROM class_session_event_logs WHERE child_id IN (SELECT id FROM tmp_child_ids) OR class_session_id IN (SELECT id FROM tmp_session_ids)
UNION ALL
SELECT 'engagement_events', count(*) FROM engagement_events WHERE child_id IN (SELECT id FROM tmp_child_ids) OR class_session_id IN (SELECT id FROM tmp_session_ids)
UNION ALL
SELECT 'session_attendances', count(*) FROM session_attendances WHERE child_id IN (SELECT id FROM tmp_child_ids) OR class_session_id IN (SELECT id FROM tmp_session_ids)
UNION ALL
SELECT 'student_awards', count(*) FROM student_awards WHERE child_id IN (SELECT id FROM tmp_child_ids) OR class_session_id IN (SELECT id FROM tmp_session_ids)
UNION ALL
SELECT 'session_presentations', count(*) FROM session_presentations WHERE class_session_id IN (SELECT id FROM tmp_session_ids)
UNION ALL
SELECT 'session_recordings', count(*) FROM session_recordings WHERE class_session_id IN (SELECT id FROM tmp_session_ids)
UNION ALL
SELECT 'class_sessions.rescheduled_from_session_id pointers (self-FK, will be nulled)', count(*) FROM class_sessions WHERE rescheduled_from_session_id IN (SELECT id FROM tmp_session_ids)
UNION ALL
SELECT 'class_sessions.carried_forward_from_session_id pointers (self-FK, will be nulled)', count(*) FROM class_sessions WHERE carried_forward_from_session_id IN (SELECT id FROM tmp_session_ids)
UNION ALL
SELECT 'children (final target)', count(*) FROM children WHERE id IN (SELECT id FROM tmp_child_ids)
UNION ALL
SELECT 'class_sessions (final target)', count(*) FROM class_sessions WHERE id IN (SELECT id FROM tmp_session_ids);

-- Any non-zero count above for invoices / payment_transactions / refunds /
-- subscriptions is real financial history that will be permanently gone.
-- Any non-zero count for progress_reports / session_attendances is real
-- academic history that will be permanently gone. There is no undo past
-- this point.

DROP TABLE tmp_child_ids, tmp_session_ids, tmp_demo_booking_ids, tmp_invoice_ids, tmp_payment_transaction_ids;


-- ---------------------------------------------------------------------------
-- STEP 2 -- THE HARD DELETE. Irreversible. Wrapped in a transaction only so
-- you can inspect the RETURNING/ROW COUNT output and ROLLBACK instead of
-- COMMIT if anything looks wrong -- once COMMIT runs, this data cannot be
-- recovered from this database.
-- ---------------------------------------------------------------------------
BEGIN;

DROP TABLE IF EXISTS tmp_child_ids, tmp_session_ids, tmp_demo_booking_ids, tmp_invoice_ids, tmp_payment_transaction_ids CASCADE;

CREATE TEMP TABLE tmp_child_ids (id uuid) ON COMMIT DROP;
INSERT INTO tmp_child_ids VALUES
    ('01a054ea-8d3d-7963-811e-21d1544976fd'), ('01a0914a-4eb4-7e69-b0f7-7638d5de7ca6'),
    ('01a0aa95-5f08-7f14-80c6-ac0abdd2609e'), ('01a099fb-19f8-7a23-a26e-9e9732dde244'),
    ('01a054ea-85d0-7f08-889d-1a65172b836a'), ('01a054ea-8c54-7c6e-96db-00ebb6bde978'),
    ('01a0ae08-746a-7a2d-9614-2f24030a17ad'), ('01a054ea-870f-7748-8138-0fd71d2103b4'),
    ('01a0a382-29f6-7768-ab24-8f87fd7f3e66'), ('01a0915a-91e7-70f2-8398-7fda59dd143d'),
    ('01a054ea-868b-718d-9e69-3eb038254840'), ('01a054ea-8b9f-7bbf-9985-8766a94966dc'),
    ('01a08566-27f4-7472-b60d-16e03538c4b1'), ('01a099f3-f7cb-7293-9b0d-96d1ede8c56c'),
    ('01a0a5da-5440-7c9c-b6e4-f82ffd790b33'), ('01a0af36-15d8-790c-a11f-61b5e16bcedc'),
    ('01a0a5b8-0499-70a4-a2c2-1b76d90b6060'), ('01a0af36-3ef6-78eb-a6fb-faa336ba4489'),
    ('01a054ea-8763-7126-8cb7-055c14728978'), ('01a054ea-85ee-7edc-bc70-eb7eb22af0d8'),
    ('01a09581-e5b1-75e8-87f5-367986e49881'), ('01a0a968-b663-7624-93b7-abbe6efe0651'),
    ('01a054ea-8694-79e7-bfb7-0ae99587a55a'), ('01a054ea-8af3-70ac-8cb6-b10a7c50d7d3'),
    ('01a093e5-a711-7c31-8942-6e2926c8cb65'), ('01a0915e-c1c7-7034-9357-930150898acd'),
    ('01a08ad1-392c-7887-bea7-9d5b55a098d4'), ('01a099d5-482d-748f-bb0c-f3ceb291d543'),
    ('01a095f1-22ef-7b15-8993-6212a0cd058d'), ('01a099f9-19d1-74bb-bb6f-8b8114db07d2'),
    ('01a084d6-5a73-7c14-8e2d-3ea41ca483c4'), ('01a0871e-fa14-726d-a4ac-aaac37a8fa67'),
    ('01a054ea-85f7-7185-ad18-04d1ee6208cc'), ('01a054ea-86a2-7183-bd08-f6e5ba135741'),
    ('01a054ea-8d4f-7f29-8084-58da9fb16253'), ('01a054ea-8d69-749a-88e1-bbfa59891e85'),
    ('01a093d7-5771-7264-8766-4cc9da2486d0'), ('01a054ea-85b9-7d5b-8091-8c0f1e6b1f4a'),
    ('01a054ea-85b1-7764-b466-a91d2a8343c2'), ('01a054ea-860a-7f1c-a206-4fa3b936a169'),
    ('01a099ee-8bcd-7819-8db0-e05812052500'), ('01a0adb5-e0e9-7dac-803c-3f00fe17ac23'),
    ('01a09401-8abd-719a-8f4d-038466abf2e8'), ('01a054ea-875d-7cad-8348-0c5e62756bcd'),
    ('01a0917c-6851-7f48-ac7f-37ab4913d6eb'), ('01a054ea-86b0-7493-9fa9-4d97a18f7adf'),
    ('01a054ea-8903-7821-9d1d-58d7476521be'), ('01a054ea-879a-7751-ade5-886562054fea'),
    ('01a054ea-87a1-7170-b5bc-e32e59d6dbb3'), ('01a09679-abb6-7beb-a461-60a3db9d9a2b'),
    ('01a093e2-3b84-7f08-832a-9446fa1dd1ab'), ('01a093ee-9705-7cf8-b4cd-3cbcea67dacb'),
    ('01a0aebd-2991-766a-8d76-148837233f1d'), ('01a054ea-86a6-74de-bc1a-383b86e53d1e'),
    ('01a09163-34c8-76ac-bd44-481a4e86575d'), ('01a09580-4933-79fe-b6f6-7f4d37b153d7'),
    ('01a0ad76-5fa8-7766-a244-aa7d896b706c'), ('01a054ea-8690-77a8-951f-e0008ca313cd'),
    ('01a054ea-87b6-7c26-ad58-3a787110cf9a'), ('01a0a00b-dbf0-779e-8c6b-118fb021166e'),
    ('01a054ea-87d6-7de3-8f4e-2bf292a266d2'), ('01a054ea-8757-79fd-b562-3082c36888b0'),
    ('01a054ea-85c1-7e06-aac7-25b9102f643b'), ('01a054ea-8ba4-79ad-be03-2cb90816d690'),
    ('01a0a0cf-b0cd-7fd7-bbf3-bb81cb2ba7bb'), ('01a054ea-86d8-7578-b6ab-501bdcaaa8da'),
    ('01a0a0c5-9e11-7d6b-882f-fee82fffe3a2'), ('01a0a89f-957c-7646-a910-e88571fedf6a'),
    ('01a054ea-8cda-740a-8749-4502e412f1ab'), ('01a099b4-264c-71ff-8b2b-07df45669c99'),
    ('01a08619-2585-73fa-a409-678df38e7c1a'), ('01a0871d-f9c3-73a4-a268-7a81e60e9b9c'),
    ('01a054ea-87e3-7e60-9c22-4a5e7013e335'), ('01a054ea-87df-7024-b62c-3fd04a596716'),
    ('01a0a013-81c7-758f-a555-960b78022db2'), ('01a054ea-87bc-7eb1-bd82-4690115142fa'),
    ('01a09b59-6922-7cb6-b0c9-0758c5cf7a4e'), ('01a099e0-98b8-7e7a-8880-df03c7f83395'),
    ('01a0a507-0ea8-7199-b207-a7d368bd7cde'), ('01a09672-ea71-73d1-8919-a17665949441'),
    ('01a0a0c5-bcd3-7357-92cc-fdc560566f3a'), ('01a054ea-8786-7934-b8ee-7bd1e8ab0692'),
    ('01a054ea-8867-74e6-bbe7-ef7e26e49e88'), ('01a054ea-85da-70c9-a25a-ac296eadb62c'),
    ('01a08ad5-1aac-7775-9e0c-00a1b95974fe'), ('01a0917f-a3dc-73e6-bfca-039cd57599b2'),
    ('01a0aa4f-d4d5-7542-be4c-0bfeefe55293'), ('01a054ea-890a-7596-adac-608742b2c6a5'),
    ('01a09b6c-9da9-72a3-893e-75a8e783f6df'), ('01a08ad5-3884-7666-ab2e-a1c09b86280f'),
    ('01a054ea-886b-7e8b-b223-47460df723af'), ('01a099db-24b1-7933-b55f-217961d3c1a2'),
    ('01a054ea-86e7-7dd5-9b79-ffc3f484d57e'), ('01a09b52-ba4b-7871-9fd3-658201cfd12d'),
    ('01a08725-7ebb-7293-af4b-768f8ae9b7f8');

CREATE TEMP TABLE tmp_session_ids (id uuid) ON COMMIT DROP;
INSERT INTO tmp_session_ids VALUES
    ('01a066c8-5ffa-790f-a633-c1a8d0120d8b'), ('01a07bf1-8fa2-71ce-ab34-2de611d1d508'),
    ('01a07f9e-c0e9-77a9-ae49-9a4397ae2a27'), ('01a07bbb-4a22-72cf-beeb-b3d0c277dd02'),
    ('01a0a4c5-de13-7fa5-ada8-5a385367a22d'), ('01a0a9de-84e7-7602-9373-6afd3a328bba'),
    ('01a0aa4c-93c0-7931-b335-d31be161b319'), ('01a09ea5-7d02-753e-b2ac-b11ad615c592'),
    ('01a0a495-3442-71d0-8a3d-01ab71eff5d4'), ('01a08605-daa4-7c5d-9606-9abc7aa1f37a'),
    ('01a08059-fa11-76d3-bb1a-3d24f9395bbd'), ('01a0af38-7627-7090-b6e9-b5adf4cc7a2e'),
    ('01a08ad3-a89c-7e4b-a0eb-09624609602c'), ('01a08ad3-b1ed-7e6f-98d4-3ee33df30c4d'),
    ('01a08adc-ec3d-7aa9-9bc9-a5a3d23d531e'), ('01a08adc-f080-78c0-b58a-b06a1c0edfb4'),
    ('01a08ae6-27ab-77fb-8f84-b08d642aec10'), ('01a08ae6-2c54-7d05-8118-2524dcf8af31'),
    ('01a08aef-6508-75eb-ab9d-42bf54eebfb7'), ('01a08aef-69b3-7755-b981-e43ed7e693b2'),
    ('01a08af8-bd7b-70ba-8787-c8d5eee07b97'), ('01a08af8-c2b6-79c8-a63c-4f063ea81b3f'),
    ('01a08b01-fb3a-7823-b236-ada86f6f9363'), ('01a08b02-0041-753a-b40b-ddf7ddceb69d'),
    ('01a08b0b-34a9-752c-ad1f-9ec160237ccf'), ('01a08b0b-39aa-704e-9919-4c95ab3fd50e'),
    ('01a08b14-73e3-7aed-9cfc-154b6f8e9b3a'), ('01a08b14-7b93-7b80-8053-768a511aba3c'),
    ('01a08b1d-aba7-7b62-9ad5-b74135d815b1'), ('01a0aeae-d457-7a94-9ef8-4aa6ee10eacc'),
    ('01a0af41-a9fc-7799-a216-11285f41b44d'), ('01a0a2c4-ed56-7476-9c27-c58e4584c8e5'),
    ('01a090f7-b156-7989-ae52-92ac3f602f0e'), ('01a0aa15-8bd8-78cf-988b-b9f30e43c773'),
    ('01a080aa-ebcc-765c-861d-f47f8c0cd0e5'), ('01a08af8-b14d-7f13-9f36-a75fdbdd88d7'),
    ('01a0953d-576e-7135-b4a6-38840f5f49ed'), ('01a0a4b3-868f-7216-9b0b-98d23a291995'),
    ('01a0af01-445a-711b-a502-a3c6a8db86ee'), ('01a0af01-54c5-7b4c-84f2-26a8086ad84a'),
    ('01a08061-f9f7-74ef-92ff-2eca2fbfc7c4'), ('01a0805d-7bd0-7cfc-b39c-1b73eb00e70d'),
    ('01a0805d-e790-7f0b-b1e7-151a1cbcd641'), ('01a0805f-5d72-75c7-8ece-54f0950bc80c'),
    ('01a07bd6-1140-7265-be09-9e913138dbe4'), ('01a08621-7323-7397-901e-1f194ad1e32a'),
    ('01a08059-fa11-7978-b055-add66d96093e'), ('01a08059-fa11-7d7b-812c-01b86ca048b7'),
    ('01a0aa31-0c01-7344-b6d6-45710d409a58'), ('01a0aa31-12f2-7e54-b570-3e3d89cdd4b2'),
    ('01a0805b-53f7-7caf-ab08-40da3376c89e'), ('01a0805f-3543-7b78-8a87-10a79bdecb93'),
    ('01a0805f-849f-78aa-85fb-f8bd1acbcde1'), ('01a0a58f-7572-70fc-89de-1763d9310b19'),
    ('01a0a9e7-b215-7448-8a6a-79431ad9f928'), ('01a08aef-64f5-7039-a203-c726d39a5265'),
    ('01a09012-9d47-7f43-8983-967b19a460ab'), ('01a0aef8-179a-7eec-b57f-79f7e8c527eb'),
    ('01a0953f-2c92-7aa7-b3b1-b2fffb391afe'), ('01a09040-6d25-7b0f-af9d-af5e6b724325'),
    ('01a08059-fa11-743c-aed2-dcc3f825cb7d'), ('01a0805b-53f7-711b-af3b-3b488e351c94'),
    ('01a0805f-3543-7ecf-afbc-b06c06f3669a'), ('01a0805f-849f-71aa-82e5-0c06df29f9cc'),
    ('01a0a7d5-476f-7951-839d-334cd463a241'), ('01a08f24-7a75-7b7d-b122-a0f4d8e409de'),
    ('01a08059-fa11-7432-b279-522377864023'), ('01a0a9f0-df15-718e-b881-7590201deed7'),
    ('01a080e1-eca3-767d-bb7d-c17bbd1019ff'), ('01a0a4ea-82a3-7f6b-99ba-20b84825e35f'),
    ('01a0a4ea-8abf-7cfe-9596-fae1e559294e'), ('01a0af38-64a0-75ab-a52a-b768e6e283c0'),
    ('01a0a58f-7187-7844-a94e-53b49c18b8d4'), ('01a0ae95-2c1e-7eea-b34b-f575e58d4cc8'),
    ('01a0a9fa-0f4f-7c4f-85bb-b337aeb07077'), ('01a0a546-2ed9-78b2-80d9-392e98e2887c'),
    ('01a0a2fb-e848-76c0-b0a0-5c296805e656'), ('01a0a826-59ca-7291-9eab-bc00dd4ec235'),
    ('01a0af01-5cb8-7759-bca7-b51ff1c029f0'), ('01a0af6f-7ab0-748c-9a35-543f6159f1e2'),
    ('01a09009-6974-767e-b794-fb2bc3407e14'), ('01a0a9cc-1295-7da6-b434-fbeb5d1bcd53'),
    ('01a0ac6c-5971-74cc-83d7-56d624b1af96'), ('01a0a5aa-fadb-7342-b554-a112239c7a8e'),
    ('01a099b2-b6b5-7d45-b413-9010e91ae5be');

CREATE TEMP TABLE tmp_demo_booking_ids ON COMMIT DROP AS
    SELECT id FROM demo_bookings WHERE class_session_id IN (SELECT id FROM tmp_session_ids);

CREATE TEMP TABLE tmp_invoice_ids ON COMMIT DROP AS
    SELECT id FROM invoices WHERE child_id IN (SELECT id FROM tmp_child_ids)
    UNION
    SELECT invoice_id FROM demo_bookings
        WHERE id IN (SELECT id FROM tmp_demo_booking_ids) AND invoice_id IS NOT NULL;

CREATE TEMP TABLE tmp_payment_transaction_ids ON COMMIT DROP AS
    SELECT id FROM payment_transactions WHERE invoice_id IN (SELECT id FROM tmp_invoice_ids);

-- 1. Deepest leaves: refunds on the affected payment transactions.
DELETE FROM refunds WHERE payment_transaction_id IN (SELECT id FROM tmp_payment_transaction_ids);

-- 2. The payment transactions themselves.
DELETE FROM payment_transactions WHERE id IN (SELECT id FROM tmp_payment_transaction_ids);

-- 3. Fee suspensions tied directly to a target child or to a target invoice.
DELETE FROM fee_suspensions
WHERE child_id IN (SELECT id FROM tmp_child_ids) OR invoice_id IN (SELECT id FROM tmp_invoice_ids);

-- 4. Demo booking children (feedback/participants) for target sessions.
DELETE FROM demo_feedbacks WHERE demo_booking_id IN (SELECT id FROM tmp_demo_booking_ids);
DELETE FROM demo_participants WHERE demo_booking_id IN (SELECT id FROM tmp_demo_booking_ids);

-- 5. Demo bookings for target sessions (now safe: no dependents left).
DELETE FROM demo_bookings WHERE id IN (SELECT id FROM tmp_demo_booking_ids);

-- 6. Invoices (target children's own invoices, plus any demo-booking invoice
--    pulled in above). Must run before subscriptions (invoices.subscription_id
--    points at subscriptions).
DELETE FROM invoices WHERE id IN (SELECT id FROM tmp_invoice_ids);

-- 7. Progress reports for target children.
DELETE FROM progress_reports WHERE child_id IN (SELECT id FROM tmp_child_ids);

-- 8. Subscriptions for target children (safe now that invoices are gone).
DELETE FROM subscriptions WHERE child_id IN (SELECT id FROM tmp_child_ids);

-- 9. Batch enrollments for target children.
DELETE FROM batch_enrollments WHERE child_id IN (SELECT id FROM tmp_child_ids);

-- 10. Enrollment forms for target children.
DELETE FROM enrollment_forms WHERE child_id IN (SELECT id FROM tmp_child_ids);

-- 11. Payout items tied to target sessions (the parent Payout row itself is
--     untouched -- it may still summarize other, unrelated sessions).
DELETE FROM payout_items WHERE class_session_id IN (SELECT id FROM tmp_session_ids);

-- 12. Leave request sessions tied to target sessions.
DELETE FROM leave_request_sessions WHERE class_session_id IN (SELECT id FROM tmp_session_ids);

-- 13. Dual-FK leaf tables: match on EITHER a target child_id OR a target
--     class_session_id, since either column alone would leave a dangling
--     reference once children/class_sessions are deleted below.
DELETE FROM class_session_event_logs
WHERE child_id IN (SELECT id FROM tmp_child_ids) OR class_session_id IN (SELECT id FROM tmp_session_ids);
DELETE FROM engagement_events
WHERE child_id IN (SELECT id FROM tmp_child_ids) OR class_session_id IN (SELECT id FROM tmp_session_ids);
DELETE FROM session_attendances
WHERE child_id IN (SELECT id FROM tmp_child_ids) OR class_session_id IN (SELECT id FROM tmp_session_ids);
DELETE FROM student_awards
WHERE child_id IN (SELECT id FROM tmp_child_ids) OR class_session_id IN (SELECT id FROM tmp_session_ids);

-- 14. Session-only leaf tables.
DELETE FROM session_presentations WHERE class_session_id IN (SELECT id FROM tmp_session_ids);
DELETE FROM session_recordings WHERE class_session_id IN (SELECT id FROM tmp_session_ids);

-- 15. class_sessions has TWO SELF-referencing FKs (a rescheduled/carried-
--     forward session points back at the original it came from). Clear any
--     pointer -- from a target session OR any other session in the table --
--     that targets a session we're about to delete, otherwise the DELETE
--     below fails with a FK violation.
UPDATE class_sessions SET rescheduled_from_session_id = NULL
WHERE rescheduled_from_session_id IN (SELECT id FROM tmp_session_ids);
UPDATE class_sessions SET carried_forward_from_session_id = NULL
WHERE carried_forward_from_session_id IN (SELECT id FROM tmp_session_ids);

-- 16. Finally, the roots themselves.
DELETE FROM children WHERE id IN (SELECT id FROM tmp_child_ids)
RETURNING id, first_name || ' ' || last_name AS child_name;
-- Expect 95 rows RETURNING above.

DELETE FROM class_sessions WHERE id IN (SELECT id FROM tmp_session_ids)
RETURNING id, batch_id, scheduled_start_at_utc;
-- Expect 85 rows RETURNING above.

-- If both RETURNING sets show exactly 95 and 85 rows and match STEP 1's
-- scope report:
--   COMMIT;
-- If anything looks wrong -- fewer/more rows than expected, an error partway
-- through, anything unexpected:
--   ROLLBACK;
-- There is no way to recover this data after COMMIT.


-- ---------------------------------------------------------------------------
-- STEP 3 -- OPTIONAL. Only after COMMIT above. One audit_logs entry
-- summarising the cleanup, in the same shape the app's own AuditLogService
-- writes for a normal delete.
-- ---------------------------------------------------------------------------
-- INSERT INTO audit_logs (id, actor_user_id, action, entity_name, entity_id, changes_json, created_at_utc, is_deleted)
-- VALUES (
--     gen_random_uuid(),
--     NULL,
--     'Delete',
--     'Child',
--     NULL,
--     '{"note":"one-time HARD DELETE (irreversible) of 95 duplicate Child rows and all dependent rows, created by the pre-fix Bulk Import Students bug","reviewedBy":"client","date":"2026-09-17"}',
--     now(),
--     false
-- );
-- INSERT INTO audit_logs (id, actor_user_id, action, entity_name, entity_id, changes_json, created_at_utc, is_deleted)
-- VALUES (
--     gen_random_uuid(),
--     NULL,
--     'Delete',
--     'ClassSession',
--     NULL,
--     '{"note":"one-time HARD DELETE (irreversible) of 85 duplicate ClassSession rows and all dependent rows, from a GenerateSchedule double-submit race","reviewedBy":"client","date":"2026-09-17"}',
--     now(),
--     false
-- );
