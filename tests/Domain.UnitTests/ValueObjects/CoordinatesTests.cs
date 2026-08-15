using delivery_schedule_management.Domain.ValueObjects;
using NUnit.Framework;
using Shouldly;

namespace delivery_schedule_management.Domain.UnitTests.ValueObjects;

public class CoordinatesTests
{
    [TestCase(-90, 0)]
    [TestCase(90, 0)]
    [TestCase(0, -180)]
    [TestCase(0, 180)]
    public void Constructor_ShouldAccept_BoundaryValues(double latitude, double longitude)
    {
        Should.NotThrow(() => new Coordinates(latitude, longitude));
    }

    [TestCase(-90.0001, 0)]
    [TestCase(90.0001, 0)]
    [TestCase(0, -180.0001)]
    [TestCase(0, 180.0001)]
    public void Constructor_ShouldThrow_WhenOutOfRange(double latitude, double longitude)
    {
        Should.Throw<ArgumentOutOfRangeException>(() => new Coordinates(latitude, longitude));
    }
}
