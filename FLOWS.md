# MS4 — Delivery Calendar — Flujos y Comportamiento Esperado

Documento vivo. Cubre lo implementado hasta Etapa 4 (Domain + Application + Infrastructure). Cada flujo mapea a un caso de uso (`Commands`/`Queries` en `src/Application/Calendars`) y, eventualmente, a un endpoint de la tabla en `CLAUDE.md` §0. Los códigos HTTP marcados como "(Etapa 5)" aún no existen — son el mapeo objetivo ya decidido, pendiente de implementar en la capa API.

Convenciones:
- **Actor**: quién dispara el flujo (MS3, Frontend/paciente, MS5, job interno).
- **Precondición**: estado requerido antes de ejecutar.
- **Happy path**: comportamiento esperado sin errores.
- **Escenarios de error**: cada uno con la excepción de dominio/aplicación lanzada y el código HTTP objetivo.
- **Idempotencia / edge cases**: comportamiento en reintentos, límites exactos, condiciones de carrera lógicas.

---

## A. Creación de calendario

### A.1 — Crear calendario de entregas (`CreateScheduleCommand`)

- **Actor**: MS3 (`POST /api/v1/schedule`).
- **Input**: `contractId`, `patientId`, `planId`, `duration` (15|30), `startDate`.
- **Precondición**: ninguna — es el punto de entrada del aggregate.
- **Happy path**: crea `DeliveryCalendar` en estado `Active`, genera `duration` `DeliveryDay`s consecutivos desde `startDate`, todos en `Status = Pending`, sin address ni recipe. Devuelve `calendarId` + lista de días.
- **Escenarios de error**:
  - `duration` distinto de 15 o 30 → `ArgumentOutOfRangeException` en `PlanDuration` ctor → **422** (Etapa 5).
  - `contractId`/`patientId`/`planId` = `Guid.Empty` → `ArgumentException` en el VO correspondiente → **422** (Etapa 5).
  - `startDate` = `default` → `ArgumentException` en `DeliveryDate` ctor → **422** (Etapa 5).
- **Idempotencia**: si ya existe un calendario con el mismo `contractId`, **no crea uno nuevo** — devuelve el calendario existente tal cual (mismo `calendarId`, mismos días, sin re-generar). Esto es intencional: replays de MS3 (reintentos de red, etc.) no deben duplicar. Enforced doblemente: en el handler (`GetByContractIdAsync` antes de crear) y en DB (índice único sobre `ContractId`).
- **Test coverage**: `CreateScheduleCommandTests`, `CalendarRepositoryTests.GetByContractIdAsync_...`.

---

## B. Mutación de días de entrega

Todos los flujos de esta categoría operan sobre un `DeliveryDay` dentro de un `DeliveryCalendar` ya existente.

### B.1 — Fijar dirección de entrega (`SetDeliveryAddressCommand`)

- **Actor**: Frontend/paciente (`POST /api/v1/calendars/{id}/addresses`).
- **Input**: `calendarId`, `dayId`, `street`, `city`, `latitude`, `longitude`, `references?`, `notes?`.
- **Precondición**: día en `Pending` o `Scheduled`.
- **Happy path**: construye `DeliveryAddress` (incluye `Coordinates` embebidas — endpoint entrega address + lat/long en un solo payload, ver nota en `DeliveryAddress.cs`), la asigna al día, día pasa a `Status = Scheduled`. Puede llamarse de nuevo para actualizar la dirección mientras el día siga en `Pending`/`Scheduled`.
- **Escenarios de error**:
  - `calendarId` no existe → `NotFoundException` → **404**.
  - `dayId` no pertenece al calendario → `NotFoundException` → **404**.
  - Día ya no está en `Pending`/`Scheduled` (ej. `ReadyForProduction`, `Delivered`, `Cancelled`) → `InvalidDeliveryDayTransitionException` → **422**.
  - `latitude`/`longitude` fuera de rango → `ArgumentOutOfRangeException` en `Coordinates` ctor → **422**.
  - `street`/`city` vacíos → `ArgumentException` en `DeliveryAddress` ctor → **422**.
- **Idempotencia**: llamar dos veces con la misma dirección mientras el día sigue `Scheduled` es un no-op funcional (sobreescribe con el mismo valor, sin error).
- **Test coverage**: `SetDeliveryAddressCommandTests`, `CalendarRepositoryTests.SetAddress_ThenGetByDeliveryDayId_...`.

