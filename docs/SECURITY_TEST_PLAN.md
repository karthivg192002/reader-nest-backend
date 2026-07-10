# Security Test Plan (Sprint 1)

## Scope & priority targets
1. **AuthN/AuthZ** — JWT validation (expiry, signature, issuer/audience), role gates,
   `[HasPermission]` module/action matrix: verify a Sub Admin with only `UserManagement:View`
   cannot create users, hit billing, or read payouts; verify Teacher/Parent tokens
   cannot reach admin endpoints (expect 403, not data).
2. **Payment data** — invoices/transactions only reachable with `BillingFinance` grants;
   no gateway secrets in the database (accounts store external refs only) or in responses.
3. **Session access** — a teacher can only list their own sessions via `/api/sessions/mine`;
   suspended parents blocked from join-session endpoints (Sprint 2 enforcement).
4. **Injection & input** — EF parameterization (no raw SQL today — keep it that way);
   DTO validation on every write endpoint; upload endpoint: size cap (100 MB),
   stored under GUID names (no path traversal from user-supplied file names).
5. **Transport & headers** — HTTPS redirection on; CORS restricted to configured origins;
   no credentials in source (dev-only seeds live in appsettings.Development.json and
   must be overridden in staging/prod via environment/user-secrets).

## Method
- Sprint 2: manual abuse-case pass per the matrix above + automated 403 tests in `iucs.readernest.tests`.
- Sprint 5: full pass including dependency audit (`dotnet list package --vulnerable`, `npm audit`)
  and a scan (OWASP ZAP baseline) against staging.

## Known accepted risks (tracked)
- Temporary passwords are emailed in plain text; forced first-login change ships in Sprint 2.
- No refresh-token rotation yet (8h access tokens); revisit with Sprint 2 hardening.
