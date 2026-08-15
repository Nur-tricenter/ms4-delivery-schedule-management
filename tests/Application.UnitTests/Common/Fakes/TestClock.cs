using delivery_schedule_management.Application.Common.Interfaces;

namespace delivery_schedule_management.Application.UnitTests.Common.Fakes;

public class TestClock(DateTimeOffset now) : IClock
{
    public DateTimeOffset Now { get; set; } = now;
}
