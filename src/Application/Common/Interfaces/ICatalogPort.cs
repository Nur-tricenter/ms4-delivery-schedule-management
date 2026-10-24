namespace delivery_schedule_management.Application.Common.Interfaces;

/// <summary>
/// Outbound port to MS2 (ms2-plans-and-recipes-catalog). Resolves what a patient eats on plan day
/// <c>dayNumber</c> of meal plan <c>planId</c>.
/// </summary>
/// <remarks>
/// MS2 models plans by day NUMBER (1..N), not by calendar date, so the caller passes
/// <c>DeliveryDay.DayNumber</c>. The real adapter reads <c>GET /api/catalog/v1/meal-plans/{planId}</c>
/// through the platform gateway; a mock adapter returns a deterministic fixture.
/// <para>
/// Contract: returns <c>null</c> when MS2 has no such plan or day (nothing to produce — the day is
/// left as is); throws when MS2 cannot be reached, so the caller can retry that day later.
/// </para>
/// </remarks>
public interface ICatalogPort
{
    Task<CatalogDayMenu?> GetDayMenuAsync(Guid planId, int dayNumber, CancellationToken cancellationToken);
}

public record CatalogDayMenu(IReadOnlyList<CatalogMenuItem> Items);

public record CatalogMenuItem(string MealTime, int MealTimeOrder, Guid RecipeId, string RecipeName, int Rations);
