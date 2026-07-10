using delivery_schedule_management.Application.Common.Interfaces;
using delivery_schedule_management.Domain.Enums;
using delivery_schedule_management.Domain.Services;
using delivery_schedule_management.Domain.ValueObjects;

namespace delivery_schedule_management.Application.Calendars.Commands.ResolveDailyCatalog;

/// <summary>
/// Internal job (not exposed via API). For every <see cref="DayStatus.Scheduled"/> day landing on
/// the target date, resolves recipe/portions via <see cref="ICatalogPort"/> and marks it
/// <see cref="DayStatus.ReadyForProduction"/>, then (re)builds that date's <c>ProductionOrder</c>
/// via <see cref="OrderConsolidationService"/>. Must run before MS3 calls
/// <c>GET /calendars/active-tomorrow</c> for the same date.
/// </summary>
public record ResolveDailyCatalogCommand(DateOnly? TargetDate) : IRequest;

public class ResolveDailyCatalogCommandHandler(
    ICalendarRepository calendarRepository,
    IProductionOrderRepository productionOrderRepository,
    ICatalogPort catalogPort,
    IApplicationDbContext context,
    IClock clock) : IRequestHandler<ResolveDailyCatalogCommand>
{
    public async Task Handle(ResolveDailyCatalogCommand request, CancellationToken cancellationToken)
    {
        var targetDate = request.TargetDate ?? DateOnly.FromDateTime(clock.Now.Date).AddDays(1);
        var deliveryDate = new DeliveryDate(targetDate);

        var calendars = await calendarRepository.GetActiveForDeliveryDateAsync(targetDate, cancellationToken);

        foreach (var calendar in calendars)
        {
            var day = calendar.Days.SingleOrDefault(d => d.Date == deliveryDate && d.Status == DayStatus.Scheduled);
            if (day is null)
            {
                continue;
            }

            var resolution = await catalogPort.GetDayResolutionAsync(calendar.PlanId.Value, targetDate, cancellationToken);

            day.MarkReadyForProduction(new RecipeReference(resolution.RecipeRef), new PortionQuantity(resolution.Portions));
        }

        var existingOrder = await productionOrderRepository.GetByDeliveryDateAsync(targetDate, cancellationToken);

        var order = OrderConsolidationService.Consolidate(calendars, deliveryDate, existingOrder, clock.Now);

        if (existingOrder is null)
        {
            productionOrderRepository.Add(order);
        }

        await context.SaveChangesAsync(cancellationToken);
    }
}