### B.2 — Reprogramar fecha de entrega (`RescheduleDeliveryDayCommand`)

- **Actor**: Frontend/paciente (`PUT /api/v1/calendars/{id}/dates`).
- **Input**: `calendarId`, `dayId`, `newDate`.
- **Precondición**: día en `Pending` o `Scheduled`, y **fuera de la ventana de bloqueo de 48h** (ver regla global abajo).
- **Happy path**: actualiza `Date` del día. No cambia `Status`.
- **Escenarios de error**:
  - `calendarId`/`dayId` no existen → `NotFoundException` → **404**.
  - Faltan <48h para la fecha **actual** del día (antes de reprogramar) → `DeliveryWindowLockedException` → **409**.
  - Día en estado `ReadyForProduction`/`InProduction`/`Delivered`/`Failed`/`Cancelled` (incluso si estuviera fuera de la ventana 48h) → `InvalidDeliveryDayTransitionException` → **422**.
- **Regla 48h — caso límite exacto**: se compara contra la fecha **actual** del día (no la nueva). `deliveryInstant - now < 48h` → bloqueado. Exactamente a 48h0m0s0ms → permitido (no bloqueado). A 48h menos 1 tick → bloqueado. Cubierto en `DeliveryDayTests` con ambos límites exactos.
- **Nota de diseño**: la regla se evalúa sobre la fecha vigente antes del cambio, no sobre `newDate`. Reprogramar a una fecha muy próxima (<48h en el futuro) está permitido siempre que el día actual esté fuera de ventana — el sistema no impide agendar entregas urgentes, solo protege contra mover una entrega ya inminente.
- **Test coverage**: `RescheduleDeliveryDayCommandTests`, `DeliveryDayTests` (boundary exacto).

### B.3 — Cancelar día de entrega (`CancelDeliveryDayCommand`)

- **Actor**: Frontend/paciente (`POST /api/v1/calendars/{id}/cancel-day`).
- **Input**: `calendarId`, `dayId`.
- **Precondición**: día no está ya en `Delivered`, `Failed`, `Cancelled` o `InProduction`, y fuera de la ventana de 48h.
- **Happy path**: día pasa a `Status = Cancelled`. Terminal — no se puede revertir.
- **Escenarios de error**:
  - `calendarId`/`dayId` no existen → `NotFoundException` → **404**.
  - Faltan <48h para la fecha del día → `DeliveryWindowLockedException` → **409**.
  - Día ya `Delivered`/`Failed`/`Cancelled`/`InProduction` → `InvalidDeliveryDayTransitionException` → **422**.
- **Nota**: a diferencia de reschedule, cancel **sí** permite cancelar un día `ReadyForProduction` (siempre que esté fuera de la ventana 48h) — solo bloquea los estados terminales/en curso.
- **Test coverage**: `CancelDeliveryDayCommandTests`.

---

## C. Pipeline de producción (interno, sin exposición HTTP directa)

### C.1 — Resolución diaria de catálogo (`ResolveDailyCatalogCommand`)

- **Actor**: job interno. En Etapa 4 no hay disparador automático (`BackgroundService`) — se ejecuta invocando el comando directamente (tests) o, en Etapa 5, vía un endpoint de trigger manual (decisión abierta de `CLAUDE.md` §3.3, resuelta a favor de trigger manual por simplicidad de esta etapa).
- **Input**: `targetDate` opcional (si es `null`, usa "mañana" relativo a `IClock.Now`).
- **Precondición**: debe correr **antes** de que MS3 llame `GET /calendars/active-tomorrow` para la misma fecha — MS4 nunca resuelve catálogo on-demand dentro de ese request.
- **Happy path**:
  1. Busca todos los calendarios con algún día en `targetDate` (`ICalendarRepository.GetActiveForDeliveryDateAsync`).
  2. Para cada día en `Status = Scheduled` que caiga en `targetDate`: llama `ICatalogPort.GetDayResolutionAsync(planId, date)` (mockeado — fixture determinística, no HTTP real a MS2), setea `RecipeRef`/`Portions`, día pasa a `ReadyForProduction`.
  3. Reconstruye/actualiza el `ProductionOrder` de `targetDate` con `OrderConsolidationService.Consolidate` — toma **todos** los días `ReadyForProduction` de esa fecha (no solo los recién resueltos en este run), across calendars.
