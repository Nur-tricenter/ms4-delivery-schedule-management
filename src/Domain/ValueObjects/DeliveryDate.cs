namespace delivery_schedule_management.Domain.ValueObjects;

public sealed record DeliveryDate
{
    public DateOnly Value { get; }

    public DeliveryDate(DateOnly value)
    {
        if (value == default)
            throw new ArgumentException("Delivery date must be a valid calendar date.", nameof(value));

        Value = value;
    }

    public DeliveryDate AddDays(int days) => new(Value.AddDays(days));
}
