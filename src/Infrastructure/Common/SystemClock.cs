using delivery_schedule_management.Application.Common.Interfaces;

namespace delivery_schedule_management.Infrastructure.Common;

public class SystemClock(TimeProvider timeProvider) : IClock
{
    public DateTimeOffset Now => timeProvider.GetUtcNow();
}
