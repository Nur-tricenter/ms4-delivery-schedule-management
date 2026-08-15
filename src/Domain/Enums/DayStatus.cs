namespace delivery_schedule_management.Domain.Enums;

public enum DayStatus
{
    Pending,
    Scheduled,
    ReadyForProduction,
    InProduction,
    Delivered,
    Failed,
    Cancelled
}
