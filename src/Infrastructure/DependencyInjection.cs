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
        builder.Services.AddScoped<IDeliveryRouteLinks, DeliveryRouteLinks>();
        builder.Services.AddScoped<IProductionOrderEvents, ProductionOrderEvents>();
        builder.AddCatalogPort();
    }

    /// <summary>
    /// <c>Catalog:UseMock=true</c> (default; standalone Aspire and tests) → deterministic fixture.
    /// Otherwise the projection of MS2's meal plan compositions (<see cref="ProjectedCatalogPort"/>).
    /// </summary>
    private static void AddCatalogPort(this IHostApplicationBuilder builder)
    {
        builder.Services.Configure<CatalogOptions>(builder.Configuration.GetSection(CatalogOptions.SectionName));

        var catalog = builder.Configuration.GetSection(CatalogOptions.SectionName).Get<CatalogOptions>() ?? new CatalogOptions();
        if (catalog.UseMock)
        {
            builder.Services.AddSingleton<ICatalogPort, MockCatalogPort>();
            return;
        }

        builder.Services.AddScoped<ICatalogPort, ProjectedCatalogPort>();
    }
}
