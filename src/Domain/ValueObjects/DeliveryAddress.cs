namespace delivery_schedule_management.Domain.ValueObjects;

/// <summary>
/// Coordinates are embedded here (rather than kept as a sibling field on <c>DeliveryDay</c>)
/// because endpoint #4 (<c>POST /calendars/{id}/addresses</c>) always submits address and
/// lat/long together as a single payload.
/// </summary>
public sealed record DeliveryAddress
{
    public string Street { get; private set; } = null!;

    public string City { get; private set; } = null!;

    public string? References { get; private set; }

    public string? Notes { get; private set; }

    public Coordinates Coordinates { get; private set; } = null!;

    private DeliveryAddress() { } // EF Core

    public DeliveryAddress(string street, string city, Coordinates coordinates, string? references = null, string? notes = null)
    {
        if (string.IsNullOrWhiteSpace(street))
            throw new ArgumentException("Street is required.", nameof(street));

        if (string.IsNullOrWhiteSpace(city))
            throw new ArgumentException("City is required.", nameof(city));

        Street = street;
        City = city;
        Coordinates = coordinates;
        References = references;
        Notes = notes;
    }
}
