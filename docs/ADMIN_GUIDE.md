# Reader Nest — Admin Guide / User Manual

A task-oriented guide for academy staff. Portal URL and credentials come from
your deployment; the seeded admin signs in and creates every other account.

## 1. First sign-in & user management (Admin)
- **Users** → create Teachers, Parents, Admission staff and Sub Admins.
  New accounts receive a temporary password by email. Suspend/reactivate from
  the same screen.
- **Permissions** → grant Sub Admins module-wise access (view/create/edit/
  delete/approve per module). Admins hold every permission implicitly.

## 2. Courses, batches & scheduling
- **Courses** → create categories (Phonics/Maths) and courses with duration
  (30/45/60 min), price and total sessions.
- **Batches** → create a batch, set capacity, assign the teacher, then
  **Generate Schedule**: pick start date, weekdays and time — every session of
  the course is placed automatically, skipping holidays; the batch moves to
  Dormant on course completion automatically.
- **Academic Calendar** → colour-coded view of all sessions; manage the
  holiday list here (generation skips those dates). Double-booking a teacher is
  rejected by the system.
- Reschedule/cancel/no-show from Sessions; a no-show carries the class forward
  one week and applies the payout rule automatically.

## 3. Admissions (Admission portal)
- **Demo Scheduling** → book one-time demos (multi-parent invites supported);
  leave the teacher blank to auto-assign the least-loaded teacher.
- Teachers submit mandatory **Demo Feedback** after each demo; review it under
  Demo Feedback and drive the conversion pipeline on **Leads**
  (Demo Completed → Follow-up → Enrolled / Not Interested).
- **Payments** → generate a Pay Now link for any open invoice and share it.

## 4. Parents & enrollment
- Parents complete the **mandatory enrollment form** at first login; review and
  approve under **Enrollments** (approval creates the child record and unlocks
  the parent dashboard). Download the submission as a file for records.
- The parent dashboard shows per-child classes done/remaining, attendance %,
  fee status, schedule with one-click join, granted resources and invoices.

## 5. Billing & finance
- **Package plans** → subscription/session/one-time plans; the hourly auto
  billing job issues recurring invoices and flags overdues.
- Overdue invoices auto-suspend the parent account (session/content access
  blocked, Pay Now popup). Access restores automatically on full payment or
  manually under **Fee Suspension**.
- Record payments (receipt auto-generated) or share gateway payment links.
  Phonics and Maths revenue route through separate payment accounts.

## 6. Teachers
- Dashboard lists today/upcoming/completed classes; **one click joins the live
  classroom** (recording starts automatically when the teacher joins).
- Mark attendance per session; complete a session with an optional summary.
- Apply for **Leave** (blocked within 6 hours of a scheduled class); admins
  approve/reject from the coordinator Availability screen.
- **Payout** shows monthly statements: per-session earnings at configured
  rates, no-show waiting amounts and deductions. Admin finalizes the month,
  which emails the statement.

## 7. Reports & communication
- **Dashboard** KPIs: students, revenue, conversion, occupancy, utilization.
- **Reports** → CSV exports: attendance, revenue, payouts, conversion.
- **Bulk Email** → all active parents or one batch.

Every admin/sub-admin action is captured in the audit log.
