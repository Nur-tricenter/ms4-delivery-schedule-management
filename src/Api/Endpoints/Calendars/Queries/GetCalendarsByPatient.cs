using delivery_schedule_management.Application.Calendars.Queries.GetCalendarsByPatient;

namespace delivery_schedule_management.Api.Endpoints;

public partial class Calendars
{
    public static async Task<IResult> GetCalendarsByPatient(ISender sender, Guid patientId)
    {
        var result = await sender.Send(new GetCalendarsByPatientQuery(patientId));

        return Results.Ok(result);
    }
}
