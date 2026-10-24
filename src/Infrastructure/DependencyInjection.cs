using delivery_schedule_management.Application.Common.Interfaces;
using delivery_schedule_management.Infrastructure.Catalog;
using delivery_schedule_management.Infrastructure.Common;
using delivery_schedule_management.Infrastructure.Data;
using delivery_schedule_management.Infrastructure.Data.Interceptors;
using delivery_schedule_management.Infrastructure.Data.Repositories;
using delivery_schedule_management.Infrastructure.Messaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace Microsoft.Extensions.DependencyInjection;

public static class DependencyInjection
{
    public static void AddInfrastructureServices(this IHostApplicationBuilder builder)
    {
        var connectionString = builder.Configuration.GetConnectionString(Services.Database);
        Guard.Against.Null(connectionString, message: $"Connection string '{Services.Database}' not found.");

        builder.Services.AddScoped<ISaveChangesInterceptor, AuditableEntityInterceptor>();
        builder.Services.AddScoped<ISaveChangesInterceptor, DispatchDomainEventsInterceptor>();

        builder.Services.AddDbContext<ApplicationDbContext>((sp, options) =>
        {
            options.AddInterceptors(sp.GetServices<ISaveChangesInterceptor>());
            options.UseNpgsql(connectionString);
            options.ConfigureWarnings(warnings => warnings.Ignore(RelationalEventId.PendingModelChangesWarning));
        });

        builder.EnrichNpgsqlDbContext<ApplicationDbContext>();

        builder.Services.AddScoped<IApplicationDbContext>(provider => provider.GetRequiredService<ApplicationDbContext>());

        builder.Services.AddScoped<ApplicationDbContextInitialiser>();

        builder.Services.AddSingleton(TimeProvider.System);

        builder.Services.AddScoped<ICalendarRepository, CalendarRepository>();
        builder.Services.AddScoped<IProductionOrderRepository, ProductionOrderRepository>();
        builder.Services.AddScoped<IClock, SystemClock>();
        builder.Services.AddScoped<IPlanCatalog, PlanCatalog>();
        builder.AddCatalogPort();
    }

    /// <summary>
    /// <c>Catalog:UseMock=true</c> (default; standalone Aspire and tests) → deterministic fixture.
    /// Otherwise the real MS2 adapter, through the platform gateway, authenticated as MS4's service
    /// account. Resilience (retry/timeout/circuit breaker) comes from ServiceDefaults'
    /// <c>ConfigureHttpClientDefaults</c>, applied to every HttpClient.
    /// </summary>
    private static void AddCatalogPort(this IHostApplicationBuilder builder)
    {
        builder.Services.Configure<CatalogOptions>(builder.Configuration.GetSection(CatalogOptions.SectionName));
        builder.Services.Configure<IdentityClientOptions>(builder.Configuration.GetSection(IdentityClientOptions.SectionName));

        var catalog = builder.Configuration.GetSection(CatalogOptions.SectionName).Get<CatalogOptions>() ?? new CatalogOptions();
        if (catalog.UseMock)
        {
            builder.Services.AddSingleton<ICatalogPort, MockCatalogPort>();
            return;
        }

        Guard.Against.NullOrWhiteSpace(catalog.BaseUrl, message: "Catalog:BaseUrl is required when Catalog:UseMock is false.");
        var identity = builder.Configuration.GetSection(IdentityClientOptions.SectionName).Get<IdentityClientOptions>() ?? new IdentityClientOptions();
        Guard.Against.NullOrWhiteSpace(identity.BaseUrl, message: "Identity:BaseUrl is required when Catalog:UseMock is false.");

        builder.Services.AddHttpClient(ServiceAccountTokenProvider.HttpClientName,
            client => client.BaseAddress = new Uri(identity.BaseUrl));
        builder.Services.AddSingleton<ServiceAccountTokenProvider>();
        builder.Services.AddTransient<ServiceAccountTokenHandler>();

        builder.Services.AddHttpClient<ICatalogPort, HttpCatalogPort>(client => client.BaseAddress = new Uri(catalog.BaseUrl))
            .AddHttpMessageHandler<ServiceAccountTokenHandler>();
    }
}
