using delivery_schedule_management.Application.Calendars.Common;
using delivery_schedule_management.Application.Common.Interfaces;
using delivery_schedule_management.Domain.Entities;
using delivery_schedule_management.Domain.ValueObjects;

namespace delivery_schedule_management.Application.Calendars.Commands.SetDeliveryAddress;

public record SetDeliveryAddressCommand(
    int CalendarId,
    int DayId,
    string Street,
    string City,
    double Latitude,
    double Longitude,
    string? References,
    string? Notes) : IRequest<SetDeliveryAddressResult>;

public record SetDeliveryAddressResult(int DayId, AddressDto Address);

public class SetDeliveryAddressCommandHandler(ICalendarRepository calendarRepository, IApplicationDbContext context)
    : IRequestHandler<SetDeliveryAddressCommand, SetDeliveryAddressResult>
{
    public async Task<SetDeliveryAddressResult> Handle(SetDeliveryAddressCommand request, CancellationToken cancellationToken)
    {
        var calendar = await calendarRepository.GetByIdAsync(request.CalendarId, cancellationToken)
            ?? throw new NotFoundException(request.CalendarId.ToString(), nameof(DeliveryCalendar));

        var day = calendar.Days.FirstOrDefault(d => d.Id == request.DayId)
            ?? throw new NotFoundException(request.DayId.ToString(), nameof(DeliveryDay));

        var address = new DeliveryAddress(
            request.Street,
            request.City,
            new Coordinates(request.Latitude, request.Longitude),
            request.References,
            request.Notes);

        day.SetAddress(address);

        await context.SaveChangesAsync(cancellationToken);

        return new SetDeliveryAddressResult(day.Id, address.ToDto());
    }
}
