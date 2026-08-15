using delivery_schedule_management.Application.Calendars.Commands.SetDeliveryAddress;

namespace delivery_schedule_management.Api.Endpoints;

public partial class Calendars
{
    public static async Task<IResult> SetAddress(ISender sender, int id, SetAddressRequest request)
    {
        var result = await sender.Send(new SetDeliveryAddressCommand(
            id, request.DayId, request.Street, request.City, request.Latitude, request.Longitude, request.References, request.Notes));

        return Results.Ok(result);
    }
}

public record SetAddressRequest(int DayId, string Street, string City, double Latitude, double Longitude, string? References, string? Notes);
