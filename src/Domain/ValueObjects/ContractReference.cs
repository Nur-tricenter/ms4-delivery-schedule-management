namespace delivery_schedule_management.Domain.ValueObjects;

public sealed record ContractReference
{
    public Guid Value { get; }

    public ContractReference(Guid value)
    {
        if (value == Guid.Empty)
            throw new ArgumentException("Contract reference cannot be empty.", nameof(value));

        Value = value;
    }
}
