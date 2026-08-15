using Ardalis.GuardClauses;
using delivery_schedule_management.Application.Calendars.Commands.ApplyDeliveryResult;
using delivery_schedule_management.Application.Calendars.Commands.CreateSchedule;
using delivery_schedule_management.Application.Calendars.Commands.SetDeliveryAddress;
using delivery_schedule_management.Application.UnitTests.Common.Fakes;
using delivery_schedule_management.Domain.Entities;
using delivery_schedule_management.Domain.Enums;
using delivery_schedule_management.Domain.ValueObjects;
using NUnit.Framework;
using Shouldly;
using DeliveryResult = delivery_schedule_management.Domain.Enums.DeliveryResult;

namespace delivery_schedule_management.Application.UnitTests.Calendars.Commands;

public class ApplyDeliveryResultCommandTests
{
    private static async Task<(FakeCalendarRepository Repo, int DayId)> SeedReadyDay()
    {
        var repository = new FakeCalendarRepository();
        var createHandler = new CreateScheduleCommandHandler(repository, new FakeApplicationDbContext());

        var created = await createHandler.Handle(
            new CreateScheduleCommand(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 15, new DateOnly(2026, 8, 10)),
            CancellationToken.None);

        var dayId = created.Days[0].Id;

        var addressHandler = new SetDeliveryAddressCommandHandler(repository, new FakeApplicationDbContext());
        await addressHandler.Handle(
            new SetDeliveryAddressCommand(created.CalendarId, dayId, "Main St 123", "Springfield", 10, 10, null, null),
            CancellationToken.None);

        var calendar = await repository.GetByIdAsync(created.CalendarId, CancellationToken.None);
        calendar!.Days.First(d => d.Id == dayId).MarkReadyForProduction(new RecipeReference("recipe-1"), new PortionQuantity(2));

        return (repository, dayId);
    }

    [Test]
    public async Task Handle_ShouldMarkDelivered_WhenDayReadyForProduction()
    {
        var (repository, dayId) = await SeedReadyDay();
        var handler = new ApplyDeliveryResultCommandHandler(repository, new FakeApplicationDbContext(), new TestClock(DateTimeOffset.UtcNow));

        var result = await handler.Handle(new ApplyDeliveryResultCommand(dayId, DeliveryResult.Delivered, null, null), CancellationToken.None);

        result.Status.ShouldBe(DayStatus.Delivered);
    }

    [Test]
    public async Task Handle_ShouldBeIdempotent_WhenCalledTwiceWithSameResult()
    {
        var (repository, dayId) = await SeedReadyDay();
        var handler = new ApplyDeliveryResultCommandHandler(repository, new FakeApplicationDbContext(), new TestClock(DateTimeOffset.UtcNow));

        await handler.Handle(new ApplyDeliveryResultCommand(dayId, DeliveryResult.Delivered, null, null), CancellationToken.None);
        var second = await handler.Handle(new ApplyDeliveryResultCommand(dayId, DeliveryResult.Delivered, null, null), CancellationToken.None);

        second.Status.ShouldBe(DayStatus.Delivered);
    }

    [Test]
    public void Handle_ShouldThrowNotFoundException_WhenDayMissing()
    {
        var repository = new FakeCalendarRepository();
        var handler = new ApplyDeliveryResultCommandHandler(repository, new FakeApplicationDbContext(), new TestClock(DateTimeOffset.UtcNow));

        Should.ThrowAsync<NotFoundException>(() =>
            handler.Handle(new ApplyDeliveryResultCommand(999, DeliveryResult.Delivered, null, null), CancellationToken.None));
    }
}
