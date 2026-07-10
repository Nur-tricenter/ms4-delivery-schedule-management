using delivery_schedule_management.Application.Calendars.Commands.CreateSchedule;
using delivery_schedule_management.Application.Calendars.Commands.ResolveDailyCatalog;
using delivery_schedule_management.Application.Calendars.Commands.SetDeliveryAddress;
using delivery_schedule_management.Application.Calendars.Queries.GetActiveTomorrow;
using delivery_schedule_management.Application.Common.Interfaces;
using delivery_schedule_management.Application.UnitTests.Common.Fakes;
using NUnit.Framework;
using Shouldly;

namespace delivery_schedule_management.Application.UnitTests.Calendars.Queries;

public class GetActiveTomorrowQueryTests
{
    [Test]
    public async Task Handle_ShouldReturnEmptyList_WhenNoProductionOrderExists()
    {
        var handler = new GetActiveTomorrowQueryHandler(new FakeProductionOrderRepository(), new TestClock(DateTimeOffset.UtcNow));

        var result = await handler.Handle(new GetActiveTomorrowQuery(null), CancellationToken.None);

        result.ShouldBeEmpty();
    }

    [Test]
    public async Task Handle_ShouldReturnConsolidatedLines_WhenProductionOrderExists()
    {
        var deliveryDate = new DateOnly(2026, 8, 10);
        var calendarRepository = new FakeCalendarRepository();
        var orderRepository = new FakeProductionOrderRepository();
        var createHandler = new CreateScheduleCommandHandler(calendarRepository, new FakeApplicationDbContext());

        var created = await createHandler.Handle(
            new CreateScheduleCommand(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 15, deliveryDate),
            CancellationToken.None);

        var addressHandler = new SetDeliveryAddressCommandHandler(calendarRepository, new FakeApplicationDbContext());
        await addressHandler.Handle(
            new SetDeliveryAddressCommand(created.CalendarId, created.Days[0].Id, "Main St 123", "Springfield", 10, 10, null, null),
            CancellationToken.None);

        var resolveClock = new TestClock(new DateTimeOffset(deliveryDate.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero).AddDays(-1));
        var resolveHandler = new ResolveDailyCatalogCommandHandler(
            calendarRepository, orderRepository, new FakeCatalogPort(), new FakeApplicationDbContext(), resolveClock);
        await resolveHandler.Handle(new ResolveDailyCatalogCommand(deliveryDate), CancellationToken.None);

        var queryHandler = new GetActiveTomorrowQueryHandler(orderRepository, resolveClock);
        var result = await queryHandler.Handle(new GetActiveTomorrowQuery(deliveryDate), CancellationToken.None);

        result.Count.ShouldBe(1);
        result[0].PackId.ShouldBe(created.Days[0].Id);
    }
}
