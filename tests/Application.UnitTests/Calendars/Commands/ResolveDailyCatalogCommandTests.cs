using delivery_schedule_management.Application.Calendars.Commands.CreateSchedule;
using delivery_schedule_management.Application.Calendars.Commands.ResolveDailyCatalog;
using delivery_schedule_management.Application.Calendars.Commands.SetDeliveryAddress;
using delivery_schedule_management.Application.Common.Interfaces;
using delivery_schedule_management.Application.UnitTests.Common.Fakes;
using delivery_schedule_management.Domain.Enums;
using NUnit.Framework;
using Shouldly;

namespace delivery_schedule_management.Application.UnitTests.Calendars.Commands;

public class ResolveDailyCatalogCommandTests
{
    private static async Task<(FakeCalendarRepository CalendarRepo, FakeProductionOrderRepository OrderRepo, int DayId)> SeedScheduledDay(DateOnly deliveryDate)
    {
        var calendarRepository = new FakeCalendarRepository();
        var createHandler = new CreateScheduleCommandHandler(calendarRepository, new FakeApplicationDbContext());

        var created = await createHandler.Handle(
            new CreateScheduleCommand(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 15, deliveryDate),
            CancellationToken.None);

        var dayId = created.Days[0].Id;

        var addressHandler = new SetDeliveryAddressCommandHandler(calendarRepository, new FakeApplicationDbContext());
        await addressHandler.Handle(
            new SetDeliveryAddressCommand(created.CalendarId, dayId, "Main St 123", "Springfield", 10, 10, null, null),
            CancellationToken.None);

        return (calendarRepository, new FakeProductionOrderRepository(), dayId);
    }

    [Test]
    public async Task Handle_ShouldMarkScheduledDayReadyForProduction_AndBuildProductionOrder()
    {
        var deliveryDate = new DateOnly(2026, 8, 10);
        var (calendarRepository, orderRepository, dayId) = await SeedScheduledDay(deliveryDate);

        var handler = new ResolveDailyCatalogCommandHandler(
            calendarRepository,
            orderRepository,
            new FakeCatalogPort(new CatalogDayResolution("recipe-1", 3)),
            new FakeApplicationDbContext(),
            new TestClock(new DateTimeOffset(deliveryDate.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero).AddDays(-1)));

        await handler.Handle(new ResolveDailyCatalogCommand(deliveryDate), CancellationToken.None);

        var calendar = calendarRepository.Calendars.Single();
        var day = calendar.Days.Single(d => d.Id == dayId);
        day.Status.ShouldBe(DayStatus.ReadyForProduction);
        day.RecipeRef!.Value.ShouldBe("recipe-1");
        day.Portions!.Value.ShouldBe(3);

        orderRepository.Orders.Count.ShouldBe(1);
        orderRepository.Orders[0].Lines.Count.ShouldBe(1);
    }

    [Test]
    public async Task Handle_ShouldRefreshExistingProductionOrder_OnSecondRun()
    {
        var deliveryDate = new DateOnly(2026, 8, 10);
        var (calendarRepository, orderRepository, _) = await SeedScheduledDay(deliveryDate);
        var clock = new TestClock(new DateTimeOffset(deliveryDate.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero).AddDays(-1));

        var handler = new ResolveDailyCatalogCommandHandler(
            calendarRepository, orderRepository, new FakeCatalogPort(), new FakeApplicationDbContext(), clock);

        await handler.Handle(new ResolveDailyCatalogCommand(deliveryDate), CancellationToken.None);
        await handler.Handle(new ResolveDailyCatalogCommand(deliveryDate), CancellationToken.None);

        orderRepository.Orders.Count.ShouldBe(1);
    }
}
