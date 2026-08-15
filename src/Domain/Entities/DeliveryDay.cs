using delivery_schedule_management.Domain.Common;
using delivery_schedule_management.Domain.Enums;
using delivery_schedule_management.Domain.Exceptions;
using delivery_schedule_management.Domain.ValueObjects;

namespace delivery_schedule_management.Domain.Entities;

public class DeliveryDay : BaseEntity
{
    public DeliveryDate Date { get; private set; } = null!;

    public DayStatus Status { get; private set; }

    public DeliveryAddress? Address { get; private set; }

    public TimeOnly? DeliveryHour { get; private set; }

    public RecipeReference? RecipeRef { get; private set; }

    public PortionQuantity? Portions { get; private set; }

    public DeliveryEvidence? Evidence { get; private set; }

    private DeliveryDay() { } // EF Core

    internal DeliveryDay(DeliveryDate date)
    {
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

    public void MarkReadyForProduction(RecipeReference recipeRef, PortionQuantity portions)
    {
        if (Status != DayStatus.Scheduled)
            throw new InvalidDeliveryDayTransitionException(Id, Status, "be marked ready for production");

        RecipeRef = recipeRef;
        Portions = portions;
        Status = DayStatus.ReadyForProduction;
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
