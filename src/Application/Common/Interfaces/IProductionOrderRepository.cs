using delivery_schedule_management.Domain.Entities;

namespace delivery_schedule_management.Application.Common.Interfaces;

public interface IProductionOrderRepository
{
    Task<ProductionOrder?> GetByDeliveryDateAsync(DateOnly date, CancellationToken cancellationToken);

    void Add(ProductionOrder order);
}
