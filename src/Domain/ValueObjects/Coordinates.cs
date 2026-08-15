namespace delivery_schedule_management.Domain.ValueObjects;

public sealed record Coordinates
{
    public double Latitude { get; }

    public double Longitude { get; }

    public Coordinates(double latitude, double longitude)
    {
        if (latitude is < -90 or > 90)
            throw new ArgumentOutOfRangeException(nameof(latitude), latitude, "Latitude must be between -90 and 90 degrees.");

        if (longitude is < -180 or > 180)
            throw new ArgumentOutOfRangeException(nameof(longitude), longitude, "Longitude must be between -180 and 180 degrees.");

        Latitude = latitude;
        Longitude = longitude;
    }
}
