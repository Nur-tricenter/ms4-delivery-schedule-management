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
        groupBuilder.MapPost(CreateSchedule, "schedule");
        groupBuilder.MapGet(GetActiveTomorrow, "calendars/active-tomorrow");
        groupBuilder.MapGet(GetCalendar, "calendars/{id}");
        groupBuilder.MapPut(RescheduleDate, "calendars/{id}/dates");
        groupBuilder.MapPost(SetAddress, "calendars/{id}/addresses");
        groupBuilder.MapPost(CancelDay, "calendars/{id}/cancel-day");
        groupBuilder.MapPatch(ApplyDeliveryResult, "calendars/days/{dayId}/delivery-result");

        // Internal/ops endpoint — not part of the public contract in CLAUDE.md §0. Manual trigger
        // for the daily catalog resolution job, per the resolved open decision (CLAUDE.md §3.3):
        // no BackgroundService yet, simplest path for this stage.
        groupBuilder.MapPost(ResolveDailyCatalog, "calendars/resolve-daily-catalog");
    }
}
