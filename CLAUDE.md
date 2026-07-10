# MS4 — Delivery Calendar — Plan de Implementación

Stack: **.NET Aspire + Clean Architecture** · Persistencia: **Postgres** (orquestado por Aspire, EF Core) · Responsable: Franklin Andre Romero Padilla

Este documento es la base para que un agente de codificación ejecute la implementación por etapas. Cada etapa tiene entradas, tareas y un "definition of done" verificable (compila + corre + tests).

---

## 0. Contratos de entrada (fijos, no se re-discuten en código)

### Endpoints expuestos por MS4

| # | Endpoint | Método | Cliente | Input | Output |
|---|---|---|---|---|---|
| 1 | `/api/v1/schedule` | POST | MS3 | contractId, patientId, planId, duration(15\|30), startDate | calendarId, days[] (vacíos, status=PENDING) |
| 2 | `/api/v1/calendars/active-tomorrow` | GET | MS3 | (ninguno / date opcional) | ProductionOrder consolidada: [{ packId, patientId, planId, hour, address, lat, long }] |
| 3 | `/api/v1/calendars/{id}/dates` | PUT | Frontend (paciente) | dayId, newDate | dayId, status (aplica regla 48h) |
| 4 | `/api/v1/calendars/{id}/addresses` | POST | Frontend (paciente) | dayId (o calendarId), address, lat, long | dayId, address |
| 5 | `/api/v1/calendars/{id}/cancel-day` | POST | Frontend (paciente) | dayId | dayId, status=CANCELLED |
| 6 | `/api/v1/calendars/{id}` | GET | Frontend / interno | calendarId | calendario completo con días |
| 7 | `/api/v1/calendars/days/{dayId}/delivery-result` | PATCH | MS5 | dayId, result(DELIVERED\|FAILED), evidence | dayId, status |

Nota: el endpoint #2 sirve doble propósito según la tabla del usuario — consulta auxiliar de MS3 **y** el mecanismo por el cual MS3 genera la orden de producción diaria (sustituye el evento async `ProductionOrderRequired` documentado en la fase de arquitectura). MS4 debe tener resuelto el día de mañana (recipe/portions vía MS2) **antes** de que llegue esta llamada — no resolver on-demand dentro del request.

### Dependencia saliente de MS4

- `ICatalogPort` → `GET /catalog/plans/{id}/day/{date}` en MS2 (síncrono). En esta etapa: **mockeado** (adapter con fixture o HTTP a un stub).

---

## 1. Modelo de dominio (reconciliado)

**Aggregate: `DeliveryCalendar` (root)**
- `Id`, `PatientId`, `ContractId`, `PlanId` (ref MS2), `Duration` (15|30), `StartDate`, `Status`
- `Days: List<DeliveryDay>`

**Entity: `DeliveryDay`** (dentro de `DeliveryCalendar`)
- `Id`, `Date`, `Status` (`Pending`, `Scheduled`, `ReadyForProduction`, `InProduction`, `Delivered`, `Failed`, `Cancelled`)
- `Address: DeliveryAddress?`
- `DeliveryHour: TimeOnly?`
- `RecipeRef`, `Portions` (resueltos por job diario vía `ICatalogPort`, cacheados)
- `DeliveryEvidence?` (foto/firma/timestamp ref, al cerrar ciclo)

**Aggregate: `ProductionOrder` (root)** — snapshot consolidado del día siguiente
- `Id`, `ProductionDate` (D-1), `DeliveryDate` (D)
- `Lines: List<ProductionOrderLine>`
- `GeneratedAt`, `Status`

**Entity: `ProductionOrderLine`**
- `DeliveryDayId`, `PatientId`, `Address`, `Coordinates`, `PlanRef`, `Hour`

**Value Objects**
- `DeliveryAddress` (street, city, references, notes)
- `Coordinates` (lat, long)
- `DeliveryDate`
- `DayStatus` (enum arriba)
- `PlanDuration` (15|30)
- `ContractReference`, `PlanReference`, `RecipeReference`
- `PortionQuantity`

**Domain Services**
- `OrderConsolidationService`: dado un `DeliveryCalendar[]` con `DeliveryDay` cuya fecha = mañana y status `ReadyForProduction`, construye/actualiza el `ProductionOrder` del día.
- `DailyCatalogResolutionService`: job diario que, para cada `DeliveryDay` de mañana en status `Scheduled`, llama `ICatalogPort` y setea `RecipeRef`/`Portions`, pasando el día a `ReadyForProduction`.

**Reglas de negocio invariantes**
- No se puede modificar fecha ni cancelar un `DeliveryDay` si `Date - Now < 48h`.
- `POST /schedule` es idempotente por `contractId` (no debe crear un segundo calendario para el mismo contrato).
- `DeliveryDay` solo pasa a `Delivered`/`Failed` vía el endpoint de MS5, nunca manualmente.

