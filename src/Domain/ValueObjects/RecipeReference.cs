namespace delivery_schedule_management.Domain.ValueObjects;

public sealed record RecipeReference
{
    public string Value { get; }

    public RecipeReference(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Recipe reference cannot be empty.", nameof(value));

        Value = value;
    }
}
