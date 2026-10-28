# MS4 — Delivery Calendar — Flows and Expected Behavior

Living document. Covers everything implemented through Stage 5 (Domain + Application + Infrastructure + API). Each flow maps to a use case (`Commands`/`Queries` under `src/Application/Calendars`) and to an endpoint in the `CLAUDE.md` §0 table. HTTP status codes are live — see `src/Web/Infrastructure/ProblemDetailsExceptionHandler.cs` for the exception → status mapping.

Conventions:
- **Actor**: who triggers the flow (MS3, Frontend/patient, MS5, internal job).
- **Precondition**: required state before execution.
- **Happy path**: expected behavior with no errors.
- **Error scenarios**: each with the domain/application exception thrown and the resulting HTTP code.
- **Idempotency / edge cases**: behavior on retries, exact boundaries, logical race conditions.

---

## A. Calendar creation

### A.1 — Create delivery schedule (`CreateScheduleCommand`)

- **Actor**: MS3 (`POST /api/v1/schedule`).
- **Input**: `contractId`, `patientId`, `planId`, `duration` (15|30), `startDate`.
- **Precondition**: none — this is the aggregate's entry point.
- **Happy path**: creates a `DeliveryCalendar` in `Active` status, generates `duration` consecutive `DeliveryDay`s starting at `startDate`, all in `Status = Pending`, no address or recipe. Returns `calendarId` + the list of days.
- **Error scenarios**:
  - `duration` other than 15 or 30 → `ArgumentOutOfRangeException` in the `PlanDuration` ctor → **422**.
  - `contractId`/`patientId`/`planId` = `Guid.Empty` → `ArgumentException` in the corresponding VO → **422**.
  - `startDate` = `default` → `ArgumentException` in the `DeliveryDate` ctor → **422**.
- **Idempotency**: if a calendar already exists for the same `contractId`, **no new one is created** — the existing calendar is returned as-is (same `calendarId`, same days, no regeneration). This is intentional: MS3 replays (network retries, etc.) must not duplicate. Enforced twice: in the handler (`GetByContractIdAsync` before creating) and in the DB (unique index on `ContractId`).
- **Test coverage**: `CreateScheduleCommandTests`, `CalendarRepositoryTests.GetByContractIdAsync_...`.

---

## B. Delivery day mutation

All flows in this category operate on a `DeliveryDay` inside an already-existing `DeliveryCalendar`.

### B.1 — Set delivery address (`SetDeliveryAddressCommand`)

- **Actor**: Frontend/patient (`POST /api/v1/calendars/{id}/addresses`).
- **Input**: `calendarId`, `dayId`, `street`, `city`, `latitude`, `longitude`, `references?`, `notes?`.
- **Precondition**: day in `Pending` or `Scheduled`.
- **Happy path**: builds a `DeliveryAddress` (embeds `Coordinates` — the endpoint delivers address + lat/long in a single payload, see the note in `DeliveryAddress.cs`), assigns it to the day, day moves to `Status = Scheduled`. Can be called again to update the address while the day stays `Pending`/`Scheduled`.
- **Error scenarios**:
  - `calendarId` doesn't exist → `NotFoundException` → **404**.
  - `dayId` doesn't belong to the calendar → `NotFoundException` → **404**.
  - Day is no longer `Pending`/`Scheduled` (e.g. `ReadyForProduction`, `Delivered`, `Cancelled`) → `InvalidDeliveryDayTransitionException` → **422**.
  - `latitude`/`longitude` out of range → `ArgumentOutOfRangeException` in the `Coordinates` ctor → **422**.
  - `street`/`city` empty → `ArgumentException` in the `DeliveryAddress` ctor → **422**.
- **Idempotency**: calling it twice with the same address while the day is still `Scheduled` is a functional no-op (overwrites with the same value, no error).
- **Test coverage**: `SetDeliveryAddressCommandTests`, `CalendarRepositoryTests.SetAddress_ThenGetByDeliveryDayId_...`.

### B.2 — Reschedule delivery date (`RescheduleDeliveryDayCommand`)

