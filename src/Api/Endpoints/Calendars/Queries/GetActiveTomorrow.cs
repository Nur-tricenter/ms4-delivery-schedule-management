using delivery_schedule_management.Application.Calendars.Queries.GetActiveTomorrow;

namespace delivery_schedule_management.Api.Endpoints;

public partial class Calendars
{
    public static async Task<IResult> GetActiveTomorrow(ISender sender, DateOnly? date)
    {
        var result = await sender.Send(new GetActiveTomorrowQuery(date));

        return Results.Ok(result);
    }
}
