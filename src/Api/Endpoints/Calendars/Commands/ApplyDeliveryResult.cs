using delivery_schedule_management.Application.Calendars.Commands.ApplyDeliveryResult;
using delivery_schedule_management.Domain.Enums;

namespace delivery_schedule_management.Api.Endpoints;

public partial class Calendars
{
    public static async Task<IResult> ApplyDeliveryResult(ISender sender, int dayId, ApplyDeliveryResultRequest request)
    {
        var result = await sender.Send(new ApplyDeliveryResultCommand(dayId, request.Result, request.PhotoUrl, request.SignatureUrl));

        return Results.Ok(result);
    }
}

public record ApplyDeliveryResultRequest(DeliveryResult Result, string? PhotoUrl, string? SignatureUrl);
