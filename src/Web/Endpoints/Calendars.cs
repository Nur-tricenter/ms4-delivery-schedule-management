using delivery_schedule_management.Application.Calendars.Commands.ApplyDeliveryResult;
using delivery_schedule_management.Application.Calendars.Commands.CancelDeliveryDay;
using delivery_schedule_management.Application.Calendars.Commands.CreateSchedule;
using delivery_schedule_management.Application.Calendars.Commands.RescheduleDeliveryDay;
using delivery_schedule_management.Application.Calendars.Commands.ResolveDailyCatalog;
using delivery_schedule_management.Application.Calendars.Commands.SetDeliveryAddress;
using delivery_schedule_management.Application.Calendars.Queries.GetActiveTomorrow;
using delivery_schedule_management.Application.Calendars.Queries.GetCalendar;
using delivery_schedule_management.Domain.Enums;
using delivery_schedule_management.Web.Infrastructure;

namespace delivery_schedule_management.Web.Endpoints;

public class Calendars : IEndpointGroup
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

    public static async Task<IResult> CreateSchedule(ISender sender, CreateScheduleRequest request)
    {
        var result = await sender.Send(new CreateScheduleCommand(
            request.ContractId, request.PatientId, request.PlanId, request.Duration, request.StartDate));

        return Results.Ok(result);
    }

    public static async Task<IResult> GetActiveTomorrow(ISender sender, DateOnly? date)
    {
        var result = await sender.Send(new GetActiveTomorrowQuery(date));

        return Results.Ok(result);
    }

    public static async Task<IResult> GetCalendar(ISender sender, int id)
    {
        var result = await sender.Send(new GetCalendarQuery(id));

        return Results.Ok(result);
    }

    public static async Task<IResult> RescheduleDate(ISender sender, int id, RescheduleDateRequest request)
    {
        var result = await sender.Send(new RescheduleDeliveryDayCommand(id, request.DayId, request.NewDate));

        return Results.Ok(result);
    }

    public static async Task<IResult> SetAddress(ISender sender, int id, SetAddressRequest request)
    {
        var result = await sender.Send(new SetDeliveryAddressCommand(
            id, request.DayId, request.Street, request.City, request.Latitude, request.Longitude, request.References, request.Notes));

        return Results.Ok(result);
    }

    public static async Task<IResult> CancelDay(ISender sender, int id, CancelDayRequest request)
    {
        var result = await sender.Send(new CancelDeliveryDayCommand(id, request.DayId));

        return Results.Ok(result);
    }

    public static async Task<IResult> ApplyDeliveryResult(ISender sender, int dayId, ApplyDeliveryResultRequest request)
    {
        var result = await sender.Send(new ApplyDeliveryResultCommand(dayId, request.Result, request.PhotoUrl, request.SignatureUrl));

        return Results.Ok(result);
    }

    public static async Task<IResult> ResolveDailyCatalog(ISender sender, DateOnly? date)
    {
        await sender.Send(new ResolveDailyCatalogCommand(date));

        return Results.NoContent();
    }
}

public record CreateScheduleRequest(Guid ContractId, Guid PatientId, Guid PlanId, int Duration, DateOnly StartDate);

public record RescheduleDateRequest(int DayId, DateOnly NewDate);

public record SetAddressRequest(int DayId, string Street, string City, double Latitude, double Longitude, string? References, string? Notes);

public record CancelDayRequest(int DayId);

public record ApplyDeliveryResultRequest(DeliveryResult Result, string? PhotoUrl, string? SignatureUrl);
