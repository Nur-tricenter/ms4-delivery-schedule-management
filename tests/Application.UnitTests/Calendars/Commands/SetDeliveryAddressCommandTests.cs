using delivery_schedule_management.Application.Calendars.Commands.CreateSchedule;
using delivery_schedule_management.Application.Calendars.Commands.SetDeliveryAddress;
using delivery_schedule_management.Application.UnitTests.Common.Fakes;
using delivery_schedule_management.Domain.Enums;
using NUnit.Framework;
using Shouldly;

namespace delivery_schedule_management.Application.UnitTests.Calendars.Commands;

public class SetDeliveryAddressCommandTests
{
    [Test]
    public async Task Handle_ShouldSetAddress_AndTransitionDayToScheduled()
    {
        var repository = new FakeCalendarRepository();
        var createHandler = new CreateScheduleCommandHandler(repository, new FakeApplicationDbContext());
        var created = await createHandler.Handle(
            new CreateScheduleCommand(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 15, new DateOnly(2026, 8, 10)),
            CancellationToken.None);

        var handler = new SetDeliveryAddressCommandHandler(repository, new FakeApplicationDbContext());

        var result = await handler.Handle(
            new SetDeliveryAddressCommand(created.CalendarId, created.Days[0].Id, "Main St 123", "Springfield", 10, 10, null, null),
            CancellationToken.None);

        result.Address.Street.ShouldBe("Main St 123");

        var calendar = await repository.GetByIdAsync(created.CalendarId, CancellationToken.None);
        calendar!.Days.First(d => d.Id == created.Days[0].Id).Status.ShouldBe(DayStatus.Scheduled);
    }
}
