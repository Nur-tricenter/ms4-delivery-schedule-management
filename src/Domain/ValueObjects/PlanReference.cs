namespace delivery_schedule_management.Domain.ValueObjects;

public sealed record PlanReference
{
    public Guid Value { get; }

    public PlanReference(Guid value)
    {
        if (value == Guid.Empty)
            throw new ArgumentException("Plan reference cannot be empty.", nameof(value));

        Value = value;
    }
}