**Outbound port**
- `ICatalogPort.GetDayResolution(planId, date) → (RecipeRef, Portions)`

---

## 2. Etapas de ejecución (para el agente)

### Etapa 1 — Scaffolding de solución (Aspire + Clean Architecture)
- Crear solución `.NET Aspire`: `MS4.AppHost`, `MS4.ServiceDefaults`, `MS4.Api`, `MS4.Application`, `MS4.Domain`, `MS4.Infrastructure`.
- `MS4.Domain`: sin dependencias externas (POCO puro, no EF, no ASP.NET).
- Referencias: `Api → Application → Domain`, `Infrastructure → Application/Domain`, `Api → Infrastructure` (composition root), `AppHost → Api` + recurso Postgres.
- **DoD**: `dotnet build` compila toda la solución vacía, `dotnet run` en AppHost levanta Postgres + Api sin errores.

### Etapa 2 — Modelo de dominio puro
- Implementar aggregates, entities, VOs y domain services de la sección 1 en `MS4.Domain`.
- VOs como `record` inmutables con validación en el constructor (ej. `Coordinates` valida rangos lat/long).
- Invariantes de negocio (regla 48h, transiciones de estado válidas) como métodos del aggregate (`DeliveryDay.Cancel(now)`, `DeliveryDay.Reschedule(newDate, now)`), nunca en capa externa.
- **DoD**: tests unitarios de dominio (xUnit) cubriendo: regla 48h (caso límite exacto), transiciones de estado inválidas lanzan excepción de dominio, creación de calendario vacío genera N días correctos según duration.

### Etapa 3 — Application layer (casos de uso)
- Patrón: Commands/Queries + Handlers (MediatR o manual, definir).
- Casos de uso mínimos:
  - `CreateScheduleCommand` (endpoint 1)
  - `GetActiveTomorrowQuery` (endpoint 2)
  - `RescheduleDeliveryDayCommand` (endpoint 3)
  - `SetDeliveryAddressCommand` (endpoint 4)
  - `CancelDeliveryDayCommand` (endpoint 5)
  - `GetCalendarQuery` (endpoint 6)
  - `ApplyDeliveryResultCommand` (endpoint 7)
  - `ResolveDailyCatalogJob` (job interno, no expuesto)
- Puertos definidos aquí: `ICalendarRepository`, `IProductionOrderRepository`, `ICatalogPort`, `IClock` (para testear regla 48h sin depender de `DateTime.Now`).
- **DoD**: handlers testeados con repos in-memory fake, sin infraestructura real.

### Etapa 4 — Infrastructure
- EF Core + Postgres: `DbContext`, configuraciones de entidades (mapeo de VOs como owned types), migraciones iniciales.
- Implementación de `ICalendarRepository`, `IProductionOrderRepository`.
- `ICatalogPort`: adapter mockeado (HttpClient contra un stub local o fixture estático — **no** contra MS2 real todavía).
- Background job `ResolveDailyCatalogJob` como `IHostedService`/`BackgroundService` con horario fijo (ej. cron diario) o trigger manual para testing.
- **DoD**: migración aplica sobre Postgres real (vía Aspire), repos persisten y recuperan correctamente contra DB real (test de integración con Testcontainers o Aspire testing).

### Etapa 5 — API layer
- Minimal APIs o Controllers (definir preferencia), un endpoint por caso de uso de la sección 0.
- Validación de input (FluentValidation o data annotations).
- Manejo de errores de dominio → status codes HTTP correctos (409 para violación de regla 48h, 404 para calendario/día inexistente, 422 para transición inválida).
- **DoD**: colección Postman/`.http` file con los 7 endpoints, cada uno probado manualmente contra datos seed.

### Etapa 6 — Datos mock / seed
- Seed de datos de prueba: 2-3 pacientes, calendarios en distintos estados, para poder probar `active-tomorrow` sin depender de MS1/MS3 reales.
- Fixture estático para `ICatalogPort` (respuestas hardcodeadas de recipe/portions).
- **DoD**: `dotnet run` en AppHost deja el sistema con datos de prueba listos para demo.

### Etapa 7 — Tests de aceptación
Verificar contra criterios ya definidos:
- Calendario no se crea duplicado para el mismo contrato.
- Cancelación rechazada si faltan <48h.
- Reprogramación de fecha rechazada si faltan <48h.
- `active-tomorrow` devuelve solo días en `ReadyForProduction` para la fecha de mañana.
- `delivery-result` actualiza `DayStatus` correctamente y es idempotente (llamar dos veces con el mismo resultado no rompe estado).
- **DoD**: suite de tests de aceptación (integración, contra Postgres real vía Aspire) en verde.