- **Actor**: Frontend/patient (`PUT /api/v1/calendars/{id}/dates`).
- **Input**: `calendarId`, `dayId`, `newDate`.
- **Precondition**: day in `Pending` or `Scheduled`, and **outside the 48h lock window** (see the global rule below).
- **Happy path**: updates the day's `Date`. Does not change `Status`.
- **Error scenarios**:
  - `calendarId`/`dayId` don't exist → `NotFoundException` → **404**.
  - Less than 48h remain until the day's **current** date (before rescheduling) → `DeliveryWindowLockedException` → **409**.
  - Day in `ReadyForProduction`/`InProduction`/`Delivered`/`Failed`/`Cancelled` (even if outside the 48h window) → `InvalidDeliveryDayTransitionException` → **422**.
- **48h rule — exact boundary case**: compared against the day's **current** date (not the new one). `deliveryInstant - now < 48h` → locked. Exactly 48h0m0s0ms → allowed (not locked). 48h minus 1 tick → locked. Covered in `DeliveryDayTests` with both exact boundaries.
- **Design note**: the rule is evaluated against the date in effect before the change, not against `newDate`. Rescheduling to a date very close in time (<48h in the future) is allowed as long as the day's current date is outside the window — the system doesn't prevent scheduling urgent deliveries, it only protects against moving a delivery that's already imminent.
- **Test coverage**: `RescheduleDeliveryDayCommandTests`, `DeliveryDayTests` (exact boundary).

### B.3 — Cancel delivery day (`CancelDeliveryDayCommand`)

- **Actor**: Frontend/patient (`POST /api/v1/calendars/{id}/cancel-day`).
- **Input**: `calendarId`, `dayId`.
- **Precondition**: day is not already `Delivered`, `Failed`, `Cancelled`, or `InProduction`, and is outside the 48h window.
- **Happy path**: day moves to `Status = Cancelled`. Terminal — cannot be reverted.
- **Error scenarios**:
  - `calendarId`/`dayId` don't exist → `NotFoundException` → **404**.
  - Less than 48h remain until the day's date → `DeliveryWindowLockedException` → **409**.
  - Day already `Delivered`/`Failed`/`Cancelled`/`InProduction` → `InvalidDeliveryDayTransitionException` → **422**.
