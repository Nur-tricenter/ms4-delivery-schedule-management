using Ardalis.GuardClauses;
using delivery_schedule_management.Application.Calendars.Commands.CreateSchedule;
using delivery_schedule_management.Application.Calendars.Queries.GetCalendarByContract;
using delivery_schedule_management.Application.Calendars.Queries.GetCalendarsByPatient;
using delivery_schedule_management.Application.Calendars.Queries.GetDayDeliveryLink;
using delivery_schedule_management.Application.Common.Interfaces;
using delivery_schedule_management.Application.UnitTests.Common.Fakes;
using delivery_schedule_management.Domain.Enums;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;
using Shouldly;

namespace delivery_schedule_management.Application.UnitTests.Calendars.Queries;

/// <summary>Lookups for following a case end to end (cross-service gap X-17).</summary>
public class CalendarLookupQueryTests
{
    private readonly FakeCalendarRepository _repository = new();

    private async Task<CreateScheduleResult> CreateAsync(Guid patientId, Guid contractId)
    {
        var handler = new CreateScheduleCommandHandler(
            _repository, new FakeApplicationDbContext(), new FakePlanCatalog(), NullLogger<CreateScheduleCommandHandler>.Instance);

        return await handler.Handle(
            new CreateScheduleCommand(contractId, patientId, Guid.NewGuid(), 15, new DateOnly(2026, 8, 10)),
            CancellationToken.None);
    }

    [Test]
    public async Task ByContract_ShouldReturnTheContractsCalendar()
    {
        var contractId = Guid.NewGuid();
        await CreateAsync(Guid.NewGuid(), Guid.NewGuid());
        var created = await CreateAsync(Guid.NewGuid(), contractId);

        var result = await new GetCalendarByContractQueryHandler(_repository)
            .Handle(new GetCalendarByContractQuery(contractId), CancellationToken.None);

        result.Id.ShouldBe(created.CalendarId);
        result.ContractId.ShouldBe(contractId);
        result.Days.Count.ShouldBe(15);
    }

    [Test]
    public async Task ByContract_ShouldThrowNotFound_WhenNoCalendar()
    {
        await Should.ThrowAsync<NotFoundException>(() => new GetCalendarByContractQueryHandler(_repository)
            .Handle(new GetCalendarByContractQuery(Guid.NewGuid()), CancellationToken.None));
    }

    [Test]
    public async Task ByPatient_ShouldReturnOnlyThatPatientsCalendars_NewestFirst()
    {
        var patientId = Guid.NewGuid();
        var first = await CreateAsync(patientId, Guid.NewGuid());
        await CreateAsync(Guid.NewGuid(), Guid.NewGuid());
        var second = await CreateAsync(patientId, Guid.NewGuid());

        var result = await new GetCalendarsByPatientQueryHandler(_repository)
            .Handle(new GetCalendarsByPatientQuery(patientId), CancellationToken.None);

        result.Select(c => c.Id).ShouldBe([second.CalendarId, first.CalendarId]);
    }

    [Test]
    public async Task ByPatient_ShouldReturnEmpty_WhenUnknownPatient()
    {
        var result = await new GetCalendarsByPatientQueryHandler(_repository)
            .Handle(new GetCalendarsByPatientQuery(Guid.NewGuid()), CancellationToken.None);

        result.ShouldBeEmpty();
    }

    [Test]
    public async Task DeliveryLink_ShouldReturnTheLatestLink_WhenRouted()
    {
        var created = await CreateAsync(Guid.NewGuid(), Guid.NewGuid());
        var dayId = created.Days[0].Id;
        var links = new FakeDeliveryRouteLinks();
        var latest = new DeliveryRouteLinkSummary(Guid.NewGuid(), Guid.NewGuid(), dayId, DateTimeOffset.UtcNow);
        links.Links.Add(new DeliveryRouteLinkSummary(Guid.NewGuid(), Guid.NewGuid(), dayId, DateTimeOffset.UtcNow.AddMinutes(-5)));
        links.Links.Add(latest);

        var result = await new GetDayDeliveryLinkQueryHandler(_repository, links)
            .Handle(new GetDayDeliveryLinkQuery(dayId), CancellationToken.None);

        result.DayId.ShouldBe(dayId);
        result.CalendarId.ShouldBe(created.CalendarId);
        result.DayStatus.ShouldBe(DayStatus.Pending);
        result.DeliveryId.ShouldBe(latest.DeliveryId);
        result.RouteId.ShouldBe(latest.RouteId);
    }

    [Test]
    public async Task DeliveryLink_ShouldReturnNullDeliveryFields_WhenNotRoutedYet()
    {
        var created = await CreateAsync(Guid.NewGuid(), Guid.NewGuid());

        var result = await new GetDayDeliveryLinkQueryHandler(_repository, new FakeDeliveryRouteLinks())
            .Handle(new GetDayDeliveryLinkQuery(created.Days[0].Id), CancellationToken.None);

        result.DeliveryId.ShouldBeNull();
        result.RouteId.ShouldBeNull();
        result.LinkedAt.ShouldBeNull();
    }

    [Test]
    public async Task DeliveryLink_ShouldThrowNotFound_WhenDayMissing()
    {
        await Should.ThrowAsync<NotFoundException>(() => new GetDayDeliveryLinkQueryHandler(_repository, new FakeDeliveryRouteLinks())
            .Handle(new GetDayDeliveryLinkQuery(999), CancellationToken.None));
    }
}
