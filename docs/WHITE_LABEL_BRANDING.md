# White-Label Branding Requirements (Sprint 0 gathering)

The platform ships fully white-labelled: no vendor branding anywhere a client's
family or staff can see. Requirements gathered below; implementation lands in
Sprint 4 (White-Label Branding + Integrations).

## Requirements

| Area | Requirement | Where it lives today |
|---|---|---|
| Logo & wordmark | Client-replaceable logo on login, portal header, emails | `Logo` component (frontend); swap assets, no code change |
| Colour palette | Primary/accent/status colours themeable per deployment | Tailwind theme tokens in `tailwind.config` / CSS variables |
| Product naming | "Reader Nest" appears only via a single name constant | Frontend copy + email templates; centralize in Sprint 4 |
| Own domain | Client-owned domain for portals and API | DNS + reverse proxy at deployment (client cloud) |
| Jitsi branding | `SHOW_JITSI_WATERMARK: false`; self-hosted domain (`meet.techmisai.com`) | `JitsiLive.tsx` interface config |
| Email identity | From-address, footer and signature per client | `IEmailSender` implementation config at deployment |
| Multi-tenant readiness | Schema keeps tenant-neutral design (no hardcoded org) | See DATABASE_SCHEMA.md |

## Client inputs still required (blocking Sprint 4 execution)

1. Final logo files (SVG + raster), brand colour hex values, product display name.
2. Domain names for the portals, API and Jitsi deployment, plus DNS access.
3. Transactional email sender identity (domain, from-address, DKIM/SPF access).

## Decisions

- Branding is configuration, not code: one deployment = one brand; no runtime
  multi-brand switching in the 3-month scope.
- The classroom (Jitsi + whiteboard overlays) inherits the same palette tokens
  so the live experience matches the portals.
