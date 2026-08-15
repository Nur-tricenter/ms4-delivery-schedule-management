using delivery_schedule_management.Domain.Enums;

namespace delivery_schedule_management.Domain.Exceptions;

public class InvalidDeliveryDayTransitionException : Exception
{
    public InvalidDeliveryDayTransitionException(int dayId, DayStatus currentStatus, string attemptedAction)
        : base($"Delivery day {dayId} in status '{currentStatus}' cannot {attemptedAction}.")
    {
    }
}