---

## 3. Decisiones abiertas para el agente (resolver antes de codear, no asumir en silencio)

1. MediatR vs handlers manuales para Application layer.
2. Minimal APIs vs Controllers para MS4.Api.
3. Mecanismo del job diario: `BackgroundService` con `Timer`/cron vs endpoint interno disparado manualmente en esta etapa (más simple para demo).
4. Cómo se autentica el frontend/paciente contra estos endpoints en esta etapa (¿sin auth por ahora, dado que es solo mock?).

---

## 4. Fuera de alcance en esta etapa (ya acordado en fase de arquitectura)
- Broker de mensajería real.
- Autenticación servicio-a-servicio.
- Outbox pattern.
- Versionado de schema de eventos (no aplica: esta etapa usa REST síncrono, no eventos).

---

## 5. Estado actual de la implementación (para agentes — leer antes de tocar código)

Ver también [`README.md`](./README.md) (cómo correr/testear) y [`FLOWS.md`](./FLOWS.md) (todo caso de uso, comportamiento esperado, escenarios de error).

**Completado**: Etapa 0 (cleanup del template) → Etapa 5 (API layer). Pendiente: Etapa 6 (seed data), Etapa 7 (tests de aceptación end-to-end).

### Decisiones de §3 ya resueltas
1. MediatR (no handlers manuales) — ya wireado en `Application/DependencyInjection.cs`.
2. Minimal APIs vía patrón `IEndpointGroup` (`src/Web/Infrastructure/`), no Controllers.
3. Job diario: **endpoint de trigger manual** (`POST /api/v1/calendars/resolve-daily-catalog`), no `BackgroundService`. No es parte del contrato público de §0 — es ops/demo.
4. Sin auth en esta etapa — confirmado, Identity removido por completo en Etapa 0.

### Gotchas técnicos ya resueltos (no los repitas)
- **EF Core owned-type constructor binding no soporta navegación anidada en el constructor.** `DeliveryAddress` anida `Coordinates` como parámetro de ctor — EF no puede bindear eso vía constructor binding. Solución aplicada: ctor privado sin parámetros + setters privados en `DeliveryAddress` (mismo patrón que las entidades). Si agregás otro VO que anide otro VO owned, vas a pisar el mismo problema.
- **Misma instancia de un owned type en dos navigations del mismo entity rompe el change tracker de EF.** `ProductionOrderLine` tiene `Coordinates` tanto embebido en `Address` como duplicado top-level (requisito explícito de §1). Nunca reuses la referencia — siempre `new Coordinates(...)`, no `address.Coordinates`.
- **`dotnet ef` tool debe matchear la versión de `Microsoft.EntityFrameworkCore.Design`** en `Directory.Packages.props`, si no tira `MSB4057`. Ver README §Database migrations.
- **`ApplicationDbContextInitialiser` usa `MigrateAsync()`**, no `EnsureCreated` — ya hay migraciones reales committeadas (`src/Infrastructure/Data/Migrations/`).
- **Enums en JSON**: configurado `JsonStringEnumConverter` global (`Web/DependencyInjection.cs`) — todo enum (`DayStatus`, `DeliveryResult`, etc.) serializa/deserializa como string, no int.
- **`AddProblemDetails()` es obligatorio** junto con `AddExceptionHandler<ProblemDetailsExceptionHandler>()` — sin eso, cualquier excepción no reconocida por el handler custom rompe con un meta-exception feo en vez de devolver un 500 limpio.

### Mapeo de excepciones de dominio → HTTP (ya implementado en `ProblemDetailsExceptionHandler`)
`ValidationException`→400, `NotFoundException`→404, `DeliveryWindowLockedException`→409, `InvalidDeliveryDayTransitionException`→422.

### Dónde está cada cosa
- Repos: `ICalendarRepository`/`IProductionOrderRepository` (puertos en Application) → `CalendarRepository`/`ProductionOrderRepository` (impl en `Infrastructure/Data/Repositories`), toman `ApplicationDbContext` concreto directo, no `IApplicationDbContext` (esa interfaz es solo `SaveChangesAsync`, el unit-of-work).
- `ICatalogPort` mockeado: `Infrastructure/Catalog/MockCatalogPort.cs` — fixture determinística por `planId`+`date`, sin HTTP real.
- Endpoints: `src/Web/Endpoints/Calendars.cs`, un solo `IEndpointGroup` con `RoutePrefix = "/api/v1"` cubriendo los 7 contratos de §0 + el trigger manual.
- Colección de pruebas manuales: `src/Web/Web.http`.