using delivery_schedule_management.Application.Calendars.Commands.CreateSchedule;
using delivery_schedule_management.Application.Calendars.Commands.ResolveDailyCatalog;
using delivery_schedule_management.Application.Calendars.Commands.SetDeliveryAddress;
using delivery_schedule_management.Application.Common.Interfaces;
using delivery_schedule_management.Application.UnitTests.Common.Fakes;
using delivery_schedule_management.Domain.Enums;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;
using Shouldly;

namespace delivery_schedule_management.Application.UnitTests.Calendars.Commands;

public class ResolveDailyCatalogCommandTests
{
    private static async Task<(FakeCalendarRepository CalendarRepo, FakeProductionOrderRepository OrderRepo, int DayId)> SeedScheduledDay(
        DateOnly startDate, int dayIndex = 0)
    {
        var calendarRepository = new FakeCalendarRepository();
        var createHandler = new CreateScheduleCommandHandler(calendarRepository, new FakeApplicationDbContext(), new FakePlanCatalog(), Microsoft.Extensions.Logging.Abstractions.NullLogger<CreateScheduleCommandHandler>.Instance);

        var created = await createHandler.Handle(
            new CreateScheduleCommand(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 15, startDate),
            CancellationToken.None);

        var dayId = created.Days[dayIndex].Id;

        var addressHandler = new SetDeliveryAddressCommandHandler(calendarRepository, new FakeApplicationDbContext());
        await addressHandler.Handle(
            new SetDeliveryAddressCommand(created.CalendarId, dayId, "Main St 123", "Springfield", 10, 10, null, null),
            CancellationToken.None);

        return (calendarRepository, new FakeProductionOrderRepository(), dayId);
    }

    private static ResolveDailyCatalogCommandHandler CreateHandler(
        FakeCalendarRepository calendars, FakeProductionOrderRepository orders, ICatalogPort catalog, DateOnly deliveryDate) =>
        new(calendars, orders, catalog, new FakeApplicationDbContext(),
            new TestClock(new DateTimeOffset(deliveryDate.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero).AddDays(-1)),
            NullLogger<ResolveDailyCatalogCommandHandler>.Instance);

    [Test]
    public async Task Handle_ShouldMarkScheduledDayReadyForProduction_AndBuildProductionOrder()
    {
        var deliveryDate = new DateOnly(2026, 8, 10);
        var (calendarRepository, orderRepository, dayId) = await SeedScheduledDay(deliveryDate);
        var recipeId = Guid.NewGuid();
        var catalog = new FakeCatalogPort(new CatalogDayMenu(
        [
            new CatalogMenuItem("Breakfast", 1, Guid.NewGuid(), "Oats", 1),
            new CatalogMenuItem("Lunch", 3, recipeId, "Quinoa bowl", 2)
        ]));

        await CreateHandler(calendarRepository, orderRepository, catalog, deliveryDate)
            .Handle(new ResolveDailyCatalogCommand(deliveryDate), CancellationToken.None);

        var day = calendarRepository.Calendars.Single().Days.Single(d => d.Id == dayId);
        day.Status.ShouldBe(DayStatus.ReadyForProduction);
        day.Menu.Count.ShouldBe(2);
        day.Menu.ShouldContain(item => item.RecipeId == recipeId && item.RecipeName == "Quinoa bowl" && item.Rations == 2);
        day.Portions!.Value.ShouldBe(3);

        orderRepository.Orders.Count.ShouldBe(1);
        orderRepository.Orders[0].Lines.Count.ShouldBe(1);
    }

    [Test]
    public async Task Handle_ShouldAskCatalogForThePlanDayNumber_NotTheDate()
    {
        // Day index 4 of a calendar starting Aug 6 lands on Aug 10 and is plan day 5.
        var deliveryDate = new DateOnly(2026, 8, 10);
        var (calendarRepository, orderRepository, _) = await SeedScheduledDay(deliveryDate.AddDays(-4), dayIndex: 4);
        var catalog = new FakeCatalogPort();

        await CreateHandler(calendarRepository, orderRepository, catalog, deliveryDate)
            .Handle(new ResolveDailyCatalogCommand(deliveryDate), CancellationToken.None);

        var calendar = calendarRepository.Calendars.Single();
        catalog.Calls.ShouldBe([(calendar.PlanId.Value, 5)]);
    }

    [Test]
    public async Task Handle_ShouldLeaveDayScheduled_WhenCatalogIsUnreachable()
    {
        var deliveryDate = new DateOnly(2026, 8, 10);
        var (calendarRepository, orderRepository, dayId) = await SeedScheduledDay(deliveryDate);
        var catalog = new FakeCatalogPort { ThrowOnCall = new HttpRequestException("MS2 down") };

        await CreateHandler(calendarRepository, orderRepository, catalog, deliveryDate)
            .Handle(new ResolveDailyCatalogCommand(deliveryDate), CancellationToken.None);

        var day = calendarRepository.Calendars.Single().Days.Single(d => d.Id == dayId);
        day.Status.ShouldBe(DayStatus.Scheduled);
        orderRepository.Orders.Single().Lines.ShouldBeEmpty();
    }

    [Test]
    public async Task Handle_ShouldLeaveDayScheduled_WhenCatalogHasNoMenuForThatDay()
    {
        var deliveryDate = new DateOnly(2026, 8, 10);
        var (calendarRepository, orderRepository, dayId) = await SeedScheduledDay(deliveryDate);
        var catalog = new FakeCatalogPort(new CatalogDayMenu([]));

        await CreateHandler(calendarRepository, orderRepository, catalog, deliveryDate)
            .Handle(new ResolveDailyCatalogCommand(deliveryDate), CancellationToken.None);

        calendarRepository.Calendars.Single().Days.Single(d => d.Id == dayId).Status.ShouldBe(DayStatus.Scheduled);
    }

    [Test]
    public async Task Handle_ShouldRefreshExistingProductionOrder_OnSecondRun()
    {
        var deliveryDate = new DateOnly(2026, 8, 10);
        var (calendarRepository, orderRepository, _) = await SeedScheduledDay(deliveryDate);
        var handler = CreateHandler(calendarRepository, orderRepository, new FakeCatalogPort(), deliveryDate);

        await handler.Handle(new ResolveDailyCatalogCommand(deliveryDate), CancellationToken.None);
        await handler.Handle(new ResolveDailyCatalogCommand(deliveryDate), CancellationToken.None);

        orderRepository.Orders.Count.ShouldBe(1);
    }
}
