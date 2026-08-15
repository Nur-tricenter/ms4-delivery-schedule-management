using delivery_schedule_management.Application.Common.Interfaces;
using delivery_schedule_management.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace delivery_schedule_management.Infrastructure.Data.Repositories;

public class ProductionOrderRepository(ApplicationDbContext context) : IProductionOrderRepository
{
    public Task<ProductionOrder?> GetByDeliveryDateAsync(DateOnly date, CancellationToken cancellationToken) =>
        context.ProductionOrders.Include(o => o.Lines).FirstOrDefaultAsync(o => o.DeliveryDate.Value == date, cancellationToken);

    public void Add(ProductionOrder order) => context.ProductionOrders.Add(order);
}
