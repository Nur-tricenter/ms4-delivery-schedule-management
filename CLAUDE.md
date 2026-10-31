# MS4 — Delivery Calendar — Implementation Plan

Stack: **.NET Aspire + Clean Architecture** · Persistence: **Postgres** (orchestrated by Aspire, EF Core) · Owner: Franklin Andre Romero Padilla

This document is the basis for a coding agent to execute the implementation in stages. Each stage has inputs, tasks, and a verifiable "definition of done" (builds + runs + tests).

All docs and code in this repository must be written in English.

---

## 0. Input contracts (fixed, not renegotiated in code)

### Endpoints exposed by MS4

| # | Endpoint | Method | Client | Input | Output |
|---|---|---|---|---|---|
| 1 | `/api/v1/schedule` | POST | MS3 | contractId, patientId, planId, duration(15\|30), startDate | calendarId, days[] (empty, status=PENDING) |
| 2 | `/api/v1/calendars/active-tomorrow` | GET | MS3 | (none / optional date) | Consolidated ProductionOrder: [{ packId, patientId, planId, hour, address, lat, long, menu }] |
| 3 | `/api/v1/calendars/{id}/dates` | PUT | Frontend (patient) | dayId, newDate | dayId, status (applies the 48h rule) |
| 4 | `/api/v1/calendars/{id}/addresses` | POST | Frontend (patient) | dayId (or calendarId), address, lat, long | dayId, address |
| 5 | `/api/v1/calendars/{id}/cancel-day` | POST | Frontend (patient) | dayId | dayId, status=CANCELLED |
| 6 | `/api/v1/calendars/{id}` | GET | Frontend / internal | calendarId | full calendar with days |
| 7 | `/api/v1/calendars/days/{dayId}/delivery-result` | PATCH | MS5 | dayId, result(DELIVERED\|FAILED), evidence | dayId, status |
| 8 | `/api/v1/calendars/by-contract/{contractId}` | GET | Frontend / demo console | contractId | full calendar with days (404 if none) |
| 9 | `/api/v1/calendars/by-patient/{patientId}` | GET | Frontend / demo console | patientId | calendars of the patient, newest first (may be empty) |
| 10 | `/api/v1/calendars/days/{dayId}/delivery-link` | GET | Frontend / demo console | dayId | dayId, calendarId, date, dayStatus, MS5 deliveryId/routeId/linkedAt (null until routed) |

Endpoints 8–10 are read-only lookups added for cross-service gap X-17 (following a contract to its calendar and a day to its MS5 delivery without reading the databases). They reuse scope `deliveryCalendar.calendar:read`.

Note: endpoint #2 serves a dual purpose per the original requirements table — an auxiliary MS3 query **and** the mechanism by which MS3 generates the daily production order (replaces the async `ProductionOrderRequired` event documented in the architecture phase). MS4 must have tomorrow's day already resolved (recipe/portions via MS2) **before** this call arrives — never resolve on-demand inside the request.

### MS4 outbound dependency

- `ICatalogPort.GetDayMenuAsync(planId, dayNumber)` → MS2's real endpoint `GET /api/catalog/v1/meal-plans/{id}` (through the platform gateway, as service account `ms4-service` with `catalog.mealPlan:read`), filtered to `PlanDay.DayNumber == dayNumber`. MS2 has no per-date endpoint: plans are modelled by day **number**, so `DeliveryDay.DayNumber` (1..N, fixed at creation) is what MS4 sends. The original `GET /catalog/plans/{id}/day/{date}` never existed in MS2 and is superseded. `Catalog:UseMock=true` (Aspire, tests) keeps the fixture adapter.
- All endpoints in the table above are served on the Nur-Tricenter platform at `/api/delivery-calendar/v1/...` (gateway prefix `/api/delivery-calendar`, from the Consul service name) and require a bearer JWT from nur-tricenter-identity — see §5 "Nur-Tricenter platform integration".

---

## 1. Domain model (reconciled)

