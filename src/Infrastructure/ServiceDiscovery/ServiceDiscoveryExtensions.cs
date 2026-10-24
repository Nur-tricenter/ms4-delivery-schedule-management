using Consul;
using delivery_schedule_management.Infrastructure.ServiceDiscovery;
using Microsoft.Extensions.Configuration;

namespace Microsoft.Extensions.DependencyInjection;

public static class ServiceDiscoveryExtensions
{
    public static bool IsServiceDiscoveryEnabled(this IConfiguration configuration) =>
        !string.IsNullOrWhiteSpace(
            configuration[$"{ServiceRegistrationOptions.SectionName}:{nameof(ServiceRegistrationOptions.ServiceDiscoveryAddress)}"]);

    /// <summary>
    /// Registers this instance in Consul when <c>ServiceRegistration:ServiceDiscoveryAddress</c> is
    /// set (Nur-Tricenter platform mode). A no-op otherwise, so standalone Aspire runs are untouched.
    /// </summary>
    public static IServiceCollection AddConsulServiceRegistration(this IServiceCollection services, IConfiguration configuration)
    {
        if (!configuration.IsServiceDiscoveryEnabled())
            return services;

        // Build the client against the CONFIGURED agent: Consul's default (127.0.0.1:8500) is this
        // container's own loopback, where no agent listens.
        var address = new Uri(configuration[$"{ServiceRegistrationOptions.SectionName}:{nameof(ServiceRegistrationOptions.ServiceDiscoveryAddress)}"]!.Trim());

        services.Configure<ServiceRegistrationOptions>(configuration.GetSection(ServiceRegistrationOptions.SectionName));
        services.AddSingleton<IConsulClient>(_ => new ConsulClient(cfg => cfg.Address = address));
        services.AddHostedService<ConsulServiceRegistration>();

        return services;
    }
}
