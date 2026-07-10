using delivery_schedule_management.Application.Calendars.Commands.CreateSchedule;

namespace delivery_schedule_management.Api.Endpoints;

public partial class Calendars
{
    public static async Task<IResult> CreateSchedule(ISender sender, CreateScheduleRequest request)
    {
        var result = await sender.Send(new CreateScheduleCommand(
            request.ContractId, request.PatientId, request.PlanId, request.Duration, request.StartDate));

        return Results.Ok(result);
    }
}

public record CreateScheduleRequest(Guid ContractId, Guid PatientId, Guid PlanId, int Duration, DateOnly StartDate);
