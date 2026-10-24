using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json.Serialization;
using delivery_schedule_management.Infrastructure.Data;
using delivery_schedule_management.Shared;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Mvc;
using Nur.Tricenter.Access.DeliveryCalendar;
using Nur.Tricenter.Security;

namespace Microsoft.Extensions.DependencyInjection;

public static class DependencyInjection
{
    public static void AddWebServices(this IHostApplicationBuilder builder)
    {
        builder.Services.AddDatabaseDeveloperPageExceptionFilter();

        builder.Services.AddHttpContextAccessor();

        builder.Services.AddExceptionHandler<ProblemDetailsExceptionHandler>();
        // Fallback ProblemDetails writer for exceptions ProblemDetailsExceptionHandler doesn't
        // recognise — without this, UseExceptionHandler re-executes the pipeline at a blank
        // path and blows up with a meta-exception instead of returning a clean 500.
        builder.Services.AddProblemDetails();

        builder.Services.ConfigureHttpJsonOptions(options =>
            options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

        // Customise default API behaviour
        builder.Services.Configure<ApiBehaviorOptions>(options =>
            options.SuppressModelStateInvalidFilter = true);

        builder.Services.AddEndpointsApiExplorer();

        builder.Services.AddOpenApi(options =>
        {
            options.AddDocumentTransformer<DocumentInfoTransformer>();
            options.AddOperationTransformer<ApiExceptionOperationTransformer>();
        });

        builder.AddPlatformSecurity();
        builder.AddPlatformServiceDiscovery();
    }

    /// <summary>
    /// JWT bearer validation against tokens minted by nur-tricenter-identity, plus one authorization
    /// policy per <see cref="DeliveryCalendarPermission"/> scope (Nur.Tricenter.Security kernel).
    /// </summary>
    /// <remarks>
    /// <c>Jwt:SecretKey</c>/<c>ValidIssuer</c>/<c>ValidAudience</c> must be byte-for-byte the values
    /// identity signs with. A mismatch is not a configuration error at startup — it is a 401 on every
    /// request, which reads like a gateway routing problem.
    /// </remarks>
    private static void AddPlatformSecurity(this IHostApplicationBuilder builder)
    {
        var jwtOptions = builder.Configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>() ?? new JwtOptions();

        // Build-time OpenAPI generation (Microsoft.Extensions.ApiDescription.Server) boots this host
        // only to read endpoint metadata, with no environment. It never validates a token, so a
        // throwaway key keeps the kernel's "no signing key" guard from failing the build. A real
        // run without Jwt__SecretKey still fails fast.
        if (string.IsNullOrWhiteSpace(jwtOptions.SecretKey)
            && Assembly.GetEntryAssembly()?.GetName().Name == "GetDocument.Insider")
        {
            jwtOptions.SecretKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48));
        }

        builder.Services.AddSecurity(jwtOptions, DeliveryCalendarPermission.PermissionsList);

        // The kernel's policies read the literal "scope" claim. With the default inbound mapping the
        // handler renames claims to WS-Federation URIs and every policy denies.
        builder.Services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme,
            options => options.MapInboundClaims = false);
    }

    /// <summary>
    /// Nur-Tricenter platform mode only (<c>ServiceRegistration:ServiceDiscoveryAddress</c> set):
    /// self-register in Consul so the gateway routes <c>/api/delivery-calendar/**</c> here, and make
    /// <c>/health</c> also probe Postgres so an instance without its database stops receiving traffic.
    /// </summary>
    private static void AddPlatformServiceDiscovery(this IHostApplicationBuilder builder)
    {
        if (!builder.Configuration.IsServiceDiscoveryEnabled())
            return;

        var connectionString = builder.Configuration.GetConnectionString(Services.Database)!;
        builder.Services.AddHealthChecks().AddNpgSql(connectionString, name: "postgres");
        builder.Services.AddConsulServiceRegistration(builder.Configuration);
    }
}
