namespace delivery_schedule_management.Infrastructure.Catalog;

/// <summary>Section <c>Catalog</c>: how MS4 reaches MS2.</summary>
public class CatalogOptions
{
    public const string SectionName = "Catalog";

    /// <summary>True → <see cref="MockCatalogPort"/> (standalone/tests). False → <see cref="HttpCatalogPort"/>.</summary>
    public bool UseMock { get; set; } = true;

    /// <summary>
    /// Base URL of the platform gateway (e.g. <c>http://gateway:8080</c>). MS4 never calls MS2
    /// directly: the gateway is the platform's single entry point and resolves MS2 from Consul.
    /// </summary>
    public string BaseUrl { get; set; } = string.Empty;

    /// <summary>Gateway path to one meal plan's full composition; <c>{0}</c> is the plan id.</summary>
    public string MealPlanPathTemplate { get; set; } = "/api/catalog/v1/meal-plans/{0}";
}

/// <summary>Section <c>Identity</c>: the service account MS4 uses to call MS2.</summary>
public class IdentityClientOptions
{
    public const string SectionName = "Identity";

    /// <summary>Base URL of the platform gateway (same as <see cref="CatalogOptions.BaseUrl"/>).</summary>
    public string BaseUrl { get; set; } = string.Empty;

    /// <summary>Identity registers in Consul as <c>identity-service</c>, hence this gateway path.</summary>
    public string LoginPath { get; set; } = "/api/identity-service/v1/auth/login";

    /// <summary>Service account with at least <c>catalog.mealPlan:read</c>.</summary>
    public string ServiceUserName { get; set; } = string.Empty;

    public string ServicePassword { get; set; } = string.Empty;
}
