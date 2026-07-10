using delivery_schedule_management.Domain.ValueObjects;
using NUnit.Framework;
using Shouldly;

namespace delivery_schedule_management.Domain.UnitTests.ValueObjects;

public class PlanDurationTests
{
    [TestCase(15)]
    [TestCase(30)]
    public void Constructor_ShouldAccept_ValidDurations(int days)
    {
        Should.NotThrow(() => new PlanDuration(days));
    }

    [TestCase(0)]
    [TestCase(7)]
    [TestCase(31)]
    public void Constructor_ShouldThrow_ForUnsupportedDurations(int days)
    {
        Should.Throw<ArgumentOutOfRangeException>(() => new PlanDuration(days));
    }
}
