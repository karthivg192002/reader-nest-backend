# Reader Nest API Documentation

Interactive reference: **`/scalar`** (development) renders the live OpenAPI
document at `/openapi/v1.json` — always current, generated from the code.
This page is the stable endpoint catalog for integrators.

Authentication: `POST /api/auth/login` returns a JWT (8h); send it as
`Authorization: Bearer <token>`. Errors are RFC 7807 ProblemDetails.
Permissions: Admin implicitly passes everything; Sub Admins need the module
grant shown per endpoint; `/mine` endpoints are scoped to the caller's role.

| Area | Endpoints | Auth |
|---|---|---|
| Auth | `POST /api/auth/login`, `GET /api/auth/me` | anonymous / any |
| Users | `GET|POST /api/users`, `GET /api/users/teachers`, `GET|PUT /api/users/{id}`, `PUT /{id}/status`, `GET|PUT /{id}/permissions` | UserManagement |
| Courses | `GET|POST /api/courses`, `GET|PUT /{id}`, `GET|POST /categories` | CourseBatchManagement |
| Batches | `GET|POST /api/batches`, `GET|PUT /{id}`, `PUT /{id}/status`, `POST /{id}/generate-schedule` | CourseBatchManagement |
| Sessions | `GET|POST /api/sessions`, `GET /mine`, `GET /{id}`, `POST /{id}/reschedule|cancel|complete|no-show`, `GET|POST /{id}/recordings`, `POST /{id}/attendance`, `GET /{id}/attendance` | SessionCalendarManagement (teacher for own) |
| Holidays | `GET|POST /api/holidays`, `DELETE /{id}` | SessionCalendarManagement |
| Leave | `POST /api/leave-requests`, `GET /mine` (teacher), `GET /api/leave-requests`, `POST /{id}/review` | LeaveManagement |
| Demo bookings | `GET|POST /api/demo-bookings`, `GET /mine`, `PUT /{id}/conversion-status`, `POST /{id}/feedback` (teacher), `GET /feedback`, `GET /feedback/mine` | Admission |
| Enrollment forms | `POST /api/enrollment-forms` (parent), `GET /mine`, `GET /api/enrollment-forms`, `POST /{id}/review` | UserManagement (admin side) |
| Resources | `GET|POST /api/resources`, `GET /{id}/download`, `POST /{id}/grants` | ContentAccessManagement |
| Parent portal | `GET /api/parent-portal/dashboard|schedule|resources|invoices`, `GET /resources/{id}/download` | parent |
| Package plans | `GET|POST /api/package-plans`, `PUT /{id}` | BillingFinance |
| Invoices | `GET|POST /api/invoices`, `POST /{id}/payments`, `POST /{id}/payment-link`, `GET /suspensions`, `POST /suspensions/{id}/lift` | BillingFinance |
| Payouts | `GET /api/payouts`, `GET /mine` (teacher), `POST /{id}/finalize|mark-paid`, `GET|POST /api/payout-rates` | Payouts |
| Reports | `GET /api/reports/dashboard-summary`, `GET /api/reports/export/{attendance|revenue|payouts|conversion}` (CSV) | ReportsAnalytics |
| Communications | `POST /api/communications/bulk-email` | Communication |
| Health | `GET /health` | anonymous |

Background jobs (hosted services): auto billing (hourly: recurring invoices,
overdue flagging, fee suspensions) and session reminders (upcoming-session
emails to teacher and parents).

## Security audit record (Sprint 5, 2026-07-11)

- `dotnet list package --vulnerable --include-transitive`: **0 vulnerable packages**
  (SQLitePCLRaw High advisory in the test project fixed by direct upgrade).
- `npm audit`: 2 advisories remain in **dev-only tooling** (esbuild/vite dev
  server GHSA-67mh-4wv8-2f99, playwright download check) — not part of the
  shipped bundle; clearing them requires a Vite major upgrade, scheduled with
  the next toolchain refresh.
