using delivery_schedule_management.Application.Calendars.Commands.ApplyDeliveryResult;
using delivery_schedule_management.Application.Calendars.Commands.CancelDeliveryDay;
using delivery_schedule_management.Application.Calendars.Commands.CreateSchedule;
using delivery_schedule_management.Application.Calendars.Commands.RescheduleDeliveryDay;
using delivery_schedule_management.Application.Calendars.Commands.ResolveDailyCatalog;
using delivery_schedule_management.Application.Calendars.Commands.SetDeliveryAddress;
using delivery_schedule_management.Application.Calendars.Queries.GetActiveTomorrow;
using delivery_schedule_management.Application.Calendars.Queries.GetCalendar;
using delivery_schedule_management.Domain.Entities;
using delivery_schedule_management.Domain.Enums;
using delivery_schedule_management.Domain.Exceptions;

namespace delivery_schedule_management.Application.FunctionalTests.Calendars;

/// <summary>
/// Acceptance criteria from CLAUDE.md §2 (Stage 7), exercised through the MediatR pipeline against
/// a real Postgres instance (Aspire-orchestrated, reset via Respawn between tests).
/// </summary>
public class CalendarAcceptanceTests : TestBase
{
    [Test]
    public async Task CreateSchedule_ShouldBeIdempotent_ByContractId()
    {
        var command = new CreateScheduleCommand(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 15, DateOnly.FromDateTime(DateTime.UtcNow).AddDays(10));

        var first = await TestApp.SendAsync(command);
        var second = await TestApp.SendAsync(command);

        second.CalendarId.ShouldBe(first.CalendarId);
        (await TestApp.CountAsync<DeliveryCalendar>()).ShouldBe(1);
    }

    [Test]
    public async Task CancelDeliveryDay_ShouldBeRejected_WhenLessThan48HoursRemain()
    {
        var created = await TestApp.SendAsync(new CreateScheduleCommand(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 15, DateOnly.FromDateTime(DateTime.UtcNow)));

        var dayId = created.Days[0].Id;

        await Should.ThrowAsync<DeliveryWindowLockedException>(() =>
            TestApp.SendAsync(new CancelDeliveryDayCommand(created.CalendarId, dayId)));
    }

    [Test]
    public async Task RescheduleDeliveryDay_ShouldBeRejected_WhenLessThan48HoursRemain()
    {
        var created = await TestApp.SendAsync(new CreateScheduleCommand(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 15, DateOnly.FromDateTime(DateTime.UtcNow)));

        var dayId = created.Days[0].Id;
        var newDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(20);

        await Should.ThrowAsync<DeliveryWindowLockedException>(() =>
            TestApp.SendAsync(new RescheduleDeliveryDayCommand(created.CalendarId, dayId, newDate)));
    }

    [Test]
    public async Task ActiveTomorrow_ShouldReturnOnly_ReadyForProductionDays_ForTargetDate()
    {
        var tomorrow = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(1);

        // Calendar A: tomorrow's day gets an address (-> Scheduled), then the catalog job resolves it
        // (-> ReadyForProduction) and consolidates it into the ProductionOrder.
        var readyCalendar = await TestApp.SendAsync(new CreateScheduleCommand(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 15, tomorrow));

        var readyDayId = readyCalendar.Days[0].Id;

        await TestApp.SendAsync(new SetDeliveryAddressCommand(
            readyCalendar.CalendarId, readyDayId, "742 Evergreen Terrace", "Springfield", 10.5, -20.25, null, null));

        // Calendar B: tomorrow's day stays Pending (no address set) - the catalog job must skip it,
        // and it must not show up in active-tomorrow.
        await TestApp.SendAsync(new CreateScheduleCommand(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 15, tomorrow));

        await TestApp.SendAsync(new ResolveDailyCatalogCommand(tomorrow));

        var result = await TestApp.SendAsync(new GetActiveTomorrowQuery(tomorrow));

        result.Count.ShouldBe(1);
        result[0].PackId.ShouldBe(readyDayId);

        var calendarVm = await TestApp.SendAsync(new GetCalendarQuery(readyCalendar.CalendarId));
        calendarVm.Days.First(d => d.Id == readyDayId).Status.ShouldBe(DayStatus.ReadyForProduction);
    }

    [Test]
    public async Task ApplyDeliveryResult_ShouldUpdateStatus_AndBeIdempotent()
    {
        var targetDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(1);

        var created = await TestApp.SendAsync(new CreateScheduleCommand(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 15, targetDate));

        var dayId = created.Days[0].Id;

        await TestApp.SendAsync(new SetDeliveryAddressCommand(
            created.CalendarId, dayId, "100 5th Ave", "Metropolis", 5, 5, null, null));

        await TestApp.SendAsync(new ResolveDailyCatalogCommand(targetDate));

        var first = await TestApp.SendAsync(
            new ApplyDeliveryResultCommand(dayId, DeliveryResult.Delivered, "https://example.com/photo.jpg", null));
        first.Status.ShouldBe(DayStatus.Delivered);

        // Idempotent: reporting the same result twice is a no-op, not an error or a state change.
        var second = await TestApp.SendAsync(
            new ApplyDeliveryResultCommand(dayId, DeliveryResult.Delivered, "https://example.com/photo.jpg", null));
        second.Status.ShouldBe(DayStatus.Delivered);
    }
}
