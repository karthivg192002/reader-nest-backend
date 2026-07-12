# Ownership Transfer / Handover Package (Sprint 5)

Everything the client receives, and the transfer steps for each item.

## 1. Source code
| Item | Location | Transfer step |
|---|---|---|
| Backend (.NET 9) | github.com/karthivg192002/reader-nest-backend | Transfer repo to the client's GitHub org (Settings → Transfer) |
| Frontend (React/Vite) | github.com/Deepan45/the-reader-nest-frontend | Same |

Both repos carry CI (build + tests / typecheck + build) that runs unchanged
after transfer.

## 2. Documentation set (in `docs/`)
- `API_DOCUMENTATION.md` + live OpenAPI (`/scalar`) — API reference
- `ADMIN_GUIDE.md` — user manual for academy staff
- `DATABASE_SCHEMA.md`, `WORKFLOWS.md`, `JITSI_ARCHITECTURE.md`,
  `WHITEBOARD_EVALUATION.md`, `ANALYTICS_KPIS.md` — design decisions
- `PROVISIONING.md` + `docker-compose.prod.yml` — one-command deployment
- `TEST_STRATEGY.md`, `SECURITY_TEST_PLAN.md`, `UAT_PLAN.md`,
  `LONG_DURATION_SESSIONS.md`, `load-tests/` — QA assets
- `WHITE_LABEL_BRANDING.md`, `ENROLLMENT_FORM_FIELDS.md` — pending client inputs

## 3. Infrastructure & accounts (client-owned)
| Item | State | Transfer step |
|---|---|---|
| Cloud environment | Runbook + compose stack ready | Client provisions per `PROVISIONING.md` |
| Jitsi deployment | `meet.techmisai.com` | Move DNS + VM to client account; enable Jibri for recording |
| Database | Migrations apply on deploy | Managed Postgres 17 in client account |
| Payment gateways | Abstraction + simulated gateway in code | Swap `SimulatedPaymentGateway` for provider SDK with client's Phonics/Maths credentials |
| Email delivery | `LoggingEmailSender` in dev | Configure SMTP/provider `IEmailSender` with client sender identity |

## 4. Secrets checklist (regenerate at transfer — never reuse dev values)
JWT signing key · DB credentials · seed admin password · gateway keys ·
email credentials. All are environment/secret-store driven; none are committed.

## 5. Acceptance status
- Regression: backend build 0 errors, 13/13 smoke tests, frontend typecheck+build green (2026-07-11).
- Security: dependency audits recorded in `API_DOCUMENTATION.md`; RBAC/abuse-case
  matrix in `SECURITY_TEST_PLAN.md`.
- Load: k6 + torture scripts ready; execution requires the client environment.
- UAT: run `UAT_PLAN.md` scenarios during Stabilization Week before go-live.
