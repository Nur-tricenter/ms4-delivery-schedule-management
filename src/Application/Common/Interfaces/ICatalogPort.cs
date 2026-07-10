namespace delivery_schedule_management.Application.Common.Interfaces;

/// <summary>
/// Outbound port to MS2 (<c>GET /catalog/plans/{id}/day/{date}</c>). Mocked in this stage
/// per CLAUDE.md — the Infrastructure adapter returns fixture data instead of calling MS2.
/// </summary>
public interface ICatalogPort
{
    Task<CatalogDayResolution> GetDayResolutionAsync(Guid planId, DateOnly date, CancellationToken cancellationToken);
}

public record CatalogDayResolution(string RecipeRef, int Portions);