**Aggregate: `DeliveryCalendar` (root)**
- `Id`, `PatientId`, `ContractId`, `PlanId` (ref MS2), `Duration` (15|30), `StartDate`, `Status`
- `Days: List<DeliveryDay>`

**Entity: `DeliveryDay`** (inside `DeliveryCalendar`)
- `Id`, `Date`, `Status` (`Pending`, `Scheduled`, `ReadyForProduction`, `InProduction`, `Delivered`, `Failed`, `Cancelled`)
- `Address: DeliveryAddress?`
- `DeliveryHour: TimeOnly?`
- `RecipeRef`, `Portions` (resolved by the daily job via `ICatalogPort`, cached)
- `DeliveryEvidence?` (photo/signature/timestamp ref, when the cycle closes)

**Aggregate: `ProductionOrder` (root)** — consolidated snapshot for the next day
- `Id`, `ProductionDate` (D-1), `DeliveryDate` (D)
- `Lines: List<ProductionOrderLine>`
- `GeneratedAt`, `Status`

**Entity: `ProductionOrderLine`**
- `DeliveryDayId`, `PatientId`, `Address`, `Coordinates`, `PlanRef`, `Hour`

**Value Objects**
- `DeliveryAddress` (street, city, references, notes)
- `Coordinates` (lat, long)
- `DeliveryDate`
- `DayStatus` (enum above)
- `PlanDuration` (15|30)
- `ContractReference`, `PlanReference`, `RecipeReference`
- `PortionQuantity`

**Domain Services**
- `OrderConsolidationService`: given `DeliveryCalendar[]` with a `DeliveryDay` whose date is tomorrow and status `ReadyForProduction`, builds/updates the day's `ProductionOrder`.
- `DailyCatalogResolutionService`: daily job that, for every tomorrow's `DeliveryDay` in status `Scheduled`, calls `ICatalogPort` and sets `RecipeRef`/`Portions`, moving the day to `ReadyForProduction`.

**Invariant business rules**
- A `DeliveryDay`'s date cannot be changed nor cancelled if `Date - Now < 48h`.
- `POST /schedule` is idempotent by `contractId` (must not create a second calendar for the same contract).
- A `DeliveryDay` only moves to `Delivered`/`Failed` through MS5: its endpoint or its `DeliveryCompleted`/`DeliveryFailed` events, never manually.

**Outbound port**
- `ICatalogPort.GetDayResolution(planId, date) → (RecipeRef, Portions)`

---

## 2. Execution stages (for the agent)

### Stage 1 — Solution scaffolding (Aspire + Clean Architecture)
- Create a `.NET Aspire` solution: `MS4.AppHost`, `MS4.ServiceDefaults`, `MS4.Api`, `MS4.Application`, `MS4.Domain`, `MS4.Infrastructure`.
- `MS4.Domain`: no external dependencies (pure POCO, no EF, no ASP.NET).
- References: `Api → Application → Domain`, `Infrastructure → Application/Domain`, `Api → Infrastructure` (composition root), `AppHost → Api` + Postgres resource.
- **DoD**: `dotnet build` compiles the whole empty solution, `dotnet run` on AppHost brings up Postgres + Api with no errors.

### Stage 2 — Pure domain model
- Implement the aggregates, entities, VOs, and domain services from section 1 in `MS4.Domain`.
- VOs as immutable `record`s with validation in the constructor (e.g. `Coordinates` validates lat/long ranges).
- Business invariants (48h rule, valid state transitions) as aggregate methods (`DeliveryDay.Cancel(now)`, `DeliveryDay.Reschedule(newDate, now)`), never in an outer layer.
- **DoD**: domain unit tests (xUnit) covering: the 48h rule (exact boundary case), invalid state transitions throw a domain exception, creating an empty calendar generates the correct N days per duration.

### Stage 3 — Application layer (use cases)
- Pattern: Commands/Queries + Handlers (MediatR or manual, to be decided).
- Minimum use cases:
  - `CreateScheduleCommand` (endpoint 1)
  - `GetActiveTomorrowQuery` (endpoint 2)
  - `RescheduleDeliveryDayCommand` (endpoint 3)
  - `SetDeliveryAddressCommand` (endpoint 4)
  - `CancelDeliveryDayCommand` (endpoint 5)
  - `GetCalendarQuery` (endpoint 6)
  - `ApplyDeliveryResultCommand` (endpoint 7)
  - `ResolveDailyCatalogJob` (internal job, not exposed)
