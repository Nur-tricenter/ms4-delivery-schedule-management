using delivery_schedule_management.Shared;

var builder = DistributedApplication.CreateBuilder(args);

var databaseServer = builder
    .AddPostgres(Services.DatabaseServer)
    .WithLifetime(ContainerLifetime.Persistent)
    .WithPgAdmin()
    .AddDatabase(Services.Database);

// Same dev-only signing key the Nur-Tricenter platform's identity uses locally, so a token minted by
// a local identity also works against a standalone MS4. Override "Parameters:jwt-secret" elsewhere.
var jwtSecret = builder.AddParameter("jwt-secret", secret: true);

builder.AddProject<Projects.Api>(Services.WebApi)
    .WithReference(databaseServer)
    .WaitFor(databaseServer)
    .WithEnvironment("Jwt__SecretKey", jwtSecret)
    .WithEnvironment("Catalog__UseMock", "true")
    .WithExternalHttpEndpoints()
    .WithAspNetCoreEnvironment()
    .WithUrlForEndpoint("http", url =>
    {
        url.DisplayText = "Scalar API Reference";
        url.Url = "/scalar";
    });

builder.Build().Run();
