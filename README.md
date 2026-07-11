# MS4 — Delivery Calendar

Backend microservice for delivery scheduling: creates patient delivery calendars, tracks per-day
status through address assignment, catalog resolution, production, and delivery result. Pure
backend — no frontend, no auth in this stage. Built with .NET Aspire + Clean Architecture, Postgres
via EF Core.

See [`CLAUDE.md`](./CLAUDE.md) for the full domain spec and implementation roadmap.
See [`FLOWS.md`](./FLOWS.md) for every use case, its expected behavior, error scenarios, and edge
cases (48h rule, idempotency, state transitions).

## Stack

- .NET 10 / ASP.NET Core Minimal APIs
- .NET Aspire (orchestration: Postgres container, service discovery, dashboard)
- EF Core 10 + Npgsql (Postgres)
- MediatR (CQRS: Commands/Queries + Handlers)
- FluentValidation (input validation, wired into the MediatR pipeline)
- NUnit + Shouldly (tests)

## Project layout

```
src/
  Domain/          pure C# — aggregates, entities, value objects, domain services. No EF, no ASP.NET.
  Application/      MediatR commands/queries/handlers, ports (repository/clock/catalog interfaces), DTOs
  Infrastructure/   EF Core DbContext, entity configurations, repository implementations, mocked catalog adapter
  Api/              Minimal API endpoints, exception -> ProblemDetails mapping
  AppHost/           Aspire orchestration entry point (Postgres + Api)
  ServiceDefaults/   Aspire service defaults (health checks, telemetry, resilience)
  Shared/            cross-cutting constants (service/database names)
tests/
  Domain.UnitTests/            domain rules (48h boundary, state transitions, calendar generation)
  Application.UnitTests/       handlers against in-memory fakes
  Infrastructure.IntegrationTests/  repositories against real Postgres (via Aspire testing)
  Application.FunctionalTests/  end-to-end acceptance tests (MediatR pipeline) against real Postgres
  TestAppHost/                  minimal Aspire host used by the two test projects above
```

## Run

```bash
dotnet run --project src/AppHost
```

Opens the Aspire dashboard, starts a Postgres container, applies EF Core migrations, and starts the
API. Find the API's Scalar reference (OpenAPI UI) from the dashboard's URL list for the `webapi`
resource, or use `src/Api/Api.http` directly (VS Code REST Client / Visual Studio / Rider all run
`.http` files).

## Test

```bash
dotnet test                                    # everything
dotnet test tests/Domain.UnitTests             # fast, no external deps
dotnet test tests/Application.UnitTests        # fast, no external deps
dotnet test tests/Infrastructure.IntegrationTests  # spins up real Postgres via Aspire — slower, needs Docker
dotnet test tests/Application.FunctionalTests      # acceptance tests, real Postgres via Aspire, needs Docker
```

## Database migrations

EF Core tooling needs to match the `Microsoft.EntityFrameworkCore.Design` package version in
`Directory.Packages.props` — if `dotnet ef` fails with an MSBuild target error, run
`dotnet tool update -g dotnet-ef`.

```bash
# add a migration
dotnet ef migrations add <Name> --project src/Infrastructure --startup-project src/Api --output-dir Data/Migrations

# migrations apply automatically on `dotnet run --project src/AppHost` in Development
# (ApplicationDbContextInitialiser calls Database.MigrateAsync() on startup)
```

## API

All 7 contract endpoints from `CLAUDE.md` §0, plus one internal/ops endpoint, under `/api/v1`:

| Endpoint | Method | Caller |
|---|---|---|
| `/schedule` | POST | MS3 — create calendar (idempotent by `contractId`) |
| `/calendars/active-tomorrow` | GET | MS3 — consolidated production order for a date |
| `/calendars/{id}/dates` | PUT | Frontend — reschedule a day (rejected <48h out) |
| `/calendars/{id}/addresses` | POST | Frontend — set delivery address for a day |
| `/calendars/{id}/cancel-day` | POST | Frontend — cancel a day (rejected <48h out) |
| `/calendars/{id}` | GET | Frontend/internal — full calendar with all days |
| `/calendars/days/{dayId}/delivery-result` | PATCH | MS5 — report delivered/failed (idempotent) |
| `/calendars/resolve-daily-catalog` | POST | internal/ops — manually trigger the daily catalog resolution job (must run before MS3 calls `active-tomorrow` for that date) |

