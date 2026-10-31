using delivery_schedule_management.Application.Common.Interfaces;
using delivery_schedule_management.Domain.Entities;
using delivery_schedule_management.Domain.Enums;

namespace delivery_schedule_management.Application.Calendars.Queries.GetDayDeliveryLink;

/// <summary>
/// Follows a delivery day to the MS5 delivery and route that carry it (from <c>DeliveryAddedToRoute</c>).
/// 404 only when the day does not exist; a day MS5 has not routed yet answers 200 with null delivery fields.
/// </summary>
public record GetDayDeliveryLinkQuery(int DayId) : IRequest<DayDeliveryLinkVm>;

public record DayDeliveryLinkVm(
    int DayId,
    int CalendarId,
    DateOnly Date,
    DayStatus DayStatus,
    Guid? DeliveryId,
    Guid? RouteId,
    DateTimeOffset? LinkedAt);

public class GetDayDeliveryLinkQueryHandler(ICalendarRepository calendarRepository, IDeliveryRouteLinks routeLinks)
    : IRequestHandler<GetDayDeliveryLinkQuery, DayDeliveryLinkVm>
{
    public async Task<DayDeliveryLinkVm> Handle(GetDayDeliveryLinkQuery request, CancellationToken cancellationToken)
    {
        var calendar = await calendarRepository.GetByDeliveryDayIdAsync(request.DayId, cancellationToken)
            ?? throw new NotFoundException(request.DayId.ToString(), nameof(DeliveryDay));

        var day = calendar.Days.First(d => d.Id == request.DayId);
        var link = await routeLinks.FindLatestForDayAsync(request.DayId, cancellationToken);

        return new DayDeliveryLinkVm(
            day.Id, calendar.Id, day.Date.Value, day.Status, link?.DeliveryId, link?.RouteId, link?.LinkedAt);
    }
}