- **Escenarios de error**:
  - Día en `Scheduled` sin `Address` asignada, al momento de consolidar → `InvalidDeliveryDayTransitionException` (lanzada por `OrderConsolidationService`, no por el job) — un día sin dirección nunca debería llegar a `ReadyForProduction` en la práctica porque `MarkReadyForProduction` no valida esto; es un invariante cross-step a vigilar en Etapa 7.
  - Días en `Pending` (sin address) en `targetDate` — se ignoran silenciosamente (no se resuelven, no entran en la orden). Quedan pendientes hasta que el paciente asigne dirección y el job vuelva a correr.
- **Idempotencia / reejecución**: correr el job dos veces para la misma fecha es seguro. Si ya existe `ProductionOrder` para `targetDate`, se llama `ReplaceLines` (reemplaza todas las líneas, actualiza `GeneratedAt`) en vez de crear un duplicado — enforced también por índice único en `ProductionOrders.DeliveryDate`.
- **Test coverage**: `ResolveDailyCatalogCommandTests`, `OrderConsolidationServiceTests`.

### C.2 — Consolidación de orden de producción (`OrderConsolidationService`, domain service puro)

- **Invocado por**: C.1, nunca directamente desde fuera.
- **Comportamiento**: dado un set de calendarios + fecha + orden existente (o `null`), filtra días `ReadyForProduction` que matcheen la fecha, construye una `ProductionOrderLine` por día (con `PatientId`, `Address`, `PlanRef`, `Hour`), y crea o reemplaza las líneas de la orden.
- **Edge case de diseño**: `Coordinates` vive tanto embebido en `DeliveryAddress` como duplicado como campo top-level en `ProductionOrderLine` (requisito explícito de `CLAUDE.md` §1 sobre la forma del entity). Son instancias VO distintas construidas a partir del mismo valor — nunca la misma referencia de objeto (ver nota de implementación: EF Core no permite que la misma instancia de un owned type esté en dos navigations distintas del mismo entity).

---

## D. Resultado de entrega (cierre de ciclo)

### D.1 — Aplicar resultado de entrega (`ApplyDeliveryResultCommand`)

- **Actor**: MS5, exclusivamente (`PATCH /api/v1/calendars/days/{dayId}/delivery-result`).
- **Input**: `dayId`, `result` (`Delivered`|`Failed`), `photoUrl?`, `signatureUrl?`.
- **Precondición**: día en `ReadyForProduction` o `InProduction`.
- **Happy path**: día pasa a `Delivered` o `Failed` según `result`. Si viene `photoUrl` o `signatureUrl`, se construye `DeliveryEvidence` (timestamp = `IClock.Now`) y se guarda en el día.
- **Escenarios de error**:
  - `dayId` no existe (búsqueda es por `dayId` solo — el endpoint no recibe `calendarId` según contrato) → `NotFoundException` → **404**.
  - Día en estado distinto de `ReadyForProduction`/`InProduction` **y** el resultado pedido difiere del estado actual → `InvalidDeliveryDayTransitionException` → **422**. Ejemplo: pedir `Delivered` sobre un día `Pending` o `Cancelled`.
- **Idempotencia — regla central de este flujo**: llamar dos veces con el **mismo** `result` sobre un día que ya está en ese estado destino es un **no-op silencioso** — devuelve el estado actual sin lanzar excepción, no vuelve a tocar `Evidence`. Esto es lo que garantiza que reintentos de MS5 (timeouts, reentregas del mismo evento) no rompan el estado. Si se pide un `result` **distinto** al ya aplicado (ej. día ya `Delivered`, se pide `Failed`) → `InvalidDeliveryDayTransitionException` (no es un cambio de resultado válido, es terminal).
- **Nunca se llega a `Delivered`/`Failed` por otro camino** — invariante de dominio explícito en `CLAUDE.md`: solo este endpoint puede hacer esa transición.
- **Test coverage**: `ApplyDeliveryResultCommandTests` (incluye caso explícito de doble llamada idéntica).

---

## E. Consultas

### E.1 — Obtener calendario completo (`GetCalendarQuery`)

- **Actor**: Frontend / interno (`GET /api/v1/calendars/{id}`).
- **Happy path**: devuelve el calendario completo con todos sus días, sin filtrar por estado.
- **Escenarios de error**: `calendarId` no existe → `NotFoundException` → **404**.
- **Test coverage**: `GetCalendarQueryTests`.

