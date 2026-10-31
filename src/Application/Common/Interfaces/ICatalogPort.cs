namespace delivery_schedule_management.Application.Common.Interfaces;

/// <summary>
/// Resolves what a patient eats on plan day <c>dayNumber</c> of meal plan <c>planId</c>, from MS2's
/// meal plan composition.
/// </summary>
/// <remarks>
/// MS2 models plans by day NUMBER (1..N), not by calendar date, so the caller passes
/// <c>DeliveryDay.DayNumber</c>. The real adapter reads MS4's own projection of MS2's
/// <c>MealPlanCompositionChanged</c> events (no call to MS2); a mock adapter returns a deterministic
/// fixture.
/// <para>
/// Contract: returns <c>null</c> when no composition is known for that plan or day (nothing to
/// produce — the day is left as is and picked up by a later run once the projection has it).
/// </para>
/// </remarks>
public interface ICatalogPort
{
    Task<CatalogDayMenu?> GetDayMenuAsync(Guid planId, int dayNumber, CancellationToken cancellationToken);
}

public record CatalogDayMenu(IReadOnlyList<CatalogMenuItem> Items);

public record CatalogMenuItem(string MealTime, int MealTimeOrder, Guid RecipeId, string RecipeName, int Rations);
