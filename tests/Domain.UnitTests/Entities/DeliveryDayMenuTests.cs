using delivery_schedule_management.Domain.Entities;
using delivery_schedule_management.Domain.Enums;
using delivery_schedule_management.Domain.ValueObjects;
using NUnit.Framework;
using Shouldly;

namespace delivery_schedule_management.Domain.UnitTests.Entities;

public class DeliveryDayMenuTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 1, 0, 0, 0, TimeSpan.Zero);

    private static DeliveryCalendar NewCalendar() => DeliveryCalendar.Create(
        Guid.NewGuid(),
        new ContractReference(Guid.NewGuid()),
        new PlanReference(Guid.NewGuid()),
        new PlanDuration(15),
        new DeliveryDate(new DateOnly(2026, 8, 10)));

    private static DeliveryDay ScheduledDay()
    {
        var day = NewCalendar().Days.First();
        day.SetAddress(new DeliveryAddress("Main St 1", "Springfield", new Coordinates(1, 1)));
        return day;
    }

    [Test]
    public void Create_ShouldNumberDaysOneToN_InPlanOrder()
    {
        NewCalendar().Days.Select(d => d.DayNumber).ShouldBe(Enumerable.Range(1, 15));
    }

    [Test]
    public void Reschedule_ShouldKeepDayNumber_SoThePatientStillGetsThatPlanDaysMenu()
    {
        var day = NewCalendar().Days.ElementAt(2);

        day.Reschedule(new DeliveryDate(new DateOnly(2026, 9, 30)), Now);

        day.DayNumber.ShouldBe(3);
    }

    [Test]
    public void MarkReadyForProduction_ShouldStoreMenu_AndSumRations()
    {
        var day = ScheduledDay();

        day.MarkReadyForProduction(
        [
            new MenuItem("Breakfast", 1, Guid.NewGuid(), "Oats", 1),
            new MenuItem("Lunch", 3, Guid.NewGuid(), "Quinoa bowl", 2)
        ]);

        day.Status.ShouldBe(DayStatus.ReadyForProduction);
        day.Menu.Count.ShouldBe(2);
        day.Portions!.Value.ShouldBe(3);
    }

    [Test]
    public void MarkReadyForProduction_ShouldReject_EmptyMenu()
    {
        Should.Throw<ArgumentException>(() => ScheduledDay().MarkReadyForProduction([]));
    }

    [Test]
    public void RenameRecipe_ShouldRefreshSnapshot_OnOpenDays()
    {
        var recipeId = Guid.NewGuid();
        var day = ScheduledDay();
        day.MarkReadyForProduction([new MenuItem("Lunch", 3, recipeId, "Old name", 2)]);

        day.RenameRecipe(recipeId, "New name").ShouldBeTrue();

        day.Menu.Single().RecipeName.ShouldBe("New name");
        day.Menu.Single().Rations.ShouldBe(2);
    }

    [Test]
    public void RenameRecipe_ShouldNotTouch_ClosedDays()
    {
        var recipeId = Guid.NewGuid();
        var day = ScheduledDay();
        day.MarkReadyForProduction([new MenuItem("Lunch", 3, recipeId, "Old name", 2)]);
        day.ApplyDeliveryResult(DeliveryResult.Delivered, null, Now);

        day.RenameRecipe(recipeId, "New name").ShouldBeFalse();

        day.Menu.Single().RecipeName.ShouldBe("Old name");
    }

    [TestCase(0)]
    [TestCase(-1)]
    public void MenuItem_ShouldReject_NonPositiveRations(int rations)
    {
        Should.Throw<ArgumentOutOfRangeException>(() => new MenuItem("Lunch", 3, Guid.NewGuid(), "Soup", rations));
    }
}
