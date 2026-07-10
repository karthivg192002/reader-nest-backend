# Reader Nest Backend

.NET 9 Web API for The Reader Nest LMS & Virtual Classroom platform.

## Solution structure

| Project | Responsibility |
|---|---|
| `iucs.readernest.api` | Web API host: controllers, DI wiring, CORS, OpenAPI, health endpoint, `CurrentUserService`. |
| `iucs.readernest.application` | Application layer: DTOs, services, AutoMapper profiles. |
| `iucs.readernest.domain` | Domain layer: entities (`Entities/<area>`), enums, `ReaderNestDbContext` + audit interceptor (`Data/`), generic repository & unit of work (`Repository/`). |

Entity design and the `BaseEntity` / `AuditEntity` split are documented in
[docs/DATABASE_SCHEMA.md](docs/DATABASE_SCHEMA.md).

## Getting started

```bash
# 1. Start PostgreSQL (host port 5433 to avoid clashing with a native install)
docker compose up -d

# 2. Run the API — in Development it applies migrations and seeds
#    the admin account + Phonics/Maths payment accounts automatically
dotnet run --project iucs.readernest.api --launch-profile http
```

Health check: `GET http://localhost:5288/health`. OpenAPI (dev only): `GET /openapi/v1.json`.

Development admin login (seeded, dev only — override via the `Seed` section):
`admin@readernest.com` / `Admin@12345`.

The frontend (`the-reader-nest-frontend`) targets this API through
`VITE_API_BASE_URL` in its `.env.development`; remove that variable to run the
UI in demo mode with mock data.

Connection string `ConnectionStrings:ReaderNestDb` lives in
`appsettings.Development.json` for local dev — use user secrets or environment
variables for real credentials, never commit them.
