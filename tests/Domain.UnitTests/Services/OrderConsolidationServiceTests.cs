using delivery_schedule_management.Domain.Entities;
using delivery_schedule_management.Domain.Services;
using delivery_schedule_management.Domain.ValueObjects;
using NUnit.Framework;
using Shouldly;

namespace delivery_schedule_management.Domain.UnitTests.Services;

public class OrderConsolidationServiceTests
{
    private static DeliveryAddress SampleAddress() =>
        new("Main St 123", "Springfield", new Coordinates(10, 10));

    private static DeliveryCalendar CreateReadyCalendar(DateOnly deliveryDate)
    {
        var calendar = DeliveryCalendar.Create(
            Guid.NewGuid(),
            new ContractReference(Guid.NewGuid()),
            new PlanReference(Guid.NewGuid()),
            new PlanDuration(15),
            new DeliveryDate(deliveryDate));

        var day = calendar.Days.First();
        day.SetAddress(SampleAddress());
        day.MarkReadyForProduction(new RecipeReference("recipe-1"), new PortionQuantity(2));

        return calendar;
    }

    [Test]
    public void Consolidate_ShouldOnlyIncludeReadyForProductionDaysOnTheGivenDate()
    {
        var deliveryDate = new DateOnly(2026, 8, 10);
        var readyCalendar = CreateReadyCalendar(deliveryDate);

        var pendingCalendar = DeliveryCalendar.Create(
            Guid.NewGuid(),
            new ContractReference(Guid.NewGuid()),
            new PlanReference(Guid.NewGuid()),
            new PlanDuration(15),
            new DeliveryDate(deliveryDate));

        var order = OrderConsolidationService.Consolidate(
            [readyCalendar, pendingCalendar],
            new DeliveryDate(deliveryDate),
            existingOrder: null,
            generatedAt: DateTimeOffset.UtcNow);

        order.Lines.Count.ShouldBe(1);
        order.Lines.Single().PatientId.ShouldBe(readyCalendar.PatientId);
        order.DeliveryDate.ShouldBe(new DeliveryDate(deliveryDate));
        order.ProductionDate.ShouldBe(new DeliveryDate(deliveryDate.AddDays(-1)));
    }

    [Test]
    public void Consolidate_ShouldReplaceLines_WhenExistingOrderProvided()
    {
        var deliveryDate = new DateOnly(2026, 8, 10);
        var firstCalendar = CreateReadyCalendar(deliveryDate);

        var existingOrder = OrderConsolidationService.Consolidate(
            [firstCalendar],
            new DeliveryDate(deliveryDate),
            existingOrder: null,
            generatedAt: DateTimeOffset.UtcNow);

        var secondCalendar = CreateReadyCalendar(deliveryDate);

        var updatedOrder = OrderConsolidationService.Consolidate(
            [firstCalendar, secondCalendar],
            new DeliveryDate(deliveryDate),
            existingOrder,
            generatedAt: DateTimeOffset.UtcNow);

        updatedOrder.ShouldBeSameAs(existingOrder);
        updatedOrder.Lines.Count.ShouldBe(2);
    }
}
