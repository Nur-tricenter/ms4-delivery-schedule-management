using delivery_schedule_management.Domain.Entities;
using delivery_schedule_management.Domain.Enums;
using delivery_schedule_management.Domain.ValueObjects;
using NUnit.Framework;
using Shouldly;

namespace delivery_schedule_management.Domain.UnitTests.Entities;

public class DeliveryCalendarTests
{
    [TestCase(15)]
    [TestCase(30)]
    public void Create_ShouldGenerateOneDayPerDurationDay_AllPending(int durationDays)
    {
        var startDate = new DeliveryDate(new DateOnly(2026, 8, 1));

        var calendar = DeliveryCalendar.Create(
            Guid.NewGuid(),
            new ContractReference(Guid.NewGuid()),
            new PlanReference(Guid.NewGuid()),
            new PlanDuration(durationDays),
            startDate);

        calendar.Days.Count.ShouldBe(durationDays);
        calendar.Days.ShouldAllBe(day => day.Status == DayStatus.Pending);
        calendar.Status.ShouldBe(CalendarStatus.Active);

        var expectedDates = Enumerable.Range(0, durationDays).Select(startDate.AddDays);
        calendar.Days.Select(d => d.Date).ShouldBe(expectedDates);
    }
}
