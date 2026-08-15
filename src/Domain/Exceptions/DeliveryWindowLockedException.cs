namespace delivery_schedule_management.Domain.Exceptions;

public class DeliveryWindowLockedException : Exception
{
    public DeliveryWindowLockedException(int dayId, DateOnly date)
        : base($"Delivery day {dayId} scheduled for {date:yyyy-MM-dd} cannot be modified within 48 hours of the delivery date.")
    {
    }
}
