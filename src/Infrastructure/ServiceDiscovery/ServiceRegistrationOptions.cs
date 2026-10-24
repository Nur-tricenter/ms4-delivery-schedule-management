namespace delivery_schedule_management.Infrastructure.ServiceDiscovery;

/// <summary>
/// Consul self-registration settings (section <c>ServiceRegistration</c>), same keys as every other
/// Nur-Tricenter service.
/// </summary>
/// <remarks>
/// <see cref="ServiceName"/> is not cosmetic: the gateway publishes each Consul service at
/// <c>/api/{ServiceName}/**</c>, so it IS the public URL segment. MS4 registers as
/// <c>delivery-calendar</c>.
/// </remarks>
public class ServiceRegistrationOptions
{
    public const string SectionName = "ServiceRegistration";

    /// <summary>Consul agent URL. Blank turns registration off (standalone / Aspire mode).</summary>
    public string? ServiceDiscoveryAddress { get; set; }

    public string ServiceName { get; set; } = "delivery-calendar";

    /// <summary>Defaults to <see cref="ServiceName"/> when blank.</summary>
    public string? ServiceId { get; set; }

    /// <summary>Host name Consul (and the gateway) use to reach this instance, e.g. the compose hostname.</summary>
    public string Host { get; set; } = "localhost";

    public int Port { get; set; }

    public bool IsEnabled => !string.IsNullOrWhiteSpace(ServiceDiscoveryAddress);

    public string ResolveServiceId() =>
        string.IsNullOrWhiteSpace(ServiceId) ? ServiceName.Trim() : ServiceId.Trim();
}
