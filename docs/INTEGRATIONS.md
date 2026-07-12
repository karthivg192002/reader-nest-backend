# Integrations (Sprint 4)

## Website / app embed

Embed the demo-booking entry point on the client's marketing site with a
plain iframe — the portal page is responsive and self-contained:

```html
<iframe
  src="https://portal.example.com/portal-select"
  title="Book a free demo class"
  style="width:100%;min-height:640px;border:0;border-radius:12px"
  loading="lazy"></iframe>
```

Or a one-line launcher that opens the portal in a new tab:

```html
<a href="https://portal.example.com/" target="_blank" rel="noopener"
   style="display:inline-block;padding:12px 20px;border-radius:10px;
          background:#7C5CFF;color:#fff;font-weight:600;text-decoration:none">
  Book a free demo class
</a>
```

Replace `portal.example.com` with the client's own domain (PROVISIONING.md).

## CRM integration (outbound lead webhooks)

Set `Integrations:CrmWebhookUrl` in the API configuration and every lead event
is pushed as JSON — no code change per CRM (Zoho/HubSpot/Salesforce all accept
inbound webhooks or a tiny middleware):

| Event | Fired when | Payload |
|---|---|---|
| `lead.created` | Demo booking created | parent name/email/phone, child, department, demo time |
| `lead.status-changed` | Conversion status updated | booking id, status, follow-up notes |

Envelope: `{ "eventType", "occurredAtUtc", "data": { … } }`. Failures are
logged and never block admissions. Pull-based alternative: `GET /api/demo-bookings`
and the conversion CSV export (`/api/reports/export/conversion`).

## Calendar sync

`GET /api/sessions/calendar.ics?teacherProfileId=&batchId=` returns an
iCalendar feed (−30/+120 days) that Google Calendar and Outlook subscribe to
("Add calendar → From URL"). Scope it per teacher or batch with the query
parameters; cancellations carry `STATUS:CANCELLED`.

## White-label branding (frontend env)

| Variable | Effect |
|---|---|
| `VITE_BRAND_NAME` | Browser title + wordmark text |
| `VITE_BRAND_LOGO_URL` | Replaces the logo image |
| `VITE_BRAND_PRIMARY` | Primary/ring theme colour (HSL triple, e.g. `262 83% 58%`) |

## Email/SMS reminders

Reminder jobs are built in (session reminders, no-show and delayed-session
alerts, payment reminders at 08:00 UTC, weekly KPI digest on Mondays). They
send through `IEmailSender` — point it at the client's SMTP/provider at
deployment; an SMS transport plugs into the same notification service.
