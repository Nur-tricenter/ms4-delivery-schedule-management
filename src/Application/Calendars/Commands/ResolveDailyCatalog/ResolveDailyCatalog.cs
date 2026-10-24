using delivery_schedule_management.Application.Common.Interfaces;
using delivery_schedule_management.Domain.Enums;
using delivery_schedule_management.Domain.Services;
using delivery_schedule_management.Domain.ValueObjects;
using Microsoft.Extensions.Logging;

namespace delivery_schedule_management.Application.Calendars.Commands.ResolveDailyCatalog;

/// <summary>
/// Internal job (not exposed via API). For every <see cref="DayStatus.Scheduled"/> day landing on
/// the target date, resolves that plan day's menu via <see cref="ICatalogPort"/> and marks it
/// <see cref="DayStatus.ReadyForProduction"/>, then (re)builds that date's <c>ProductionOrder</c>
/// via <see cref="OrderConsolidationService"/>. Must run before MS3 calls
/// <c>GET /calendars/active-tomorrow</c> for the same date.
/// </summary>
/// <remarks>
/// One failing day never blocks the rest: a day whose plan MS2 doesn't know, or whose resolution
/// throws (MS2 unreachable), stays <see cref="DayStatus.Scheduled"/> and is picked up by the next
/// run. The job is idempotent, so re-running it after an MS2 outage is the recovery path.
/// </remarks>
public record ResolveDailyCatalogCommand(DateOnly? TargetDate) : IRequest;

public class ResolveDailyCatalogCommandHandler(
    ICalendarRepository calendarRepository,
    IProductionOrderRepository productionOrderRepository,
    ICatalogPort catalogPort,
    IApplicationDbContext context,
    IClock clock,
    ILogger<ResolveDailyCatalogCommandHandler> logger) : IRequestHandler<ResolveDailyCatalogCommand>
{
    public async Task Handle(ResolveDailyCatalogCommand request, CancellationToken cancellationToken)
    {
        var targetDate = request.TargetDate ?? DateOnly.FromDateTime(clock.Now.Date).AddDays(1);
        var deliveryDate = new DeliveryDate(targetDate);

        var calendars = await calendarRepository.GetActiveForDeliveryDateAsync(targetDate, cancellationToken);

        foreach (var calendar in calendars)
        {
            var day = calendar.Days.FirstOrDefault(d => d.Date == deliveryDate && d.Status == DayStatus.Scheduled);
            if (day is null)
            {
                continue;
            }

            CatalogDayMenu? menu;
            try
            {
                menu = await catalogPort.GetDayMenuAsync(calendar.PlanId.Value, day.DayNumber, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex,
                    "Could not resolve plan {PlanId} day {DayNumber} for delivery day {DayId}; it stays Scheduled until the next run.",
                    calendar.PlanId.Value, day.DayNumber, day.Id);
                continue;
            }

            if (menu is null || menu.Items.Count == 0)
            {
                logger.LogWarning(
                    "MS2 has no menu for plan {PlanId} day {DayNumber}; delivery day {DayId} stays Scheduled.",
                    calendar.PlanId.Value, day.DayNumber, day.Id);
                continue;
            }

            day.MarkReadyForProduction(menu.Items
                .Select(i => new MenuItem(i.MealTime, i.MealTimeOrder, i.RecipeId, i.RecipeName, i.Rations))
                .ToList());
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
