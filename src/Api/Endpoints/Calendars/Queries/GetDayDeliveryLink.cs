using delivery_schedule_management.Application.Calendars.Queries.GetDayDeliveryLink;

namespace delivery_schedule_management.Api.Endpoints;

public partial class Calendars
{
    public static async Task<IResult> GetDayDeliveryLink(ISender sender, int dayId)
    {
        var result = await sender.Send(new GetDayDeliveryLinkQuery(dayId));

        return Results.Ok(result);
    }
}
