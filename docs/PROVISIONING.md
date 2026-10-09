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
- `Storage:S3:Endpoint` / `Storage:S3:AccessKey` / `Storage:S3:SecretKey` /
  `Storage:S3:BucketName` (env vars `Storage__S3__*`) — S3-compatible object
  storage (Hetzner Object Storage in this project) that `S3FileStorage`
  needs to construct its client. **Every one of these is required while
  `Storage:Provider` is `S3` (the default)**. If any is left unset, the container
  still starts, but every request touching `ResourcesController` or
  `ParentPortalController`'s resource download — including plain GETs, not
  just uploads — 500s at DI-construction time with an
  `Amazon.Runtime.AmazonClientException`. `appsettings.json` seeds these
  keys as `""` (not absent) purely so they're discoverable, which is why a
  missing env var surfaces as a runtime exception on first request rather
  than a startup failure — there's nothing to catch it before then.
- `Storage:Provider` (env `Storage__Provider`; `STORAGE_PROVIDER` in the
  compose `.env`) — `S3` (default) or `Local`: only the **starting** value.
  Admins switch it live on Admin → Settings → File storage; that choice is
  saved in the database (`storage.provider` AppSetting) and wins over this
  setting. Without S3 credentials it is always `Local`, so a `Local`-only
  client can leave every `Storage:S3:*` value out.
  `Local` stores uploaded resources and class presentations under
  `Storage:LocalPath` (env `Storage__LocalPath`, `/app/uploads` in the
  deploy configs) via `LocalFileStorage`, with the same signed multipart
  upload and seekable playback links S3 gives (served by
  `LocalStorageController`). **`Storage:LocalPath` must be a mounted host
  folder or volume** — the Jenkinsfile mounts `/opt/readernest/uploads`,
  docker-compose the `reader_nest_uploads` volume; without a mount the files
  are lost on the next redeploy. To switch, first copy the existing files
  (keys stay the same, so the database needs no change), then change the
  setting and redeploy:
  `docker exec readernestbackend dotnet iucs.readernest.api.dll storage-migrate --to local`
  (`--to s3` copies back). Re-runnable; it skips files already copied and
  never deletes from the source.

## CI/CD hook-up

Both GitHub repos already run CI (build + smoke tests / typecheck + build).
Deployment jobs are added to the same workflows once the target account exists:
build container → push to the client's registry → deploy + `dotnet ef database update`.

## Client inputs required

1. Cloud account access (or a provisioned project/subscription with quotas).
2. Domains + DNS for portals, API and `meet.techmisai.com`.
3. Payment gateway accounts (Phonics + Maths) and email sender identity.
