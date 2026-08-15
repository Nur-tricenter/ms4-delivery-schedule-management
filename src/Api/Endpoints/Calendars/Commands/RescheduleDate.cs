using delivery_schedule_management.Application.Calendars.Commands.RescheduleDeliveryDay;

namespace delivery_schedule_management.Api.Endpoints;

public partial class Calendars
{
    public static async Task<IResult> RescheduleDate(ISender sender, int id, RescheduleDateRequest request)
    {
        var result = await sender.Send(new RescheduleDeliveryDayCommand(id, request.DayId, request.NewDate));

        return Results.Ok(result);
    }
}

public record RescheduleDateRequest(int DayId, DateOnly NewDate);
