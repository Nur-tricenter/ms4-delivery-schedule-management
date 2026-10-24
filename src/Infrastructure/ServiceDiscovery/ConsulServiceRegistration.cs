using Consul;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace delivery_schedule_management.Infrastructure.ServiceDiscovery;

/// <summary>
/// Registers this instance in Consul on startup (with an HTTP check on <c>/health</c>) and
/// deregisters it on shutdown. Mirrors the reference implementation in ms2-plans-and-recipes-catalog.
/// </summary>
/// <remarks>
/// Registration never fails startup: if Consul is unreachable after a few attempts the service keeps
/// running and is reachable directly — only gateway discovery is degraded. Deregistration is
/// best-effort too; an instance killed without deregistering is reaped by Consul after
/// <see cref="DeregisterCriticalServiceAfter"/>.
/// </remarks>
public sealed class ConsulServiceRegistration : IHostedService
{
    public const string HealthCheckPath = "/health";

    public static readonly TimeSpan HealthCheckInterval = TimeSpan.FromSeconds(10);
    public static readonly TimeSpan HealthCheckTimeout = TimeSpan.FromSeconds(5);
    public static readonly TimeSpan DeregisterCriticalServiceAfter = TimeSpan.FromSeconds(30);

    private const int MaxAttempts = 3;
    private static readonly TimeSpan RetryBaseDelay = TimeSpan.FromMilliseconds(500);

    private readonly IConsulClient _consul;
    private readonly ServiceRegistrationOptions _options;
    private readonly ILogger<ConsulServiceRegistration> _logger;
    private bool _registered;

    public ConsulServiceRegistration(
        IConsulClient consul,
        IOptions<ServiceRegistrationOptions> options,
        ILogger<ConsulServiceRegistration> logger)
    {
        _consul = consul;
        _options = options.Value;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (_options.Port <= 0)
        {
            _logger.LogWarning(
                "'{Section}:Port' is not set, so {ServiceName} is not registering itself in Consul.",
                ServiceRegistrationOptions.SectionName, _options.ServiceName);
            return;
        }

        var registration = BuildRegistration();

        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            try
            {
                await _consul.Agent.ServiceRegister(registration, cancellationToken);
                _registered = true;
                _logger.LogInformation(
                    "Registered {ServiceId} ({ServiceName}) in Consul with an HTTP check on {HealthUrl}.",
                    registration.ID, registration.Name, registration.Check.HTTP);
                return;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Attempt {Attempt} of {MaxAttempts} to register {ServiceId} in Consul failed.",
                    attempt, MaxAttempts, registration.ID);
            }

            if (attempt < MaxAttempts)
            {
                try
                {
                    await Task.Delay(RetryBaseDelay * Math.Pow(2, attempt - 1), cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
        }

        _logger.LogWarning(
            "Could not register {ServiceId} in {DiscoveryAddress} after {MaxAttempts} attempts. The service keeps " +
            "running; only gateway discovery is degraded.",
            registration.ID, _options.ServiceDiscoveryAddress, MaxAttempts);
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (!_registered)
            return;

        try
        {
            await _consul.Agent.ServiceDeregister(_options.ResolveServiceId(), cancellationToken);
            _registered = false;
            _logger.LogInformation("Deregistered {ServiceId} from Consul.", _options.ResolveServiceId());
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not deregister {ServiceId} from Consul; it will be evicted after {Eviction}.",
                _options.ResolveServiceId(), DeregisterCriticalServiceAfter);
        }
    }

    private AgentServiceRegistration BuildRegistration() => new()
    {
        ID = _options.ResolveServiceId(),
        Name = _options.ServiceName,
        Address = _options.Host,
        Port = _options.Port,
        Check = new AgentServiceCheck
        {
            HTTP = $"http://{_options.Host}:{_options.Port}{HealthCheckPath}",
            Interval = HealthCheckInterval,
            Timeout = HealthCheckTimeout,
            DeregisterCriticalServiceAfter = DeregisterCriticalServiceAfter
        }
    };
}
