using delivery_schedule_management.Domain.Entities;
using delivery_schedule_management.Domain.Enums;
using delivery_schedule_management.Domain.Exceptions;
using delivery_schedule_management.Domain.ValueObjects;
using NUnit.Framework;
using Shouldly;

namespace delivery_schedule_management.Domain.UnitTests.Entities;

public class DeliveryDayTests
{
    private static DeliveryCalendar CreateCalendarWithOneDay(DateOnly deliveryDate) =>
        DeliveryCalendar.Create(
            Guid.NewGuid(),
            new ContractReference(Guid.NewGuid()),
            new PlanReference(Guid.NewGuid()),
            new PlanDuration(15),
            new DeliveryDate(deliveryDate));

    private static DeliveryAddress SampleAddress() =>
        new("Main St 123", "Springfield", new Coordinates(10, 10));

    [Test]
    public void Cancel_ShouldSucceed_WhenExactlyFortyEightHoursBeforeDelivery()
    {
        var deliveryDate = new DateOnly(2026, 8, 10);
        var day = CreateCalendarWithOneDay(deliveryDate).Days.First();
        var now = new DateTimeOffset(deliveryDate.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero).AddHours(-48);

        Should.NotThrow(() => day.Cancel(now));

        day.Status.ShouldBe(DayStatus.Cancelled);
    }

    [Test]
    public void Cancel_ShouldThrow_WhenOneTickInsideFortyEightHourWindow()
    {
        var deliveryDate = new DateOnly(2026, 8, 10);
        var day = CreateCalendarWithOneDay(deliveryDate).Days.First();
        var now = new DateTimeOffset(deliveryDate.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero).AddHours(-48).AddTicks(1);

        Should.Throw<DeliveryWindowLockedException>(() => day.Cancel(now));

        day.Status.ShouldBe(DayStatus.Pending);
    }

    [Test]
    public void Reschedule_ShouldSucceed_WhenExactlyFortyEightHoursBeforeDelivery()
    {
        var deliveryDate = new DateOnly(2026, 8, 10);
        var day = CreateCalendarWithOneDay(deliveryDate).Days.First();
        var now = new DateTimeOffset(deliveryDate.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero).AddHours(-48);
        var newDate = new DeliveryDate(new DateOnly(2026, 8, 20));

        Should.NotThrow(() => day.Reschedule(newDate, now));

        day.Date.ShouldBe(newDate);
    }

    [Test]
    public void Reschedule_ShouldThrow_WhenOneTickInsideFortyEightHourWindow()
    {
        var deliveryDate = new DateOnly(2026, 8, 10);
        var day = CreateCalendarWithOneDay(deliveryDate).Days.First();
        var now = new DateTimeOffset(deliveryDate.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero).AddHours(-48).AddTicks(1);
        var newDate = new DeliveryDate(new DateOnly(2026, 8, 20));

        Should.Throw<DeliveryWindowLockedException>(() => day.Reschedule(newDate, now));
    }

    [Test]
    public void Cancel_ShouldThrow_WhenDayAlreadyDelivered()
    {
        var deliveryDate = new DateOnly(2026, 8, 10);
        var day = CreateCalendarWithOneDay(deliveryDate).Days.First();
        var farEnoughInPast = new DateTimeOffset(deliveryDate.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero).AddDays(-10);

        day.SetAddress(SampleAddress());
        day.MarkReadyForProduction(new RecipeReference("recipe-1"), new PortionQuantity(2));
        day.ApplyDeliveryResult(DeliveryResult.Delivered, evidence: null, farEnoughInPast);

        Should.Throw<InvalidDeliveryDayTransitionException>(() => day.Cancel(farEnoughInPast));
    }

    [Test]
    public void Reschedule_ShouldThrow_WhenDayIsReadyForProduction()
    {
        var deliveryDate = new DateOnly(2026, 8, 10);
        var day = CreateCalendarWithOneDay(deliveryDate).Days.First();
        var farEnoughInPast = new DateTimeOffset(deliveryDate.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero).AddDays(-10);

        day.SetAddress(SampleAddress());
        day.MarkReadyForProduction(new RecipeReference("recipe-1"), new PortionQuantity(2));

        Should.Throw<InvalidDeliveryDayTransitionException>(() => day.Reschedule(new DeliveryDate(deliveryDate.AddDays(5)), farEnoughInPast));
    }

    [Test]
    public void MarkReadyForProduction_ShouldThrow_WhenDayHasNoAddressYet()
    {
        var day = CreateCalendarWithOneDay(new DateOnly(2026, 8, 10)).Days.First();

        Should.Throw<InvalidDeliveryDayTransitionException>(
            () => day.MarkReadyForProduction(new RecipeReference("recipe-1"), new PortionQuantity(2)));
    }

    [Test]
    public void ApplyDeliveryResult_ShouldBeIdempotent_WhenCalledTwiceWithSameResult()
    {
        var deliveryDate = new DateOnly(2026, 8, 10);
        var day = CreateCalendarWithOneDay(deliveryDate).Days.First();
        var now = new DateTimeOffset(deliveryDate.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero).AddDays(-10);

        day.SetAddress(SampleAddress());
        day.MarkReadyForProduction(new RecipeReference("recipe-1"), new PortionQuantity(2));

        day.ApplyDeliveryResult(DeliveryResult.Delivered, evidence: null, now);
        Should.NotThrow(() => day.ApplyDeliveryResult(DeliveryResult.Delivered, evidence: null, now));

        day.Status.ShouldBe(DayStatus.Delivered);
    }

    [Test]
    public void ApplyDeliveryResult_ShouldThrow_WhenDayNotYetReadyForProduction()
    {
        var day = CreateCalendarWithOneDay(new DateOnly(2026, 8, 10)).Days.First();

        Should.Throw<InvalidDeliveryDayTransitionException>(
            () => day.ApplyDeliveryResult(DeliveryResult.Delivered, evidence: null, DateTimeOffset.UtcNow));
    }
}
