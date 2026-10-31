using delivery_schedule_management.Application.Common.Interfaces;
using delivery_schedule_management.Domain.Entities;

namespace delivery_schedule_management.Application.UnitTests.Common.Fakes;

public class FakeProductionOrderEvents : IProductionOrderEvents
{
    public List<(ProductionOrder Order, IReadOnlyList<DeliveryCalendar> Calendars)> Staged { get; } = [];

    public void ProductionOrderReady(ProductionOrder order, IEnumerable<DeliveryCalendar> calendars) =>
        Staged.Add((order, calendars.ToList()));
}
