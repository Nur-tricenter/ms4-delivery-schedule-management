using System.Data.Common;
using delivery_schedule_management.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Respawn;

namespace delivery_schedule_management.Infrastructure.IntegrationTests;

[SetUpFixture]
public class IntegrationTestSetup
{
    internal static string ConnectionString { get; private set; } = null!;

    private static DistributedApplication? _app;
    private static DbConnection? _respawnConnection;
    private static Respawner? _respawner;

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

        ConnectionString = (await _app.GetConnectionStringAsync(Services.Database))!;

        await using (var context = CreateContext())
        {
            await context.Database.MigrateAsync(cancellationToken);
        }

        _respawnConnection = new NpgsqlConnection(ConnectionString);
        await _respawnConnection.OpenAsync(cancellationToken);
        _respawner = await Respawner.CreateAsync(_respawnConnection);
    }

    [OneTimeTearDown]
    public async Task OneTimeTearDown()
    {
        if (_respawnConnection is not null) await _respawnConnection.DisposeAsync();
        if (_app is not null) await _app.DisposeAsync();
    }

    internal static ApplicationDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(ConnectionString)
            .Options;

        return new ApplicationDbContext(options);
    }

    internal static async Task ResetStateAsync()
    {
        if (_respawner is not null && _respawnConnection is not null)
        {
            await _respawner.ResetAsync(_respawnConnection);
        }
    }
}
