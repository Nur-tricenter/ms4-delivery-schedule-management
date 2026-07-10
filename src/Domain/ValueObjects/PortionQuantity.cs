namespace delivery_schedule_management.Domain.ValueObjects;

public sealed record PortionQuantity
{
    public int Value { get; }

    public PortionQuantity(int value)
    {
        if (value <= 0)
            throw new ArgumentOutOfRangeException(nameof(value), value, "Portion quantity must be greater than zero.");

        Value = value;
    }
}
