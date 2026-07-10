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
  Web/              Minimal API endpoints, exception -> ProblemDetails mapping
  AppHost/           Aspire orchestration entry point (Postgres + Web)
  ServiceDefaults/   Aspire service defaults (health checks, telemetry, resilience)
  Shared/            cross-cutting constants (service/database names)
tests/
  Domain.UnitTests/            domain rules (48h boundary, state transitions, calendar generation)
  Application.UnitTests/       handlers against in-memory fakes
  Infrastructure.IntegrationTests/  repositories against real Postgres (via Aspire testing)
  Application.FunctionalTests/  end-to-end HTTP tests against real Postgres (scaffolded, not yet populated)
  TestAppHost/                  minimal Aspire host used by the two test projects above
```

## Run

```bash
dotnet run --project src/AppHost
```

Opens the Aspire dashboard, starts a Postgres container, applies EF Core migrations, and starts the
Web API. Find the API's Scalar reference (OpenAPI UI) from the dashboard's URL list for the `webapi`
resource, or use `src/Web/Web.http` directly (VS Code REST Client / Visual Studio / Rider all run
`.http` files).

## Test

```bash
dotnet test                                    # everything
dotnet test tests/Domain.UnitTests             # fast, no external deps
dotnet test tests/Application.UnitTests        # fast, no external deps
dotnet test tests/Infrastructure.IntegrationTests  # spins up real Postgres via Aspire — slower, needs Docker
```

## Database migrations

EF Core tooling needs to match the `Microsoft.EntityFrameworkCore.Design` package version in
`Directory.Packages.props` — if `dotnet ef` fails with an MSBuild target error, run
`dotnet tool update -g dotnet-ef`.

```bash
# add a migration
dotnet ef migrations add <Name> --project src/Infrastructure --startup-project src/Web --output-dir Data/Migrations

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

## Notes for local dev

- `ICatalogPort` (outbound call to MS2) is mocked — `MockCatalogPort` returns deterministic fixture
  data, no real network call.
- No seed data yet (Etapa 6) — use `src/Web/Web.http` to create a calendar, set an address, run the
  catalog job, then query `active-tomorrow`.
- No auth in this stage (confirmed decision, see `CLAUDE.md` §3.4).
