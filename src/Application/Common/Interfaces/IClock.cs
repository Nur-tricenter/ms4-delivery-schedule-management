namespace delivery_schedule_management.Application.Common.Interfaces;

public interface IClock
{
    DateTimeOffset Now { get; }
}