Error mapping: `400` validation, `404` not found, `409` 48h lock window violation, `422` invalid
state transition. See `FLOWS.md` for exact scenarios per endpoint.

## Scalar walkthrough — testing the flow end-to-end

Open Scalar at the URL shown in the Aspire dashboard for the `webapi` resource (path: `/scalar/v1`).
All endpoints are listed under the **Calendars** tag and numbered `[Step 1]`–`[Step 8]` to guide test order.

Today's date in the examples below: `2026-07-11` — adjust dates to match your run.

---

### Step 1 — Create a delivery calendar

`POST /api/v1/schedule`

```json
{
  "contractId": "11111111-0000-0000-0000-000000000001",
  "patientId":  "22222222-0000-0000-0000-000000000001",
  "planId":     "33333333-0000-0000-0000-000000000001",
  "duration":   15,
  "startDate":  "2026-07-12"
}
```

Returns `calendarId` (integer) + 15 days, all `Pending`. **Copy `calendarId` and one `dayId`** — every later step needs them.

| Scenario | What to do | Expected |
|----------|-----------|----------|
| Happy path | Send the request above | 200 — `calendarId` + day list |
| Idempotent retry | Send again with the **same `contractId`** | 200 — same calendar returned, no duplicate created |
| Invalid duration | Change `"duration"` to `7` | 400 — `FluentValidation` error |

---

### Step 2 — Inspect the calendar

`GET /api/v1/calendars/{id}` — use `calendarId` from Step 1.

Returns the full calendar with all days and their current `Pending` status. Find the day whose `date` is `2026-07-12` (tomorrow) — **note its `dayId`** for Steps 3–4.

| Scenario | What to do | Expected |
|----------|-----------|----------|
| Happy path | Use `calendarId` from Step 1 | 200 — all days `Pending` |
| Not found | Use a non-existent ID (e.g. `9999`) | 404 |

---

### Step 3 — Set a delivery address for a day

`POST /api/v1/calendars/{calendarId}/addresses`

Use the `dayId` for tomorrow's day from Step 2 so that Step 4 can resolve it.

```json
{
  "dayId":     <dayId from Step 2 — the one dated 2026-07-12>,
  "street":    "Av. Javier Prado 123",
  "city":      "Lima",
  "latitude":  -12.0833,
  "longitude": -77.0167
}
```

Day status moves `Pending → Scheduled`. Returns `dayId` + address.

| Scenario | What to do | Expected |
|----------|-----------|----------|
| Happy path | Payload above | 200 — day is now `Scheduled` |
| Update address | Call again with a different street | 200 — address overwritten, still `Scheduled` |
| Invalid coordinates | Set `latitude` to `200` | 400 — validation error |
| Wrong state | Use a `dayId` that is already `Delivered` or `Cancelled` (see seed Patient 3) | 422 |

---

### Step 4 — Run daily catalog resolution (ops trigger)

`POST /api/v1/calendars/resolve-daily-catalog` — no body required.

Finds all `Scheduled` days dated tomorrow, calls `ICatalogPort` (mocked — deterministic fixture), sets `RecipeRef`/`Portions`, moves each day to `ReadyForProduction`, and rebuilds the `ProductionOrder` for that date.

Add `?date=2026-07-12` as a query param to target a specific date (default is tomorrow).

Returns **204 No Content**.

| Scenario | What to do | Expected |
|----------|-----------|----------|
| Happy path | Call with no params | 204 — tomorrow's Scheduled days → ReadyForProduction |
| Re-run | Call again for the same date | 204 — idempotent, existing order's lines replaced, no duplicate |
| Specific date | Add `?date=2026-07-15` | 204 — resolves that date instead of tomorrow |

---

### Step 5 — Get tomorrow's production order

`GET /api/v1/calendars/active-tomorrow`

Returns all `ReadyForProduction` days for tomorrow, mapped to `{ packId, patientId, planId, hour, address, lat, long }`. Step 4 must have run first or this returns an empty list.

Add `?date=2026-07-12` to query a specific date.

