using delivery_schedule_management.Domain.Entities;

namespace delivery_schedule_management.Application.Common.Interfaces;

/// <summary>
/// Outbound port: tells the rest of the platform that a delivery date's production order is ready
/// (MS3 projects it; no service calls MS4 for it).
/// </summary>
/// <remarks>
/// Implementations only STAGE the event in the same unit of work as the order; they never save and
/// never publish. The caller's <see cref="IApplicationDbContext.SaveChangesAsync"/> commits the order
/// and the staged event together, and a background dispatcher publishes it afterwards.
/// </remarks>
public interface IProductionOrderEvents
{
    /// <summary>
    /// Stages the full snapshot of <paramref name="order"/>. <paramref name="calendars"/> are the
    /// calendars the order was consolidated from: they carry each line's menu.
    /// </summary>
    void ProductionOrderReady(ProductionOrder order, IEnumerable<DeliveryCalendar> calendars);
}
