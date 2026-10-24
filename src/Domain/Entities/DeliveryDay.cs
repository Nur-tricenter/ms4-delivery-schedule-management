using delivery_schedule_management.Domain.Common;
using delivery_schedule_management.Domain.Enums;
using delivery_schedule_management.Domain.Exceptions;
using delivery_schedule_management.Domain.ValueObjects;

namespace delivery_schedule_management.Domain.Entities;

public class DeliveryDay : BaseEntity
{
    private readonly List<MenuItem> _menu = new();

    /// <summary>
    /// 1-based position of this day in the plan (MS2's <c>PlanDay.DayNumber</c>). Fixed at creation:
    /// rescheduling moves <see cref="Date"/> but the patient still receives plan day N's menu.
    /// </summary>
    public int DayNumber { get; private set; }

    public DeliveryDate Date { get; private set; } = null!;

    public DayStatus Status { get; private set; }

    public DeliveryAddress? Address { get; private set; }

    public TimeOnly? DeliveryHour { get; private set; }

    /// <summary>Recipes and rations for this day, resolved from MS2 by the daily catalog job.</summary>
    public IReadOnlyCollection<MenuItem> Menu => _menu.AsReadOnly();

    /// <summary>Total rations across <see cref="Menu"/>; null until the day is resolved.</summary>
    public PortionQuantity? Portions { get; private set; }

    public DeliveryEvidence? Evidence { get; private set; }

    private DeliveryDay() { } // EF Core

    internal DeliveryDay(int dayNumber, DeliveryDate date)
    {
        if (dayNumber <= 0)
            throw new ArgumentOutOfRangeException(nameof(dayNumber), dayNumber, "Day number must be greater than zero.");

        DayNumber = dayNumber;
        Date = date;
        Status = DayStatus.Pending;
    }

    public void SetAddress(DeliveryAddress address)
    {
        if (Status is not (DayStatus.Pending or DayStatus.Scheduled))
            throw new InvalidDeliveryDayTransitionException(Id, Status, "set the delivery address");

        Address = address;
        Status = DayStatus.Scheduled;
    }

    public void MarkReadyForProduction(IReadOnlyCollection<MenuItem> menu)
    {
        if (Status != DayStatus.Scheduled)
            throw new InvalidDeliveryDayTransitionException(Id, Status, "be marked ready for production");

        if (menu.Count == 0)
            throw new ArgumentException("A day cannot go to production with an empty menu.", nameof(menu));

        _menu.Clear();
        _menu.AddRange(menu);
        Portions = new PortionQuantity(menu.Sum(item => item.Rations));
        Status = DayStatus.ReadyForProduction;
    }

    /// <summary>
    /// Refreshes the recipe-name snapshot after MS2 publishes <c>RecipeUpdated</c>. Only for days not
    /// yet closed — a delivered/failed/cancelled day keeps the name it was produced with.
    /// </summary>
    public bool RenameRecipe(Guid recipeId, string recipeName)
    {
        if (Status is DayStatus.Delivered or DayStatus.Failed or DayStatus.Cancelled)
            return false;

        var changed = false;
        for (var i = 0; i < _menu.Count; i++)
        {
            var item = _menu[i];
            if (item.RecipeId != recipeId || item.RecipeName == recipeName)
                continue;

            _menu[i] = new MenuItem(item.MealTime, item.MealTimeOrder, item.RecipeId, recipeName, item.Rations);
            changed = true;
        }

        return changed;
    }

    public void Reschedule(DeliveryDate newDate, DateTimeOffset now)
    {
        EnsureOutsideLockWindow(now);

        if (Status is not (DayStatus.Pending or DayStatus.Scheduled))
            throw new InvalidDeliveryDayTransitionException(Id, Status, "be rescheduled");

        Date = newDate;
    }

    public void Cancel(DateTimeOffset now)
    {
        EnsureOutsideLockWindow(now);

        if (Status is DayStatus.Delivered or DayStatus.Failed or DayStatus.Cancelled or DayStatus.InProduction)
            throw new InvalidDeliveryDayTransitionException(Id, Status, "be cancelled");

        Status = DayStatus.Cancelled;
    }

    public void ApplyDeliveryResult(DeliveryResult result, DeliveryEvidence? evidence, DateTimeOffset now)
    {
        var targetStatus = result == DeliveryResult.Delivered ? DayStatus.Delivered : DayStatus.Failed;

        if (Status == targetStatus)
            return; // idempotent: same result reported twice is a no-op

        if (Status is not (DayStatus.ReadyForProduction or DayStatus.InProduction))
            throw new InvalidDeliveryDayTransitionException(Id, Status, "receive a delivery result");

        Status = targetStatus;
        Evidence = evidence;
    }

    private void EnsureOutsideLockWindow(DateTimeOffset now)
    {
        var deliveryInstant = new DateTimeOffset(Date.Value.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);

        if (deliveryInstant - now < TimeSpan.FromHours(48))
            throw new DeliveryWindowLockedException(Id, Date.Value);
    }
}