### E.2 — Orden de producción activa de mañana (`GetActiveTomorrowQuery`)

- **Actor**: MS3 (`GET /api/v1/calendars/active-tomorrow`), doble propósito: consulta auxiliar **y** mecanismo por el cual MS3 obtiene la orden de producción diaria (sustituye el evento async `ProductionOrderRequired`).
- **Input**: `date` opcional (default: mañana relativo a `IClock.Now`).
- **Precondición**: asume que C.1 (`ResolveDailyCatalogCommand`) ya corrió para esa fecha. **Este query nunca dispara resolución on-demand.**
- **Happy path**: lee el `ProductionOrder` ya consolidado para la fecha, devuelve sus líneas mapeadas a `{ packId, patientId, planId, hour, address, lat, long }`. `packId` se mapea a `DeliveryDayId` (no existe un concepto de "pack" separado en el dominio — es la unidad de entrega identificable más cercana, decisión de diseño documentada en `GetActiveTomorrowQuery.cs`).
- **Caso sin datos**: si no existe `ProductionOrder` para la fecha (job no corrió aún, o no hay días elegibles), devuelve **lista vacía**, no error.
- **Garantía de contenido**: solo puede devolver días que estuvieron `ReadyForProduction` al momento en que corrió la consolidación — nunca días `Pending`/`Scheduled`/`Cancelled`.
- **Test coverage**: `GetActiveTomorrowQueryTests`.

---

## Reglas globales (aplican a múltiples flujos)

### Regla de bloqueo de 48h (`DeliveryDay.EnsureOutsideLockWindow`)

Aplica a: B.2 (reschedule), B.3 (cancel). No aplica a B.1 (set address), C.1, D.1.

- Fórmula: `deliveryInstant - now < 48h` → bloqueado (`DeliveryWindowLockedException`).
- `deliveryInstant` se construye como medianoche UTC de la fecha del día (`DeliveryDate.Value.ToDateTime(TimeOnly.MinValue)`, offset cero) — no usa `DeliveryHour`.
- Límite exacto: a **48h00m00s000ms** exactas → permitido. A **47h59m59.999s** → bloqueado. Verificado en `DeliveryDayTests` con `AddHours(-48)` (permitido) vs `AddHours(-48).AddTicks(1)` (bloqueado).

### Transiciones de estado válidas (`DayStatus`)

```
Pending ──SetAddress──> Scheduled ──MarkReadyForProduction──> ReadyForProduction ──ApplyDeliveryResult──> Delivered
   │                        │                                        │                                       │
   │                        │                                        └──ApplyDeliveryResult──> Failed         │
   │                        │                                                                                 │
   ├──Reschedule (fuera 48h, self-loop)                                                                       │
   ├──Cancel (fuera 48h)──> Cancelled  <──Cancel (fuera 48h)── Scheduled                                       │
   └──Cancel (fuera 48h)──> Cancelled                                                                          │
                                          ReadyForProduction ──Cancel (fuera 48h)──> Cancelled                 │
                                          InProduction ──ApplyDeliveryResult──> Delivered/Failed (idempotente) ┘
```

Cualquier transición no listada arriba lanza `InvalidDeliveryDayTransitionException`. `InProduction` no tiene un comando propio en Etapa 3 (no hay flujo que lo setee explícitamente todavía — MS4 no expone un paso de "iniciar producción"; queda como estado reservado del enum, alcanzable solo si se decide exponerlo en una etapa futura).

### Idempotencia — resumen cross-flow

| Flujo | Mecanismo de idempotencia |
|---|---|
| A.1 CreateSchedule | Lookup previo por `ContractId` + índice único en DB |
| C.1 ResolveDailyCatalog | `ReplaceLines` sobre orden existente + índice único en `DeliveryDate` |
| D.1 ApplyDeliveryResult | No-op si `Status` ya es el `targetStatus` pedido |

---

## Fuera de alcance de estos flujos (documentado, no implementado)

- Autenticación/autorización — ningún flujo valida quién llama (decisión confirmada: sin auth en esta etapa).
- Transición explícita a `InProduction`.
- Reintentos/outbox para `ICatalogPort` — el mock siempre responde determinísticamente, sin fallos simulados.
- Disparo automático (cron/`BackgroundService`) de C.1 — pendiente de decisión en Etapa 5 (endpoint manual vs. `BackgroundService`).