- Ports defined here: `ICalendarRepository`, `IProductionOrderRepository`, `ICatalogPort`, `IClock` (to test the 48h rule without depending on `DateTime.Now`).
- **DoD**: handlers tested against in-memory fake repos, no real infrastructure.

### Stage 4 — Infrastructure
- EF Core + Postgres: `DbContext`, entity configurations (mapping VOs as owned types), initial migrations.
- Implementation of `ICalendarRepository`, `IProductionOrderRepository`.
- `ICatalogPort`: mocked adapter (HttpClient against a local stub or a static fixture — **not** against real MS2 yet).
- `ResolveDailyCatalogJob` background job as an `IHostedService`/`BackgroundService` on a fixed schedule (e.g. daily cron) or a manual trigger for testing.
- **DoD**: migration applies against real Postgres (via Aspire), repos persist and retrieve correctly against a real DB (integration test with Testcontainers or Aspire testing).

### Stage 5 — API layer
- Minimal APIs or Controllers (preference to be decided), one endpoint per use case from section 0.
- Input validation (FluentValidation or data annotations).
- Domain errors → correct HTTP status codes (409 for a 48h rule violation, 404 for a missing calendar/day, 422 for an invalid transition).
- **DoD**: a Postman collection/`.http` file with the 7 endpoints, each manually tested against seed data.

### Stage 6 — Mock/seed data
- Seed test data: 2-3 patients, calendars in different states, so `active-tomorrow` can be tested without depending on real MS1/MS3.
- Static fixture for `ICatalogPort` (hardcoded recipe/portions responses).
- **DoD**: `dotnet run` on AppHost leaves the system with test data ready for a demo.

