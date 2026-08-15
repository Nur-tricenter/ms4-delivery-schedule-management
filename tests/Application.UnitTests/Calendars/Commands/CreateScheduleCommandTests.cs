using delivery_schedule_management.Application.Calendars.Commands.CreateSchedule;
using delivery_schedule_management.Application.UnitTests.Common.Fakes;
using NUnit.Framework;
using Shouldly;

namespace delivery_schedule_management.Application.UnitTests.Calendars.Commands;

public class CreateScheduleCommandTests
{
    private static CreateScheduleCommand SampleCommand(Guid contractId) =>
        new(contractId, Guid.NewGuid(), Guid.NewGuid(), 15, new DateOnly(2026, 8, 1));

    [Test]
    public async Task Handle_ShouldCreateCalendar_WithOneDayPerDurationDay()
    {
        var repository = new FakeCalendarRepository();
        var handler = new CreateScheduleCommandHandler(repository, new FakeApplicationDbContext());

        var result = await handler.Handle(SampleCommand(Guid.NewGuid()), CancellationToken.None);

        result.Days.Count.ShouldBe(15);
        repository.Calendars.Count.ShouldBe(1);
    }

    [Test]
    public async Task Handle_ShouldBeIdempotent_ByContractId()
    {
        var repository = new FakeCalendarRepository();
        var handler = new CreateScheduleCommandHandler(repository, new FakeApplicationDbContext());
        var contractId = Guid.NewGuid();

        var first = await handler.Handle(SampleCommand(contractId), CancellationToken.None);
        var second = await handler.Handle(SampleCommand(contractId), CancellationToken.None);

        second.CalendarId.ShouldBe(first.CalendarId);
        repository.Calendars.Count.ShouldBe(1);
    }
}