- **Note**: unlike reschedule, cancel **does** allow cancelling a `ReadyForProduction` day (as long as it's outside the 48h window) — it only blocks terminal/in-progress states.
- **Test coverage**: `CancelDeliveryDayCommandTests`.

---

## C. Production pipeline (internal, no direct HTTP exposure)

### C.1 — Daily catalog resolution (`ResolveDailyCatalogCommand`)

- **Actor**: internal job. There is no `BackgroundService` trigger — it runs by invoking the command directly (tests) or via the manual trigger endpoint (`POST /api/v1/calendars/resolve-daily-catalog`, resolved open decision from `CLAUDE.md` §3.3, chosen for simplicity at this stage).
- **Input**: optional `targetDate` (if `null`, uses "tomorrow" relative to `IClock.Now`).
- **Precondition**: must run **before** MS3 calls `GET /calendars/active-tomorrow` for the same date — MS4 never resolves the catalog on-demand inside that request.
- **Happy path**:
  1. Finds every calendar with a day landing on `targetDate` (`ICalendarRepository.GetActiveForDeliveryDateAsync`).
  2. For each day in `Status = Scheduled` on `targetDate`: calls `ICatalogPort.GetDayMenuAsync(planId, day.DayNumber)` — on the platform, MS2's `GET /api/catalog/v1/meal-plans/{planId}` through the gateway (cached per plan for the run); standalone, the deterministic mock — stores the day's `Menu` (meal time, recipe, rations) and `Portions` (total rations); day moves to `ReadyForProduction`. `DayNumber` is the plan day fixed at calendar creation, so a rescheduled day still gets its original plan day's menu.
  3. Rebuilds/updates `targetDate`'s `ProductionOrder` via `OrderConsolidationService.Consolidate` — picks up **every** `ReadyForProduction` day for that date (not just the ones resolved in this run), across all calendars.
- **Error scenarios**:
  - A `Scheduled` day with no `Address` assigned, at consolidation time → `InvalidDeliveryDayTransitionException` (thrown by `OrderConsolidationService`, not by the job) — a day with no address should never actually reach `ReadyForProduction` in practice because `MarkReadyForProduction` doesn't validate this; it's a cross-step invariant to watch in Stage 7.
  - MS2 has no menu for that plan/day (unknown plan, day without recipes) → the day is **skipped and logged**, stays `Scheduled`.
  - MS2 unreachable / 5xx / service account rejected → that day is **skipped and logged**, stays `Scheduled`; the rest of the run continues. Re-running the job (idempotent) is the recovery path.
  - `Pending` days (no address) on `targetDate` — silently ignored (not resolved, not added to the order). They stay pending until the patient assigns an address and the job runs again.
- **Idempotency / re-runs**: running the job twice for the same date is safe. If a `ProductionOrder` already exists for `targetDate`, `ReplaceLines` is called (replaces all lines, updates `GeneratedAt`) instead of creating a duplicate — also enforced by a unique index on `ProductionOrders.DeliveryDate`.
- **Test coverage**: `ResolveDailyCatalogCommandTests`, `OrderConsolidationServiceTests`.

### C.2 — Production order consolidation (`OrderConsolidationService`, pure domain service)

- **Invoked by**: C.1, never directly from the outside.
- **Behavior**: given a set of calendars + a date + an existing order (or `null`), filters `ReadyForProduction` days matching the date, builds one `ProductionOrderLine` per day (with `PatientId`, `Address`, `PlanRef`, `Hour`), and creates or replaces the order's lines.
- **Design edge case**: `Coordinates` lives both embedded in `DeliveryAddress` and duplicated as a top-level field on `ProductionOrderLine` (explicit `CLAUDE.md` §1 requirement on the entity's shape). These are distinct VO instances built from the same value — never the same object reference (see implementation note: EF Core doesn't allow the same owned-type instance to sit in two different navigations on the same entity).

---

## D. Delivery result (cycle close-out)

### D.1 — Apply delivery result (`ApplyDeliveryResultCommand`)

- **Actor**: MS5, exclusively — through `PATCH /api/v1/calendars/days/{dayId}/delivery-result`, or through its `DeliveryCompleted` / `DeliveryFailed` events (`ms4.delivery-completed` / `ms4.delivery-failed`, WorkerService). The event path resolves the day via `DeliveryRouteLinks`, filled from `DeliveryAddedToRoute` (`PackageId` = MS4 `packId`), applies the same `DeliveryDay.ApplyDeliveryResult` rule (evidence = `DeliveredAt` only), and logs + acknowledges instead of failing when the delivery is unlinked, the day is gone, or the transition is invalid (e.g. cancelled day).
- **Input**: `dayId`, `result` (`Delivered`|`Failed`), `photoUrl?`, `signatureUrl?`.
- **Precondition**: day in `ReadyForProduction` or `InProduction`.
- **Happy path**: day moves to `Delivered` or `Failed` depending on `result`. If `photoUrl` or `signatureUrl` is provided, a `DeliveryEvidence` (timestamp = `IClock.Now`) is built and stored on the day.
- **Error scenarios**:
  - `dayId` doesn't exist (lookup is by `dayId` only — the endpoint doesn't receive `calendarId` per the contract) → `NotFoundException` → **404**.
  - Day in a status other than `ReadyForProduction`/`InProduction` **and** the requested result differs from the current state → `InvalidDeliveryDayTransitionException` → **422**. Example: requesting `Delivered` on a `Pending` or `Cancelled` day.
- **Idempotency — the central rule of this flow**: calling it twice with the **same** `result` on a day already in that target status is a **silent no-op** — returns the current state without throwing, doesn't touch `Evidence` again. This is what guarantees that MS5 retries (timeouts, redelivered events) don't break state. If a **different** `result` is requested than what's already applied (e.g. day already `Delivered`, requesting `Failed`) → `InvalidDeliveryDayTransitionException` (not a valid result change, it's terminal).
- **Never reached via any other path** — explicit domain invariant in `CLAUDE.md`: only MS5 (the endpoint or its delivery events) can perform this transition to `Delivered`/`Failed`.
- **Test coverage**: `ApplyDeliveryResultCommandTests` (includes an explicit identical-double-call case).

---

## E. Queries

### E.1 — Get full calendar (`GetCalendarQuery`)

- **Actor**: Frontend / internal (`GET /api/v1/calendars/{id}`).
- **Happy path**: returns the full calendar with all its days, unfiltered by status.
- **Error scenarios**: `calendarId` doesn't exist → `NotFoundException` → **404**.
- **Test coverage**: `GetCalendarQueryTests`.

