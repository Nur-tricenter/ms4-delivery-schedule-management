using delivery_schedule_management.Application.Calendars.Queries.GetCalendar;

namespace delivery_schedule_management.Api.Endpoints;

public partial class Calendars
{
    public static async Task<IResult> GetCalendar(ISender sender, int id)
    {
        var result = await sender.Send(new GetCalendarQuery(id));

        return Results.Ok(result);
    }
}