### Stage 7 — Acceptance tests
Verify against the already-defined criteria:
- A calendar is not created twice for the same contract.
- Cancellation is rejected if <48h remain.
- Rescheduling a date is rejected if <48h remain.
- `active-tomorrow` returns only days in `ReadyForProduction` for tomorrow's date.
- `delivery-result` updates `DayStatus` correctly and is idempotent (calling it twice with the same result doesn't break state).
- **DoD**: acceptance test suite (integration, against real Postgres via Aspire) passing.

---

## 3. Open decisions for the agent (resolve before coding, don't assume silently)

1. MediatR vs. manual handlers for the Application layer.
2. Minimal APIs vs. Controllers for MS4.Api.
3. Daily job mechanism: `BackgroundService` with a `Timer`/cron vs. an internal endpoint triggered manually at this stage (simpler for a demo).
4. How the frontend/patient authenticates against these endpoints at this stage (no auth for now, since it's a mock?).

---

## 4. Out of scope at this stage (already agreed during the architecture phase)
- ~~A real message broker.~~ Superseded by the platform integration: MS4 now **consumes** RabbitMQ events (see §5). It still publishes none.
- ~~Service-to-service authentication.~~ Superseded: MS4 → MS2 calls authenticate as service account `ms4-service`.
- Outbox pattern (MS4 publishes no events, so it has nothing to put in one).
- Event schema versioning (contracts are shared via the `Nur.Tricenter.Integration` kernel package).

---

## 5. Current implementation status (for agents — read before touching code)

See also [`README.md`](./README.md) (how to run/test) and [`FLOWS.md`](./FLOWS.md) (every use case, expected behavior, error scenarios).

**Done**: Stage 0 (template cleanup) → Stage 7 (end-to-end acceptance tests). All 7 stages complete.

### §3 decisions already resolved
1. MediatR (not manual handlers) — already wired in `Application/DependencyInjection.cs`.
2. Minimal APIs via the `IEndpointGroup` pattern (`src/Api/Infrastructure/`), not Controllers.
3. Daily job: **manual trigger endpoint** (`POST /api/v1/calendars/resolve-daily-catalog`), no `BackgroundService`. Not part of the public §0 contract — it's ops/demo tooling.
4. ~~No auth at this stage.~~ Superseded by the platform integration: every endpoint requires a bearer JWT from nur-tricenter-identity with the endpoint's `deliveryCalendar.*` scope (ASP.NET Identity stays removed — MS4 only *validates* tokens).

### Technical gotchas already solved (don't repeat them)
- **EF Core owned-type constructor binding doesn't support a nested navigation as a constructor parameter.** `DeliveryAddress` nests `Coordinates` as a ctor param — EF can't bind that via constructor binding. Fix applied: a private parameterless ctor + private setters on `DeliveryAddress` (same pattern already used by entities). If you add another VO that nests another owned VO, you'll hit the same issue.
- **The same owned-type instance in two navigations on the same entity breaks EF's change tracker.** `ProductionOrderLine` has `Coordinates` both embedded in `Address` and duplicated as a top-level field (an explicit §1 requirement). Never reuse the reference — always `new Coordinates(...)`, not `address.Coordinates`.
- **The `dotnet ef` tool must match the `Microsoft.EntityFrameworkCore.Design` version** in `Directory.Packages.props`, or it throws `MSB4057`. See README's "Database migrations" section.
- **`ApplicationDbContextInitialiser` uses `MigrateAsync()`**, not `EnsureCreated` — real migrations are already committed (`src/Infrastructure/Data/Migrations/`).
- **Enums in JSON**: a global `JsonStringEnumConverter` is configured (`Api/DependencyInjection.cs`) — every enum (`DayStatus`, `DeliveryResult`, etc.) serializes/deserializes as a string, not an int.
- **`AddProblemDetails()` is required** alongside `AddExceptionHandler<ProblemDetailsExceptionHandler>()` — without it, any exception the custom handler doesn't recognize breaks with an ugly meta-exception instead of a clean 500.
- **EF-generated ids aren't populated until `SaveChangesAsync`.** `DeliveryDay.Id` is 0 until the owning `DeliveryCalendar` is saved. Building a `ProductionOrder` via `OrderConsolidationService.Consolidate` from an unsaved calendar bakes `ProductionOrderLine.DeliveryDayId = 0` into every line. Always save calendars first, then consolidate (see `ApplicationDbContextInitialiser.TrySeedAsync` for the two-phase save).

### Domain exception → HTTP mapping (already implemented in `ProblemDetailsExceptionHandler`)
`ValidationException`→400, `NotFoundException`→404, `DeliveryWindowLockedException`→409, `InvalidDeliveryDayTransitionException`→422.

### Acceptance tests (Stage 7)
Two complementary test files in `tests/Application.FunctionalTests/Calendars/`, both against a real
Postgres instance (Aspire-orchestrated via `TestAppHost`, reset with Respawn between tests via
`TestBase.SetUp`):
- `CalendarAcceptanceTests.cs` — the 5 criteria from §2 Stage 7, sent through the MediatR pipeline
  directly (`TestApp.SendAsync`), bypassing HTTP. Exceptions surface as CLR types (`Should.ThrowAsync<T>`).
- `CalendarsEndpointsTests.cs` — same kind of scenarios but through `WebApiFactory.CreateClient()`
  (real HTTP), asserting actual status codes and `ProblemDetails` bodies. This is what actually covers
  the Stage 5 API layer (routing, DTO binding, `ProblemDetailsExceptionHandler` mapping, enum JSON) —
  previously only manually verified via `Api.http`. Client-side `JsonSerializerOptions` must include
  `JsonStringEnumConverter` to match the server's `ConfigureHttpJsonOptions`, or enum fields
  (`DayStatus`, etc.) fail to deserialize.

`IClock` is the real `SystemClock` in both (no override wired in `WebApiFactory`), so 48h-boundary
cases use `DateTime.UtcNow`-relative dates rather than a fixed instant.

### Where things live
- Repos: `ICalendarRepository`/`IProductionOrderRepository` (ports in Application) → `CalendarRepository`/`ProductionOrderRepository` (impl in `Infrastructure/Data/Repositories`), take the concrete `ApplicationDbContext` directly, not `IApplicationDbContext` (that interface is just the `SaveChangesAsync` unit-of-work).
- Mocked `ICatalogPort`: `Infrastructure/Catalog/MockCatalogPort.cs` — deterministic fixture keyed by `planId`+`date`, no real HTTP call.
- Endpoints: `src/Api/Endpoints/Calendars/`, one file per endpoint (partial `Calendars` class, `RoutePrefix = "/api/v1"`), split into `Commands/` and `Queries/` subfolders — mirrors the `src/Application/Calendars/Commands|Queries` layout. `Calendars.cs` at the folder root only holds the `IEndpointGroup.Map` route table; each handler + its request record lives in its own file under the matching subfolder (`Commands/CreateSchedule.cs`, `Queries/GetCalendar.cs`, etc.).
- Manual test collection: `src/Api/Api.http`.
- Seed data: `ApplicationDbContextInitialiser.TrySeedAsync` — 3 patients (fixed GUIDs `aaaaaaaa-...0001/2/3`) covering all `DayStatus` values except `InProduction`: Patient 1 all-`Pending`, Patient 2 has a `ReadyForProduction` day for tomorrow (pre-consolidated into a `ProductionOrder` so `active-tomorrow` has data immediately) plus a `Scheduled` day, Patient 3 has `Delivered`/`Failed`/`Cancelled` days. Guarded by `if (await _context.Calendars.AnyAsync()) return;` — only seeds an empty DB.

### Nur-Tricenter platform integration (2026-10)

MS4 runs in two modes from the same code, switched purely by configuration:

| | Standalone (Aspire) | Platform (nur-tricenter-local) |
|---|---|---|
| Start | `dotnet run --project src/AppHost` | `nur-tricenter-local/scripts/start-stack.ps1` (includes `ms4.compose.yml`) |
| Postgres | own container | shared `nur-tricenter-postgres`, DB `ms4_delivery_calendar` |
| Consul | off (`ServiceRegistration:ServiceDiscoveryAddress` blank) | registers as `delivery-calendar` → gateway route `/api/delivery-calendar/**` |
| Auth | JWT, dev key injected by AppHost (`Parameters:jwt-secret`) | JWT, same dev key as identity |
| MS2 | `MockCatalogPort` (`Catalog:UseMock=true`) | `HttpCatalogPort` via gateway, service account `ms4-service` |
| RabbitMQ | not used | `src/WorkerService` consumes `ms4.meal-plan-published`, `ms4.recipe-updated`, `ms4.delivery-added-to-route`, `ms4.delivery-completed`, `ms4.delivery-failed` |
| Schema | migrate + seed on startup (Development) | `--migrate` one-shot job; seed off |

**Platform pieces and where they live:**
- Kernels: `Nur.Tricenter.Access` (scopes `DeliveryCalendarPermission`, added to the kernel in 1.1.0), `Nur.Tricenter.Security` (`AddSecurity`), `Nur.Tricenter.Integration` (message contracts). `Nur.Tricenter.Observability` is deliberately NOT used — ServiceDefaults already registers the same OTel pipeline. Feed: GitHub Packages (`NuGet.config`, needs `NUGET_USERNAME`/`NUGET_TOKEN`).
- Scopes per endpoint: `Api/Endpoints/Calendars/Calendars.cs` (`RequireAuthorization`). `/health` and `/alive` are anonymous and mapped in every environment (Consul probes `/health`).
- Consul: `Infrastructure/ServiceDiscovery/`. Inbox + projections + consumers: `Infrastructure/Messaging/`. MS2 adapter + service-account token: `Infrastructure/Catalog/`.
- Platform compose fragment: `ms4.compose.yml` (bind-mounts `artifacts/bin/<Project>/debug` — this repo sets `ArtifactsPath`).

**Domain changes the integration forced:** MS2 plans have several recipes per day, keyed by day number. `DeliveryDay` therefore has `DayNumber` (fixed at creation; rescheduling keeps it) and a `Menu` owned collection (`MenuItem`: meal time, recipe id/name, rations); `Portions` = total rations. `RecipeRef`/`RecipeReference` were removed.

**Gotchas:**
- **`Jwt:SecretKey` is mandatory at host build** (the kernel throws). Tests set it in `WebApiFactory`; `dotnet ef` needs `Jwt__SecretKey=<anything>` in the environment; build-time OpenAPI generation gets a throwaway key (`DependencyInjection.AddPlatformSecurity`).
- **`--migrate` must be stripped from `args`** before `WebApplication.CreateBuilder` — the command-line config provider rejects a bare switch.
- **Consumer decorators take the concrete inner consumer** (`IdempotentConsumer<T, TInner>`), never `IIntegrationMessageConsumer<T>`: Joseco registers the pipeline under that interface, so injecting it is a DI cycle.
- **Npgsql only writes UTC `DateTimeOffset`s** — `InboxMessage`/`PlanCatalogEntry` normalise with `ToUniversalTime()`.
- **Joseco.Communication.External.RabbitMQ 1.1.0 ignores `RabbitMqSettings:Port`** (broker must be on 5672). Queues/bindings are declared by nur-tricenter-local's `rabbitmq-definitions.json`, not here.
- **Docker Compose 2.29** rejects differing declarations of the same external network across `include:`d files (`conflicts with imported resource`). Keep `ms4.compose.yml`'s `networks:` block byte-identical in meaning to the other fragments (`external: true`, no `name:`).

**Known gap (security):** scopes gate *which kind of caller* may use an endpoint, not *whose* calendar it is. A token with `deliveryCalendar.calendar:edit` can edit any patient's days (broken object-level authorization). Closing it needs a mapping from the identity token's `sub` to MS4's `PatientId`, which the platform doesn't provide yet (patients live in MS1).

### Cross-service integration gaps (master endpoint-table audit, 2026-07-10)
An audit of the org-wide microservice endpoint table (MS1-MS5, cross-service REST calls) against this
repo's own §0 contract confirmed **MS4's implementation already satisfies every endpoint the master
table assigns to it** — no code defect. Three gaps surfaced, all upstream/org-level, not fixable
unilaterally in this repo:

1. **MS5 → MS4 `delivery-result` call (§0 endpoint #7) is missing from the master table entirely.**
   It's implemented here (`ApplyDeliveryResultCommand`) and backs a domain invariant (§1: a
   `DeliveryDay` only reaches `Delivered`/`Failed` via this call) — but the master table never shows
   MS5 calling MS4 anywhere. **Update (2026-10-28):** MS5 now publishes `DeliveryAddedToRoute`,
   `DeliveryCompleted` and `DeliveryFailed`, and MS4 consumes them (`src/Infrastructure/Messaging/`,
   table `DeliveryRouteLinks`). Packages from MS3's `PackageValidated` carry MS3's own GUID, not
   MS4's `packId` (ms3-gap-03), so MS4's day id now travels in `DeliveryDayId` (kernel 1.4.0,
   MS3 → MS5 → MS4). `DeliveryAddedToRouteConsumer` looks the day up by `DeliveryDayId`, then a numeric
   `PackageId`, then falls back to matching by patient (earliest `ReadyForProduction`/`InProduction`
   day not linked yet). Verified end to end on the local stack on 2026-10-28 (patient fallback).

2. ~~**MS4 ↔ MS2 outbound catalog contract has three conflicting shapes.**~~ **Resolved** against
   MS2's actual code (ms2-plans-and-recipes-catalog): none of the three documented shapes exists
   there. MS4 now uses the endpoint MS2 really exposes, `GET /api/v1/meal-plans/{id}` (full
   composition), and picks the plan day by `DayNumber`. The master table should be updated to say so.

3. **Minor: `GET /calendars/{id}` (§0 endpoint #6) isn't listed in the master table**, unlike similar
   frontend-read endpoints for other services. Low risk (frontend-only, no cross-MS dependency,
   already implemented as `GetCalendarQuery`) — worth a line item upstream for completeness, not a
   blocker.
