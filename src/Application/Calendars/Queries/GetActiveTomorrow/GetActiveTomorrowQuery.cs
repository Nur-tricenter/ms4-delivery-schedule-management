using delivery_schedule_management.Application.Calendars.Common;
using delivery_schedule_management.Application.Common.Interfaces;

namespace delivery_schedule_management.Application.Calendars.Queries.GetActiveTomorrow;

public record GetActiveTomorrowQuery(DateOnly? Date) : IRequest<List<ActiveTomorrowLineDto>>;

/// <summary>
/// One pack MS3 has to produce for the target date.
/// </summary>
/// <param name="Menu">
/// The day's menu snapshot taken by resolve-daily-catalog (meal times, recipes, rations). It lets MS3
/// build its production order without asking MS2 for the plan composition again. Added at the end of
/// the record so existing JSON consumers keep working.
/// </param>
public record ActiveTomorrowLineDto(
    int PackId,
    Guid PatientId,
    Guid PlanId,
    TimeOnly? Hour,
    string Address,
    double Latitude,
    double Longitude,
    IReadOnlyList<MenuItemDto> Menu);

public class GetActiveTomorrowQueryHandler(
    IProductionOrderRepository productionOrderRepository,
    ICalendarRepository calendarRepository,
    IClock clock)
    : IRequestHandler<GetActiveTomorrowQuery, List<ActiveTomorrowLineDto>>
{
    public async Task<List<ActiveTomorrowLineDto>> Handle(GetActiveTomorrowQuery request, CancellationToken cancellationToken)
    {
        var targetDate = request.Date ?? DateOnly.FromDateTime(clock.Now.Date).AddDays(1);

        var order = await productionOrderRepository.GetByDeliveryDateAsync(targetDate, cancellationToken);

        if (order is null)
        {
            return [];
        }

        var calendars = await calendarRepository.GetActiveForDeliveryDateAsync(targetDate, cancellationToken);
        var menusByDay = calendars
            .SelectMany(calendar => calendar.Days)
            .Where(day => day.Date.Value == targetDate)
            .ToDictionary(day => day.Id, day => day.ToMenuDto());

        return order.Lines
            .Select(line => new ActiveTomorrowLineDto(
                line.DeliveryDayId,
                line.PatientId,
                line.PlanRef.Value,
                line.Hour,
                $"{line.Address.Street}, {line.Address.City}",
                line.Coordinates.Latitude,
                line.Coordinates.Longitude,
                menusByDay.GetValueOrDefault(line.DeliveryDayId) ?? []))
            .ToList();
    }
}
