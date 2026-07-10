# Reader Nest — Database Schema Design (Sprint 0)

EF Core 9 code-first model over PostgreSQL. All entities live in
`iucs.readernest.domain/Entities/<area>` and derive from one of two abstractions in
`Entities/Common`:

| Abstraction | Adds | Used for |
|---|---|---|
| `BaseEntity` (implements `IBaseEntity`) | `Id` (Guid PK), `CreatedAtUtc`, `UpdatedAtUtc`, `IsDeleted`, `DeletedAtUtc` | Every table. System-generated / high-volume tables use it directly. |
| `AuditEntity` (extends `BaseEntity`, implements `IAuditableEntity`) | `CreatedBy`, `UpdatedBy` (acting user id, null = system) | Admin-managed or financially sensitive tables that need a "who did it" trail. |

`AuditableEntityInterceptor` stamps all of these automatically on `SaveChanges` and
converts hard deletes to soft deletes; the `DbContext` applies a global `IsDeleted`
query filter, stores enums as strings, defaults decimals to `numeric(12,2)` and forces
`DeleteBehavior.Restrict` on every FK so academic/financial history can never cascade away.

## Entities by area

### Users & Access Control
| Entity | Base | Notes |
|---|---|---|
| `User` | AuditEntity | Login identity, role enum (Admin/SubAdmin/AdmissionTeam/Teacher/Parent), status, timezone. Unique email. |
| `ParentProfile` | BaseEntity | 1:1 with User; multi-child dashboard root; tracks mandatory enrollment-form completion. |
| `TeacherProfile` | BaseEntity | 1:1 with User; specialization, optional department. |
| `Child` | AuditEntity | Student under a parent; no login of its own. |
| `SubAdminPermission` | AuditEntity | Module-wise view/create/edit/delete/approve grants; unique (user, module). Coordinator/Management personas are presets of these rows. |

### Academics
| Entity | Base | Notes |
|---|---|---|
| `CourseCategory` | AuditEntity | Category + department. |
| `Course` | AuditEntity | Type (individual/group), duration (30/45/60), price, total sessions, department (routes payments). |
| `Batch` | AuditEntity | Teacher, capacity, Active → Dormant lifecycle; `CompletedAtUtc` anchors the 15-day recording window. |
| `BatchEnrollment` | AuditEntity | Child ↔ batch, unique pair; drives classes completed/remaining. |
| `EnrollmentForm` | AuditEntity | Mandatory first-login form; answers as JSON (field list pending from client); admin approve workflow. |
| `Holiday` | AuditEntity | Academic calendar holidays. |
| `LeaveRequest` | AuditEntity | Teacher leave; 6-hour rule enforced in the application layer; admin approval. |

### Sessions
| Entity | Base | Notes |
|---|---|---|
| `ClassSession` | AuditEntity | Regular or demo; status enum drives calendar colours; self-links for reschedule and no-show carry-forward; Jitsi room id. |
| `SessionAttendance` | BaseEntity | Join-based capture per participant (teacher or student row). |
| `SessionRecording` | BaseEntity | Cloud recording; `ExpiresAtUtc` feeds the auto-expiry job. |

### Admission
| Entity | Base | Notes |
|---|---|---|
| `DemoBooking` | AuditEntity | One-time demo; lead contact data; conversion funnel status; payment link. |
| `DemoParticipant` | BaseEntity | Multi-parent invitees. |
| `DemoFeedback` | BaseEntity | Mandatory teacher feedback (level, strengths, recommended course, batch type); unique per booking. |

### Billing
| Entity | Base | Notes |
|---|---|---|
| `PaymentAccount` | AuditEntity | One per department (Phonics/Maths); external gateway ref only, no secrets. |
| `PackagePlan` | AuditEntity | Subscription / session-based / one-time pricing. |
| `Subscription` | AuditEntity | Child on a plan; drives auto-billing. |
| `Invoice` | AuditEntity | Auto-generated, department-routed; overdue check feeds fee suspension. |
| `PaymentTransaction` | BaseEntity | Gateway attempts + receipt fields. |
| `Refund` | AuditEntity | Refund workflow per transaction. |
| `FeeSuspension` | AuditEntity | Blocks session/content access; Pay Now popup; auto/admin restoration. |

### Payouts
| Entity | Base | Notes |
|---|---|---|
| `PayoutRate` | AuditEntity | Per teacher per duration, versioned by `EffectiveFrom`. |
| `Payout` | AuditEntity | One per teacher per month, unique (teacher, year, month); fresh each month, history intact; statement emailed. |
| `PayoutItem` | BaseEntity | Signed line items: session earnings, student no-show waiting amounts, teacher no-show deductions, penalties. |

### Resources, Communication, Auditing
| Entity | Base | Notes |
|---|---|---|
| `Resource` | AuditEntity | Books/worksheets; only worksheets downloadable. |
| `ResourceAccess` | BaseEntity | Admin-gated visibility per parent dashboard. |
| `Notification` | BaseEntity | Email/SMS/in-app queue: reminders, alerts, payout statements, bulk mail. |
| `AuditLog` | BaseEntity | Append-only action trail (logins, approvals, exports, payments). |

## Migration workflow

```bash
# from reader-nest-backend/
dotnet ef migrations add <Name> --project iucs.readernest.domain --startup-project iucs.readernest.api
dotnet ef database update --project iucs.readernest.domain --startup-project iucs.readernest.api
```
