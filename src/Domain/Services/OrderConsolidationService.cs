using delivery_schedule_management.Domain.Entities;
using delivery_schedule_management.Domain.Enums;
using delivery_schedule_management.Domain.Exceptions;
using delivery_schedule_management.Domain.ValueObjects;

namespace delivery_schedule_management.Domain.Services;

/// <summary>
/// Builds or refreshes the <see cref="ProductionOrder"/> for a given delivery date from every
/// <see cref="DeliveryDay"/>, across all calendars, that is <see cref="DayStatus.ReadyForProduction"/>
/// for that date. Catalog resolution (<c>ICatalogPort</c>) happens earlier, outside this service —
/// by the time consolidation runs, every eligible day already carries its menu and portions.
/// </summary>
public static class OrderConsolidationService
{
    public static ProductionOrder Consolidate(
        IEnumerable<DeliveryCalendar> calendars,
        DeliveryDate deliveryDate,
        ProductionOrder? existingOrder,
        DateTimeOffset generatedAt)
    {
        var lines = calendars
            .SelectMany(calendar => calendar.Days
                .Where(day => day.Status == DayStatus.ReadyForProduction && day.Date == deliveryDate)
                .Select(day => new ProductionOrderLine(
                    day.Id,
                    calendar.PatientId,
                    day.Address ?? throw new InvalidDeliveryDayTransitionException(day.Id, day.Status, "be consolidated without a delivery address"),
                    calendar.PlanId,
                    day.DeliveryHour)))
            .ToList();

        if (existingOrder is not null)
        {
            existingOrder.ReplaceLines(lines, generatedAt);
            return existingOrder;
        }

        return ProductionOrder.Create(deliveryDate, lines, generatedAt);
    }
}
