namespace delivery_schedule_management.Infrastructure.Catalog;

/// <summary>Section <c>Catalog</c>: which <c>ICatalogPort</c> MS4 uses.</summary>
public class CatalogOptions
{
    public const string SectionName = "Catalog";

    /// <summary>
    /// True (default; standalone Aspire and tests) -> <see cref="MockCatalogPort"/>, a deterministic
    /// fixture. False -> <see cref="ProjectedCatalogPort"/>, MS4's projection of MS2's published meal
    /// plan compositions. MS4 never calls MS2 over HTTP.
    /// </summary>
    public bool UseMock { get; set; } = true;
}
