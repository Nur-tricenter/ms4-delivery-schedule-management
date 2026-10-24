using delivery_schedule_management.Application.Calendars.Commands.CreateSchedule;
using delivery_schedule_management.Application.Common.Exceptions;
using delivery_schedule_management.Application.Common.Interfaces;
using delivery_schedule_management.Application.UnitTests.Common.Fakes;
using Microsoft.Extensions.Logging.Abstractions;
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
        var handler = new CreateScheduleCommandHandler(repository, new FakeApplicationDbContext(), new FakePlanCatalog(), Microsoft.Extensions.Logging.Abstractions.NullLogger<CreateScheduleCommandHandler>.Instance);

        var result = await handler.Handle(SampleCommand(Guid.NewGuid()), CancellationToken.None);

        result.Days.Count.ShouldBe(15);
        repository.Calendars.Count.ShouldBe(1);
    }

    [Test]
    public async Task Handle_ShouldBeIdempotent_ByContractId()
    {
        var repository = new FakeCalendarRepository();
        var handler = new CreateScheduleCommandHandler(repository, new FakeApplicationDbContext(), new FakePlanCatalog(), Microsoft.Extensions.Logging.Abstractions.NullLogger<CreateScheduleCommandHandler>.Instance);
        var contractId = Guid.NewGuid();

        var first = await handler.Handle(SampleCommand(contractId), CancellationToken.None);
        var second = await handler.Handle(SampleCommand(contractId), CancellationToken.None);

        second.CalendarId.ShouldBe(first.CalendarId);
        repository.Calendars.Count.ShouldBe(1);
    }

    [Test]
    public async Task Handle_ShouldReject_DurationThatDoesNotMatchTheProjectedPlan()
    {
        var repository = new FakeCalendarRepository();
        var plans = new FakePlanCatalog();
        var planId = Guid.NewGuid();
        plans.Plans[planId] = new PlanSummary(planId, "Keto 30", 30);
        var handler = new CreateScheduleCommandHandler(repository, new FakeApplicationDbContext(), plans,
            NullLogger<CreateScheduleCommandHandler>.Instance);

        var ex = await Should.ThrowAsync<ValidationException>(() => handler.Handle(
            new CreateScheduleCommand(Guid.NewGuid(), Guid.NewGuid(), planId, 15, new DateOnly(2026, 8, 1)),
            CancellationToken.None));

        ex.Errors.ShouldContainKey(nameof(CreateScheduleCommand.Duration));
        repository.Calendars.ShouldBeEmpty();
    }

    [Test]
    public async Task Handle_ShouldAccept_PlanNotYetProjected()
    {
        // Absence from the projection means "unknown", not "invalid": the event may still be in flight.
        var repository = new FakeCalendarRepository();
        var handler = new CreateScheduleCommandHandler(repository, new FakeApplicationDbContext(), new FakePlanCatalog(),
            NullLogger<CreateScheduleCommandHandler>.Instance);

        var result = await handler.Handle(SampleCommand(Guid.NewGuid()), CancellationToken.None);

        result.Days.Count.ShouldBe(15);
    }
}