| Scenario | What to do | Expected |
|----------|-----------|----------|
| After Steps 1–4 | Call with no params | 200 — the day from Step 3 appears |
| Before Step 4 (fresh run with seed data) | Skip Steps 1–4, call immediately | 200 — seed Patient 2 has a pre-resolved day for tomorrow |
| No resolved days | Target a date that had no `Scheduled` days | 200 — empty list (not 404) |

---

### Step 6 — Reschedule a delivery day

`PUT /api/v1/calendars/{calendarId}/dates`

```json
{
  "dayId":   <dayId of a Pending or Scheduled day with date > 48h from now>,
  "newDate": "2026-08-01"
}
```

Returns `dayId` + `status` (unchanged — rescheduling doesn't change status).

| Scenario | What to do | Expected |
|----------|-----------|----------|
| Happy path | Use a day dated far in the future | 200 |
| 48h lock | Use a day whose **current** date is `2026-07-12` (less than 48h away) | 409 |
| Wrong state | Use a day in `ReadyForProduction` or `Delivered` | 422 |

---

### Step 7 — Cancel a delivery day

`POST /api/v1/calendars/{calendarId}/cancel-day`

```json
{
  "dayId": <dayId of a Pending or Scheduled day with date > 48h from now>
}
```

Day moves to `Cancelled` (terminal — cannot be undone).

| Scenario | What to do | Expected |
|----------|-----------|----------|
| Happy path | Day dated far in the future | 200 — `Cancelled` |
| 48h lock | Day dated tomorrow (`2026-07-12`) | 409 |
| Already cancelled | Call twice on the same day | 422 on second call |
| Terminal state | Use a `Delivered` or `Failed` day | 422 |

---

### Step 8 — Record delivery result (MS5 callback)

`PATCH /api/v1/calendars/days/{dayId}/delivery-result`

Day must be `ReadyForProduction` or `InProduction`. Use the `dayId` that went through Steps 3–4, or grab the pre-resolved day from seed Patient 2.

```json
{
  "result":       "DELIVERED",
  "photoUrl":     "https://example.com/evidence.jpg",
  "signatureUrl": null
}
```

Returns `dayId` + final `status` (`Delivered` or `Failed`).

| Scenario | What to do | Expected |
|----------|-----------|----------|
| Delivered | `"result": "DELIVERED"` | 200 — day is `Delivered` |
| Failed | `"result": "FAILED"` | 200 — day is `Failed` |
| Idempotent retry | Call twice with the **same** `result` | 200 both times — no error |
| Conflicting retry | Call with `DELIVERED`, then call again with `FAILED` | 200 then **422** |
| Wrong state | Use a `Pending` or `Cancelled` day | 422 |

---

## Business rules

### 48-hour lock window
A delivery day is **locked** once its scheduled date is less than 48 hours away. Any attempt to reschedule or cancel a locked day is rejected with a `409 Conflict`. The boundary is inclusive: exactly 48 hours remaining is not locked.

### Calendar creation
- A calendar always spans exactly **15 or 30 days** starting from `startDate`, one delivery day per day.
- The `POST /schedule` endpoint is **idempotent by `contractId`**: calling it a second time with the same contract returns the existing calendar without creating a duplicate.

### Delivery day statuses
A day progresses through a strict one-way pipeline:

```
Pending → Scheduled → ReadyForProduction → Delivered
                                         → Failed
       ↘ (any stage before terminal) → Cancelled
```

| Status | Meaning |
|---|---|
| `Pending` | Day created, no address yet |
| `Scheduled` | Address set — day is queued for catalog resolution |
| `ReadyForProduction` | Recipe and portions resolved via MS2 catalog — day is in the production order |
| `Delivered` | MS5 confirmed delivery with evidence |
| `Failed` | MS5 reported a failed delivery attempt |
| `Cancelled` | Patient or operator cancelled the delivery — **terminal, cannot be undone** |

### Allowed operations per status

| Operation | Allowed statuses | Locked <48h? |
|---|---|---|
| Set address | `Pending`, `Scheduled` | No lock (no time restriction) |
| Reschedule date | `Pending`, `Scheduled` | Yes — rejected if <48h out |
| Cancel | `Pending`, `Scheduled`, `ReadyForProduction` | Yes — rejected if <48h out |
| Record delivery result | `ReadyForProduction`, `InProduction` | No lock |

### Delivery results
- Only **MS5** can move a day to `Delivered` or `Failed` — this cannot be done manually through any other endpoint.
- Reporting the same result twice (e.g. `DELIVERED` → `DELIVERED`) is a **no-op** — the call succeeds and returns the current status unchanged.
- Reporting a conflicting result (e.g. `DELIVERED` then `FAILED`) is rejected with `422`.

### Catalog resolution
The daily catalog job (`POST /calendars/resolve-daily-catalog`) must run **before** MS3 calls `GET /calendars/active-tomorrow` for a given date. Only days in `Scheduled` status receive recipe/portion data from MS2 and move to `ReadyForProduction`. Days without an address (`Pending`) are skipped silently. The job is idempotent — running it twice for the same date replaces the production order lines without creating duplicates.

---

## Service dependencies and inter-microservice communication

### Who calls MS4

| Caller | Endpoint | When |
|---|---|---|
| **MS3** (contracting) | `POST /api/v1/schedule` | After patient payment is confirmed — creates the delivery calendar |
| **MS3** (contracting) | `GET /api/v1/calendars/active-tomorrow` | Daily — retrieves all addresses and plan IDs for next-day production planning |
| **Frontend** (patient app) | `PUT /api/v1/calendars/{id}/dates` | Patient reschedules a delivery day |
| **Frontend** (patient app) | `POST /api/v1/calendars/{id}/addresses` | Patient sets or updates a delivery address |
| **Frontend** (patient app) | `POST /api/v1/calendars/{id}/cancel-day` | Patient cancels a delivery day |
| **Frontend** (patient app) | `GET /api/v1/calendars/{id}` | Patient views their full calendar |
| **MS5** (delivery) | `PATCH /api/v1/calendars/days/{dayId}/delivery-result` | Courier records delivery outcome with evidence |

### What MS4 calls outbound

| Dependency | Endpoint | Purpose | Current state |
|---|---|---|---|
| **MS2** (catalog) | `GET /catalog/plans/{id}/day/{date}` | Fetch recipe reference and portion count for a plan on a given date | **Mocked** — `MockCatalogPort` returns a deterministic fixture; no real HTTP call yet |

### Known integration gaps (pending resolution upstream)

1. **MS5 does not yet call MS4.** The master architecture table has no row showing MS5 calling `PATCH /delivery-result`. Until MS5 is wired to call this endpoint, every delivery day stays stuck in `ReadyForProduction` permanently and delivery outcomes are never recorded. This is a gap in MS5's implementation, not in MS4.

2. **MS4 ↔ MS2 contract needs reconciliation before going live.** Three conflicting shapes exist:
   - This service expects `GET /catalog/plans/{id}/day/{date}` (day-specific, returns recipe + portions).
   - The master table describes a plain `GET /catalog/plans/{id}` (existence check only, no recipe data).
   - MS3 uses a separate batch endpoint `POST /catalog/plans/structure` for recipe resolution.
   The correct shape must be agreed with the MS2 team before replacing `MockCatalogPort` with a real HTTP adapter, or the daily catalog job will call a path that MS2 doesn't expose.

3. **`GET /calendars/{id}` is absent from the master table.** All other frontend-read endpoints appear in it. Low risk since it has no cross-service dependency, but the master table should be updated for completeness.

See `CLAUDE.md` §5 "Cross-service integration gaps" for the full audit details.

---

## Notes for local dev

- `ICatalogPort` (outbound call to MS2) is mocked — `MockCatalogPort` returns deterministic fixture
  data, no real network call.
- Seed data loads automatically on first run against an empty database (`dotnet run --project
  src/AppHost`, Development environment only): 3 patients with fixed GUIDs
  (`aaaaaaaa-0000-0000-0000-000000000001/2/3`) covering `Pending`, `Scheduled`,
  `ReadyForProduction`, `Delivered`, `Failed`, and `Cancelled` days. Patient 2's calendar has
  tomorrow's day pre-consolidated into a `ProductionOrder`, so `GET
  /calendars/active-tomorrow` returns data without running the catalog job first. Seeding is
  skipped if the database already has any calendar.
- No auth in this stage (confirmed decision, see `CLAUDE.md` §3.4).
