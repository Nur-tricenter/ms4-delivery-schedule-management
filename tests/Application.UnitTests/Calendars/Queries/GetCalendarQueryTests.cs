using Ardalis.GuardClauses;
using delivery_schedule_management.Application.Calendars.Commands.CreateSchedule;
using delivery_schedule_management.Application.Calendars.Queries.GetCalendar;
using delivery_schedule_management.Application.UnitTests.Common.Fakes;
using NUnit.Framework;
using Shouldly;

namespace delivery_schedule_management.Application.UnitTests.Calendars.Queries;

public class GetCalendarQueryTests
{
    [Test]
    public async Task Handle_ShouldReturnCalendar_WhenExists()
    {
        var repository = new FakeCalendarRepository();
        var createHandler = new CreateScheduleCommandHandler(repository, new FakeApplicationDbContext(), new FakePlanCatalog(), Microsoft.Extensions.Logging.Abstractions.NullLogger<CreateScheduleCommandHandler>.Instance);
        var created = await createHandler.Handle(
            new CreateScheduleCommand(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 15, new DateOnly(2026, 8, 10)),
            CancellationToken.None);

        var handler = new GetCalendarQueryHandler(repository);

        var result = await handler.Handle(new GetCalendarQuery(created.CalendarId), CancellationToken.None);

        result.Id.ShouldBe(created.CalendarId);
        result.Days.Count.ShouldBe(15);
    }

    [Test]
    public void Handle_ShouldThrowNotFoundException_WhenMissing()
    {
        var handler = new GetCalendarQueryHandler(new FakeCalendarRepository());

        Should.ThrowAsync<NotFoundException>(() => handler.Handle(new GetCalendarQuery(999), CancellationToken.None));
    }
}
