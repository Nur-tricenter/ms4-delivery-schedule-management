using delivery_schedule_management.Application.Calendars.Queries.GetCalendarByContract;

namespace delivery_schedule_management.Api.Endpoints;

public partial class Calendars
{
    public static async Task<IResult> GetCalendarByContract(ISender sender, Guid contractId)
    {
        var result = await sender.Send(new GetCalendarByContractQuery(contractId));

        return Results.Ok(result);
    }
}
