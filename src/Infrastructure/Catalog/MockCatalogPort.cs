using delivery_schedule_management.Application.Common.Interfaces;

namespace delivery_schedule_management.Infrastructure.Catalog;

/// <summary>
/// Stands in for the outbound call to MS2 (<c>GET /catalog/plans/{id}/day/{date}</c>), which is not
/// wired up yet. Returns deterministic fixture data so the daily resolution job and the
/// active-tomorrow flow are fully exercisable end-to-end without MS2 online.
/// </summary>
public class MockCatalogPort : ICatalogPort
{
    public Task<CatalogDayResolution> GetDayResolutionAsync(Guid planId, DateOnly date, CancellationToken cancellationToken)
    {
        var portions = (Math.Abs(planId.GetHashCode()) % 4) + 1;
        var recipeRef = $"recipe-{planId:N}-{date:yyyyMMdd}";

        return Task.FromResult(new CatalogDayResolution(recipeRef, portions));
    }
}
