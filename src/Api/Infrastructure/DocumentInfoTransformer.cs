using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace delivery_schedule_management.Api.Infrastructure;

internal sealed class DocumentInfoTransformer : IOpenApiDocumentTransformer
{
    public Task TransformAsync(OpenApiDocument document, OpenApiDocumentTransformerContext context, CancellationToken cancellationToken)
    {
        document.Info.Title = "MS4 — Delivery Calendar API";
        document.Info.Version = "v1";
        document.Info.Description = """
            Microservice responsible for creating and managing patient delivery calendars.

            Tracks each delivery day through its full lifecycle — from initial scheduling,
            address assignment, and daily catalog resolution, through production ordering
            and final delivery confirmation by the field team.

            ---

            ## Service dependencies

            | Direction | Caller / Dependency | Endpoints |
            |-----------|--------------------|----|
            | Inbound | **MS3** (scheduling engine) | `POST /schedule`, `GET /calendars/active-tomorrow` |
            | Inbound | **Frontend** (patient) | Set address, reschedule date, cancel day |
            | Inbound | **MS5** (delivery agent app) | `PATCH /calendars/days/{dayId}/delivery-result` |
            | Outbound | none over HTTP | `ICatalogPort` reads the projection of MS2's `MealPlanCompositionChanged` events (mocked standalone); `ProductionOrderReady` goes out as an event |

            ---

            ## Delivery day lifecycle

            ```
            Pending
              │
              ├─[SetAddress]──────────────────► Scheduled
              │                                     │
              │                           [ResolveDailyCatalog]
              │                                     │
              │                                     ▼
              │                           ReadyForProduction
              │                                     │
              │                           [MS5: delivery-result]
              │                                 ┌───┴───┐
              │                                 ▼       ▼
              │                           Delivered   Failed
              │
              └─[Cancel, outside 48h]──► Cancelled
                   (also valid from Scheduled and ReadyForProduction)
            ```

            ---

            ## Key business rules

            - **48-hour lock window**: a day cannot be rescheduled or cancelled if fewer than
              48 hours remain until its delivery date. Returns `409 Conflict`.
              Exact boundary: at exactly 48h00m00s → allowed; 47h59m59.999s → locked.

            - **Idempotency**:
              - `POST /schedule` with the same `contractId` returns the existing calendar — no duplicate.
              - `POST /resolve-daily-catalog` for the same date replaces the production order lines.
              - `PATCH /delivery-result` with the same result on an already-terminal day is a no-op.

            - **Catalog resolution is always pre-computed**: `GET /calendars/active-tomorrow` never
              triggers on-demand resolution. Run `POST /resolve-daily-catalog` first, then query.

            ---

            ## How to test in this UI

            Endpoints are numbered **[Step 1]** through **[Step 8]** under the Calendars tag.
            Follow that order to walk a calendar from creation to delivery close-out.

            See the **README** for full step-by-step request bodies and error scenarios.

            > **No authentication** at this stage — all endpoints are open.
            """;

        return Task.CompletedTask;
    }
}
