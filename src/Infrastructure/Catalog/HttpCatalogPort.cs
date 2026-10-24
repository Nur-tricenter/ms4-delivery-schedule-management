using System.Net;
using System.Net.Http.Json;
using delivery_schedule_management.Application.Common.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace delivery_schedule_management.Infrastructure.Catalog;

/// <summary>
/// Real MS2 adapter. Reads <c>GET /api/catalog/v1/meal-plans/{planId}</c> through the gateway
/// (MS2 policy <c>catalog.mealPlan:read</c>; the bearer token is attached by
/// <see cref="ServiceAccountTokenHandler"/>) and picks the requested plan day.
/// </summary>
/// <remarks>
/// MS2 has no per-day endpoint, so the whole composition is fetched once per plan and cached for
/// the lifetime of this instance — a typed-client instance lives for one daily-job run, where many
/// patients share a handful of plans.
/// </remarks>
public class HttpCatalogPort(
    HttpClient httpClient,
    IOptions<CatalogOptions> options,
    ILogger<HttpCatalogPort> logger) : ICatalogPort
{
    private readonly Dictionary<Guid, MealPlanCompositionDto?> _plans = new();

    public async Task<CatalogDayMenu?> GetDayMenuAsync(Guid planId, int dayNumber, CancellationToken cancellationToken)
    {
        var plan = await GetPlanAsync(planId, cancellationToken);

        var day = plan?.Days?.FirstOrDefault(d => d.DayNumber == dayNumber);
        if (day is null)
            return null;

        var items = (day.MealTimes ?? [])
            .SelectMany(mealTime => (mealTime.Recipes ?? []).Select(recipe => new CatalogMenuItem(
                mealTime.Name,
                mealTime.Order,
                recipe.RecipeId,
                recipe.RecipeName,
                recipe.Ration?.Quantity ?? 0)))
            .Where(item => item.Rations > 0)
            .ToList();

        return new CatalogDayMenu(items);
    }

    private async Task<MealPlanCompositionDto?> GetPlanAsync(Guid planId, CancellationToken cancellationToken)
    {
        if (_plans.TryGetValue(planId, out var cached))
            return cached;

        var path = string.Format(options.Value.MealPlanPathTemplate, planId);
        using var response = await httpClient.GetAsync(path, cancellationToken);

        MealPlanCompositionDto? plan = null;
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            logger.LogWarning("MS2 has no meal plan {PlanId}.", planId);
        }
        else
        {
            // Anything else non-2xx (401/403 from a misconfigured service account, 5xx) is a failure
            // the job must surface, not a plan that doesn't exist.
            response.EnsureSuccessStatusCode();
            var envelope = await response.Content.ReadFromJsonAsync<ApiResponse<MealPlanCompositionDto>>(cancellationToken);
            plan = envelope?.Data;
        }

        _plans[planId] = plan;
        return plan;
    }

    // MS2's wire shapes (Catalog.App ApiResponse<T> + MealPlanCompositionDto). Deliberately local
    // copies: they are a read model of another service's HTTP response, not a shared contract.
    private sealed record ApiResponse<T>(bool Success, string? Message, T? Data);

    private sealed record MealPlanCompositionDto(Guid MealPlanId, string Name, List<PlanDayDto>? Days);

    private sealed record PlanDayDto(int DayNumber, List<MealTimeDto>? MealTimes);

    private sealed record MealTimeDto(Guid MealTimeId, string Name, int Order, List<RecipeDto>? Recipes);

    private sealed record RecipeDto(Guid RecipeId, string RecipeName, RationDto? Ration);

    private sealed record RationDto(int Quantity);
}
