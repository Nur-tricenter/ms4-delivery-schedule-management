using delivery_schedule_management.Domain.Entities;
using delivery_schedule_management.Domain.ValueObjects;
using delivery_schedule_management.Infrastructure.Data.Repositories;

namespace delivery_schedule_management.Infrastructure.IntegrationTests;

public class ProductionOrderRepositoryTests : RepositoryTestBase
{
    [Test]
    public async Task AddThenGetByDeliveryDate_ShouldRoundTripOrderAndLines()
    {
        var deliveryDate = new DeliveryDate(new DateOnly(2026, 8, 10));
        var address = new DeliveryAddress("Main St 123", "Springfield", new Coordinates(10.5, -20.25));
        var line = new ProductionOrderLine(1, Guid.NewGuid(), address, new PlanReference(Guid.NewGuid()), new TimeOnly(9, 0));
        var order = ProductionOrder.Create(deliveryDate, [line], DateTimeOffset.UtcNow);

        await using (var context = IntegrationTestSetup.CreateContext())
        {
            new ProductionOrderRepository(context).Add(order);
            await context.SaveChangesAsync(CancellationToken.None);
        }

        await using var readContext = IntegrationTestSetup.CreateContext();
        var loaded = await new ProductionOrderRepository(readContext).GetByDeliveryDateAsync(deliveryDate.Value, CancellationToken.None);

        loaded.ShouldNotBeNull();
        loaded.Lines.Count.ShouldBe(1);
        loaded.Lines.First().Coordinates.Latitude.ShouldBe(10.5);
        loaded.Lines.First().Address.Street.ShouldBe("Main St 123");
    }

    [Test]
    public async Task ReplaceLines_ThenSave_ShouldOverwriteExistingLines()
    {
        var deliveryDate = new DeliveryDate(new DateOnly(2026, 8, 11));
        var address = new DeliveryAddress("First St", "Town", new Coordinates(1, 1));
        var order = ProductionOrder.Create(
            deliveryDate,
            [new ProductionOrderLine(1, Guid.NewGuid(), address, new PlanReference(Guid.NewGuid()), null)],
            DateTimeOffset.UtcNow);

        await using (var context = IntegrationTestSetup.CreateContext())
        {
            new ProductionOrderRepository(context).Add(order);
            await context.SaveChangesAsync(CancellationToken.None);
        }

        await using (var context = IntegrationTestSetup.CreateContext())
        {
            var repository = new ProductionOrderRepository(context);
            var loaded = await repository.GetByDeliveryDateAsync(deliveryDate.Value, CancellationToken.None);

            var newAddress = new DeliveryAddress("Second St", "Town", new Coordinates(2, 2));
            loaded!.ReplaceLines([new ProductionOrderLine(2, Guid.NewGuid(), newAddress, new PlanReference(Guid.NewGuid()), null)], DateTimeOffset.UtcNow);

            await context.SaveChangesAsync(CancellationToken.None);
        }

        await using var readContext = IntegrationTestSetup.CreateContext();
        var reloaded = await new ProductionOrderRepository(readContext).GetByDeliveryDateAsync(deliveryDate.Value, CancellationToken.None);

        reloaded!.Lines.Count.ShouldBe(1);
        reloaded.Lines.First().DeliveryDayId.ShouldBe(2);
    }
}
