using delivery_schedule_management.Application.Calendars.Commands.ResolveDailyCatalog;

namespace delivery_schedule_management.Api.Endpoints;

public partial class Calendars
{
    public static async Task<IResult> ResolveDailyCatalog(ISender sender, DateOnly? date)
    {
        await sender.Send(new ResolveDailyCatalogCommand(date));

        return Results.NoContent();
    }
}
