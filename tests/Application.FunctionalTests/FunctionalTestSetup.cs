using Microsoft.Extensions.DependencyInjection;
using Nur.Tricenter.Access.DeliveryCalendar;

namespace delivery_schedule_management.Application.FunctionalTests;

[SetUpFixture]
public class FunctionalTestSetup
{
    internal static IServiceScopeFactory ScopeFactory { get; private set; } = null!;
    internal static DatabaseResetter? DbResetter { get; private set; }

    private static WebApiFactory? _factory;
    private static DistributedApplication? _app;

    /// <summary>An HTTP client carrying a bearer token with every MS4 scope.</summary>
    internal static HttpClient CreateHttpClient() =>
        CreateHttpClient(DeliveryCalendarPermission.PermissionsList.ToArray());

    /// <summary>An HTTP client carrying a bearer token with exactly <paramref name="scopes"/>.</summary>
    internal static HttpClient CreateHttpClient(params string[] scopes)
    {
        var client = CreateAnonymousHttpClient();
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", TestTokens.Create(scopes));
        return client;
    }

    /// <summary>An HTTP client with no Authorization header.</summary>
    internal static HttpClient CreateAnonymousHttpClient() =>
        (_factory ?? throw new InvalidOperationException("WebApiFactory not initialised.")).CreateClient();

    [OneTimeSetUp]
    public async Task OneTimeSetUp()
    {
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var cancellationToken = cts.Token;

        var builder = await DistributedApplicationTestingBuilder
            .CreateAsync<Projects.TestAppHost>(
                args: [],
                configureBuilder: (options, _) =>
                {
                    options.DisableDashboard = true;
                });

        builder.Configuration["ASPIRE_ALLOW_UNSECURED_TRANSPORT"] = "true";

        _app = await builder
            .BuildAsync(cancellationToken)
            .WaitAsync(cancellationToken);

        await _app
            .StartAsync(cancellationToken)
            .WaitAsync(cancellationToken);

        await _app.ResourceNotifications.WaitForResourceHealthyAsync(
            Services.Database, cancellationToken);

        var connectionString = (await _app.GetConnectionStringAsync(Services.Database))!;

        _factory = new WebApiFactory(connectionString);
        ScopeFactory = _factory.Services.GetRequiredService<IServiceScopeFactory>();
        DbResetter = await DatabaseResetter.CreateAsync(connectionString);
    }

    [OneTimeTearDown]
    public async Task OneTimeTearDown()
    {
        if (DbResetter is not null) await DbResetter.DisposeAsync();
        if (_app is not null) await _app.DisposeAsync();
        if (_factory is not null) await _factory.DisposeAsync();
    }
}
