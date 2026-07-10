using delivery_schedule_management.Application.Common.Interfaces;

namespace delivery_schedule_management.Application.Calendars.Queries.GetActiveTomorrow;

public record GetActiveTomorrowQuery(DateOnly? Date) : IRequest<List<ActiveTomorrowLineDto>>;

public record ActiveTomorrowLineDto(int PackId, Guid PatientId, Guid PlanId, TimeOnly? Hour, string Address, double Latitude, double Longitude);

public class GetActiveTomorrowQueryHandler(IProductionOrderRepository productionOrderRepository, IClock clock)
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

        return order.Lines
            .Select(line => new ActiveTomorrowLineDto(
                line.DeliveryDayId,
                line.PatientId,
                line.PlanRef.Value,
                line.Hour,
                $"{line.Address.Street}, {line.Address.City}",
                line.Coordinates.Latitude,
                line.Coordinates.Longitude))
            .ToList();
    }
}
