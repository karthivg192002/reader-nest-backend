# Test Strategy & Environments (Sprint 0)

## Test pyramid
| Layer | Tooling | Scope | Owner |
|---|---|---|---|
| Unit/service smoke | xUnit + SQLite in-memory (`iucs.readernest.tests`) | Services against the real DbContext + audit interceptor | Devs, per PR (runs in CI) |
| API integration | PowerShell/REST scripts now → WebApplicationFactory later | Auth, RBAC gates, CRUD contracts, ProblemDetails | Squad F, per sprint |
| E2E UI | Playwright (already a frontend devDependency) | Login → portal flows per role | Squad F, Sprint 2+ |
| Load | k6 against self-hosted Jitsi + API | Live-classroom concurrency | Sprint 3 scripting, Sprint 5 execution |
| Security | See SECURITY_TEST_PLAN.md | RBAC, payment data, session access | Sprint 2 kick-off |

## Environments
| Env | Purpose | Data |
|---|---|---|
| Local | Dev; docker-compose Postgres (host port 5433), seeded admin | Synthetic |
| Staging (client cloud, Sprint 2) | Regression + UAT; migrations applied by CI | Anonymised synthetic |
| Production | Go-live | Real; migrations gated by manual approval |

## Conventions
- Every regression wave (Sprints 2–4) adds tests to `iucs.readernest.tests`
  instead of one-off scripts; the CI test step is a required check.
- Bug fixes land with a reproducing test.