### E.2 — Active production order for tomorrow (`GetActiveTomorrowQuery`)

- **Actor**: MS3 (`GET /api/v1/calendars/active-tomorrow`), dual purpose: auxiliary query **and** the mechanism by which MS3 obtains the daily production order (replaces the async `ProductionOrderRequired` event).
- **Input**: optional `date` (default: tomorrow relative to `IClock.Now`).
- **Precondition**: assumes C.1 (`ResolveDailyCatalogCommand`) already ran for that date. **This query never triggers on-demand resolution.**
- **Happy path**: reads the already-consolidated `ProductionOrder` for the date, returns its lines mapped to `{ packId, patientId, planId, hour, address, lat, long }`. `packId` maps to `DeliveryDayId` (there's no separate "pack" concept in the domain — it's the closest identifiable delivery unit, a design decision documented in `GetActiveTomorrowQuery.cs`).
- **No-data case**: if no `ProductionOrder` exists for the date (job hasn't run yet, or no days were eligible), returns an **empty list**, not an error.
- **Content guarantee**: can only return days that were `ReadyForProduction` at the time consolidation ran — never `Pending`/`Scheduled`/`Cancelled` days.
- **Test coverage**: `GetActiveTomorrowQueryTests`.

---

## Global rules (apply across multiple flows)

### 48h lock rule (`DeliveryDay.EnsureOutsideLockWindow`)

Applies to: B.2 (reschedule), B.3 (cancel). Does not apply to B.1 (set address), C.1, D.1.

- Formula: `deliveryInstant - now < 48h` → locked (`DeliveryWindowLockedException`).
- `deliveryInstant` is built as UTC midnight of the day's date (`DeliveryDate.Value.ToDateTime(TimeOnly.MinValue)`, zero offset) — does not use `DeliveryHour`.
- Exact boundary: at exactly **48h00m00s000ms** → allowed. At **47h59m59.999s** → locked. Verified in `DeliveryDayTests` with `AddHours(-48)` (allowed) vs `AddHours(-48).AddTicks(1)` (locked).

### Valid state transitions (`DayStatus`)

```
Pending ──SetAddress──> Scheduled ──MarkReadyForProduction──> ReadyForProduction ──ApplyDeliveryResult──> Delivered
   │                        │                                        │                                       │
   │                        │                                        └──ApplyDeliveryResult──> Failed         │
   │                        │                                                                                 │
   ├──Reschedule (outside 48h, self-loop)                                                                     │
   ├──Cancel (outside 48h)──> Cancelled  <──Cancel (outside 48h)── Scheduled                                   │
   └──Cancel (outside 48h)──> Cancelled                                                                        │
                                          ReadyForProduction ──Cancel (outside 48h)──> Cancelled                │
                                          InProduction ──ApplyDeliveryResult──> Delivered/Failed (idempotent)  ┘
```

Any transition not listed above throws `InvalidDeliveryDayTransitionException`. `InProduction` has no command of its own yet (nothing in the current set of flows explicitly sets it — MS4 doesn't expose a "start production" step; it stays a reserved enum state, reachable only if a future stage decides to expose it).

### Idempotency — cross-flow summary

| Flow | Idempotency mechanism |
|---|---|
| A.1 CreateSchedule | Prior lookup by `ContractId` + unique DB index |
| C.1 ResolveDailyCatalog | `ReplaceLines` on the existing order + unique index on `DeliveryDate` |
| D.1 ApplyDeliveryResult | No-op if `Status` already equals the requested `targetStatus` |

---

## Out of scope for these flows (documented, not implemented)

- Per-patient authorization — every endpoint requires a JWT with its `deliveryCalendar.*` scope (401 without a token, 403 with the wrong scope), but nothing checks that a frontend caller owns the calendar it edits (see `CLAUDE.md` §5, known gap).
- Explicit transition to `InProduction`.
- Outbox for `ICatalogPort` — transient MS2 failures are retried by the HTTP resilience handler (ServiceDefaults); a day that still fails stays `Scheduled` until the next run.
- Automatic trigger (cron/`BackgroundService`) for C.1 — resolved as manual trigger, see `CLAUDE.md` §5.
