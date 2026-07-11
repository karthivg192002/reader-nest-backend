# Cloud Provisioning Runbook (awaiting client-owned account)

Everything below is ready to execute the day the client grants access to their
cloud account; nothing else blocks provisioning.

## Topology (single region, pilot scale)

| Component | Runs as | Sizing (pilot) |
|---|---|---|
| PostgreSQL 17 | Managed database (or container + volume) | 2 vCPU / 4 GB |
| API (`iucs.readernest.api`) | Container, `ASPNETCORE_ENVIRONMENT=Production` | 2 vCPU / 2 GB |
| Frontend (Vite build) | Static hosting / CDN behind the client domain | – |
| Jitsi stack (web, prosody, jicofo, JVB, Jibri) | Docker compose on a dedicated VM (`meet.techmisai.com`) | 4 vCPU / 8 GB + Jibri 4 vCPU |
| Recording storage | Object storage bucket, lifecycle delete after 16 days | see LONG_DURATION_SESSIONS.md |

## Configuration handed to the API at deploy time

- `ConnectionStrings:ReaderNestDb` — managed Postgres connection string (secret store)
- `Jwt:SigningKey` — generated per environment, never the dev key
- `Cors:AllowedOrigins` — the client's portal domain(s)
- `Seed:*` — production admin bootstrap credentials (rotate after first login)
- `Payments:PayNowBaseUrl` + real `IPaymentGateway` registration once the
  Phonics/Maths gateway accounts exist
- `Database:MigrateOnStartup=false` in production — migrations applied by CI

## CI/CD hook-up

Both GitHub repos already run CI (build + smoke tests / typecheck + build).
Deployment jobs are added to the same workflows once the target account exists:
build container → push to the client's registry → deploy + `dotnet ef database update`.

## Client inputs required

1. Cloud account access (or a provisioned project/subscription with quotas).
2. Domains + DNS for portals, API and `meet.techmisai.com`.
3. Payment gateway accounts (Phonics + Maths) and email sender identity.
