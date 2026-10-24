using delivery_schedule_management.Application.Calendars.Commands.CancelDeliveryDay;
using delivery_schedule_management.Application.Calendars.Commands.CreateSchedule;
using delivery_schedule_management.Application.UnitTests.Common.Fakes;
using delivery_schedule_management.Domain.Enums;
using delivery_schedule_management.Domain.Exceptions;
using NUnit.Framework;
using Shouldly;

namespace delivery_schedule_management.Application.UnitTests.Calendars.Commands;

public class CancelDeliveryDayCommandTests
{
    private static async Task<(FakeCalendarRepository Repo, int CalendarId, int DayId)> SeedCalendar()
    {
        var repository = new FakeCalendarRepository();
        var createHandler = new CreateScheduleCommandHandler(repository, new FakeApplicationDbContext(), new FakePlanCatalog(), Microsoft.Extensions.Logging.Abstractions.NullLogger<CreateScheduleCommandHandler>.Instance);

        var created = await createHandler.Handle(
            new CreateScheduleCommand(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 15, new DateOnly(2026, 8, 10)),
            CancellationToken.None);

        return (repository, created.CalendarId, created.Days[0].Id);
    }

    [Test]
    public async Task Handle_ShouldCancelDay_WhenOutsideLockWindow()
    {
        var (repository, calendarId, dayId) = await SeedCalendar();
        var clock = new TestClock(new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero));
        var handler = new CancelDeliveryDayCommandHandler(repository, new FakeApplicationDbContext(), clock);

        var result = await handler.Handle(new CancelDeliveryDayCommand(calendarId, dayId), CancellationToken.None);

        result.Status.ShouldBe(DayStatus.Cancelled);
    }

    [Test]
    public async Task Handle_ShouldThrowDeliveryWindowLockedException_WhenInsideFortyEightHourWindow()
    {
        var (repository, calendarId, dayId) = await SeedCalendar();
        var clock = new TestClock(new DateTimeOffset(2026, 8, 9, 12, 0, 0, TimeSpan.Zero));
        var handler = new CancelDeliveryDayCommandHandler(repository, new FakeApplicationDbContext(), clock);

        await Should.ThrowAsync<DeliveryWindowLockedException>(() =>
            handler.Handle(new CancelDeliveryDayCommand(calendarId, dayId), CancellationToken.None));
    }
}
