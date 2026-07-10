using delivery_schedule_management.Application.Calendars.Commands.CancelDeliveryDay;

namespace delivery_schedule_management.Api.Endpoints;

public partial class Calendars
{
    public static async Task<IResult> CancelDay(ISender sender, int id, CancelDayRequest request)
    {
        var result = await sender.Send(new CancelDeliveryDayCommand(id, request.DayId));

        return Results.Ok(result);
    }
}

public record CancelDayRequest(int DayId);
