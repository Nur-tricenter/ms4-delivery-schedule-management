using delivery_schedule_management.Application.Common.Interfaces;
using delivery_schedule_management.Domain.Entities;

namespace delivery_schedule_management.Application.UnitTests.Common.Fakes;

public class FakeProductionOrderRepository : IProductionOrderRepository
{
    private readonly List<ProductionOrder> _orders = [];
    private int _nextOrderId = 1;

    public IReadOnlyList<ProductionOrder> Orders => _orders;

    public Task<ProductionOrder?> GetByDeliveryDateAsync(DateOnly date, CancellationToken cancellationToken) =>
        Task.FromResult(_orders.FirstOrDefault(o => o.DeliveryDate.Value == date));

    public void Add(ProductionOrder order)
    {
        order.Id = _nextOrderId++;
        _orders.Add(order);
    }
}
