namespace delivery_schedule_management.Domain.ValueObjects;

/// <summary>
/// One recipe the patient receives on a delivery day, as resolved from the MS2 meal plan:
/// which meal time it belongs to, which recipe, and how many rations.
/// </summary>
/// <remarks>
/// <see cref="RecipeName"/> is a snapshot taken at resolution time (refreshed when MS2 publishes
/// <c>RecipeUpdated</c>), so reading a calendar never needs a round trip to MS2.
/// <para>
/// Private parameterless ctor + private setters instead of a positional record: EF Core maps this as
/// an owned collection, and owned types need a bindable shape (see CLAUDE.md §5 gotchas).
/// </para>
/// </remarks>
public sealed class MenuItem : IEquatable<MenuItem>
{
    public string MealTime { get; private set; } = null!;

    /// <summary>Position of the meal time within the day (breakfast before lunch, ...).</summary>
    public int MealTimeOrder { get; private set; }

    public Guid RecipeId { get; private set; }

    public string RecipeName { get; private set; } = null!;

    public int Rations { get; private set; }

    private MenuItem() { } // EF Core

    public MenuItem(string mealTime, int mealTimeOrder, Guid recipeId, string recipeName, int rations)
    {
        if (string.IsNullOrWhiteSpace(mealTime))
            throw new ArgumentException("Meal time cannot be empty.", nameof(mealTime));

        if (recipeId == Guid.Empty)
            throw new ArgumentException("Recipe id cannot be empty.", nameof(recipeId));

        if (string.IsNullOrWhiteSpace(recipeName))
            throw new ArgumentException("Recipe name cannot be empty.", nameof(recipeName));

        if (rations <= 0)
            throw new ArgumentOutOfRangeException(nameof(rations), rations, "Rations must be greater than zero.");

        MealTime = mealTime;
        MealTimeOrder = mealTimeOrder;
        RecipeId = recipeId;
        RecipeName = recipeName;
        Rations = rations;
    }

    public bool Equals(MenuItem? other) =>
        other is not null
        && MealTime == other.MealTime
        && MealTimeOrder == other.MealTimeOrder
        && RecipeId == other.RecipeId
        && RecipeName == other.RecipeName
        && Rations == other.Rations;

    public override bool Equals(object? obj) => Equals(obj as MenuItem);

    public override int GetHashCode() => HashCode.Combine(MealTime, MealTimeOrder, RecipeId, RecipeName, Rations);
}
