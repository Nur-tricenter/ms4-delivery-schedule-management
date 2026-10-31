using delivery_schedule_management.Application.Calendars.Commands.ApplyDeliveryResult;
using delivery_schedule_management.Application.Calendars.Commands.CancelDeliveryDay;
using delivery_schedule_management.Application.Calendars.Commands.CreateSchedule;
using delivery_schedule_management.Application.Calendars.Commands.RescheduleDeliveryDay;
using delivery_schedule_management.Application.Calendars.Commands.SetDeliveryAddress;
using delivery_schedule_management.Application.Calendars.Common;
using delivery_schedule_management.Application.Calendars.Queries.GetActiveTomorrow;
using delivery_schedule_management.Application.Calendars.Queries.GetDayDeliveryLink;
using Nur.Tricenter.Access.DeliveryCalendar;

namespace delivery_schedule_management.Api.Endpoints;

/// <summary>
/// Route group registration for the calendar endpoints. Each handler lives in its own file under
/// this folder (one file per endpoint); this file only wires them into the route table.
/// </summary>
public partial class Calendars : IEndpointGroup
{
    public static string? RoutePrefix => "/api/v1";

    public static void Map(RouteGroupBuilder groupBuilder)
    {
        // Every endpoint requires a bearer token from nur-tricenter-identity carrying the scope named
        // in its RequireAuthorization call (kernel: Nur.Tricenter.Access.DeliveryCalendar).
        groupBuilder.ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        groupBuilder.MapPost(CreateSchedule, "schedule")
            .WithSummary("[Step 1 · MS3] Create delivery calendar")
            .WithDescription(
                "Caller: MS3 (scheduling engine). " +
                "Starting point for all flows. Creates a new delivery calendar for a patient and returns " +
                "a calendarId plus the full list of empty days (all Pending). " +
                "Idempotent by contractId — a second call for the same contract returns 409.")
            .Produces<CreateScheduleResult>()
            .ProducesProblem(409)
            .RequireAuthorization(DeliveryCalendarPermission.CalendarCreate);

        groupBuilder.MapGet(GetActiveTomorrow, "calendars/active-tomorrow")
            .WithSummary("[Step 5 · MS3] Get tomorrow's production order")
            .WithDescription(
                "Caller: MS3 (scheduling engine). " +
                "Returns all delivery days in ReadyForProduction status for tomorrow's date. " +
                "MS3 uses this to generate the daily production order. " +
                "Step 4 (resolve-daily-catalog) must run first — days must already be in ReadyForProduction or they won't appear here. " +
                "Optional 'date' query param to query a different date.")
            .Produces<List<ActiveTomorrowLineDto>>()
            .RequireAuthorization(DeliveryCalendarPermission.ProductionOrderRead);

        groupBuilder.MapGet(GetCalendar, "calendars/{id}")
            .WithSummary("[Step 2 · Frontend] Inspect the calendar")
            .WithDescription(
                "Caller: Frontend (patient app) or internal. " +
                "Returns the full calendar with all delivery days and their current statuses. " +
                "Use the calendarId from Step 1. Copy a dayId from the days list for Steps 3–8.")
            .Produces<CalendarVm>()
            .ProducesProblem(404)
            .RequireAuthorization(DeliveryCalendarPermission.CalendarRead);

        // Lookups for following a case end to end (cross-service gap X-17). Read-only, same scope as
        // GET calendars/{id}. The literal segments keep them apart from the calendars/{id} route.
        groupBuilder.MapGet(GetCalendarByContract, "calendars/by-contract/{contractId:guid}")
            .WithSummary("[Lookup] Find the calendar of an MS3 contract")
            .WithDescription(
                "Caller: Frontend / demo console. " +
                "Returns the calendar created for this contractId (at most one: POST /schedule is idempotent by contract). " +
                "404 if MS3 has not created it yet.")
            .Produces<CalendarVm>()
            .ProducesProblem(404)
            .RequireAuthorization(DeliveryCalendarPermission.CalendarRead);

        groupBuilder.MapGet(GetCalendarsByPatient, "calendars/by-patient/{patientId:guid}")
            .WithSummary("[Lookup] List the calendars of a patient")
            .WithDescription(
                "Caller: Frontend / demo console. " +
                "Returns every calendar of the patient, newest first, with their days. " +
                "An unknown patient returns an empty list.")
            .Produces<List<CalendarVm>>()
            .RequireAuthorization(DeliveryCalendarPermission.CalendarRead);

        groupBuilder.MapGet(GetDayDeliveryLink, "calendars/days/{dayId:int}/delivery-link")
            .WithSummary("[Lookup] Follow a delivery day to its MS5 delivery and route")
            .WithDescription(
                "Caller: Frontend / demo console. " +
                "Returns the day's status plus the MS5 deliveryId/routeId linked by the delivery-added-to-route event. " +
                "The delivery fields are null until MS5 routes the day's package. 404 if the day does not exist.")
            .Produces<DayDeliveryLinkVm>()
            .ProducesProblem(404)
            .RequireAuthorization(DeliveryCalendarPermission.CalendarRead);

        groupBuilder.MapPut(RescheduleDate, "calendars/{id}/dates")
            .WithSummary("[Step 6 · Frontend] Reschedule a delivery day")
            .WithDescription(
                "Caller: Frontend (patient app). " +
                "Change the date of a specific delivery day. " +
                "48h rule: returns 409 if the current day date is less than 48 hours away. " +
                "Day must be in Pending or Scheduled status; any other status returns 422.")
            .Produces<RescheduleDeliveryDayResult>()
            .ProducesProblem(404)
            .ProducesProblem(409)
            .ProducesProblem(422)
            .RequireAuthorization(DeliveryCalendarPermission.CalendarEdit);

        groupBuilder.MapPost(SetAddress, "calendars/{id}/addresses")
            .WithSummary("[Step 3 · Frontend] Set delivery address for a day")
            .WithDescription(
                "Caller: Frontend (patient app). " +
                "Assign a delivery address (street, city, lat/long) to a specific day. " +
                "Moves the day status from Pending → Scheduled. " +
                "A day must be Scheduled before the daily catalog job (Step 4) can resolve its recipe and portions.")
            .Produces<SetDeliveryAddressResult>()
            .ProducesProblem(404)
            .ProducesProblem(422)
            .RequireAuthorization(DeliveryCalendarPermission.CalendarEdit);

        groupBuilder.MapPost(CancelDay, "calendars/{id}/cancel-day")
            .WithSummary("[Step 7 · Frontend] Cancel a delivery day")
            .WithDescription(
                "Caller: Frontend (patient app). " +
                "Cancel a specific delivery day. " +
                "48h rule: returns 409 if the day date is less than 48 hours away. " +
                "Day moves to Cancelled status. Cannot cancel a day already in ReadyForProduction or later — returns 422.")
            .Produces<CancelDeliveryDayResult>()
            .ProducesProblem(404)
            .ProducesProblem(409)
            .ProducesProblem(422)
            .RequireAuthorization(DeliveryCalendarPermission.CalendarEdit);

        groupBuilder.MapPatch(ApplyDeliveryResult, "calendars/days/{dayId}/delivery-result")
            .WithSummary("[Step 8 · MS5] Record delivery result")
            .WithDescription(
                "Caller: MS5 (delivery agent app). " +
                "Called after a delivery attempt. Records DELIVERED or FAILED with optional " +
                "photo/signature evidence URLs. Day must be in ReadyForProduction or InProduction status; returns 422 otherwise. " +
                "Idempotent: calling twice with the same result is safe.")
            .Produces<ApplyDeliveryResultResult>()
            .ProducesProblem(404)
            .ProducesProblem(422)
            .RequireAuthorization(DeliveryCalendarPermission.DeliveryResultEdit);

        // Internal/ops endpoint — not part of the public contract in CLAUDE.md §0. Manual trigger
        // for the daily catalog resolution job, per the resolved open decision (CLAUDE.md §3.3):
        // no BackgroundService yet, simplest path for this stage.
        groupBuilder.MapPost(ResolveDailyCatalog, "calendars/resolve-daily-catalog")
            .WithSummary("[Step 4 · Internal] Run daily catalog resolution")
            .WithDescription(
                "Caller: internal/ops — not part of the public MS4 contract. " +
                "Resolves recipe and portions for all Scheduled days whose date is tomorrow (from the projection of MS2's meal plan compositions) and stages the ProductionOrderReady event, " +
                "moving them to ReadyForProduction. " +
                "Must run before calling active-tomorrow (Step 5) or the response will be empty. " +
                "Optional 'date' query param overrides which date is treated as 'tomorrow'.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(400)
            .RequireAuthorization(DeliveryCalendarPermission.CatalogResolutionManage);
    }
}
