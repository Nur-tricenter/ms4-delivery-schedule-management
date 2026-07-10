namespace delivery_schedule_management.Domain.ValueObjects;

public sealed record PlanDuration
{
    public int Days { get; }

    public PlanDuration(int days)
    {
        if (days is not (15 or 30))
            throw new ArgumentOutOfRangeException(nameof(days), days, "Plan duration must be either 15 or 30 days.");

        Days = days;
    }
}
