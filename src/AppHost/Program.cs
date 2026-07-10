using delivery_schedule_management.Shared;

var builder = DistributedApplication.CreateBuilder(args);

var databaseServer = builder
    .AddPostgres(Services.DatabaseServer)
    .WithLifetime(ContainerLifetime.Persistent)
    .AddDatabase(Services.Database);

builder.AddProject<Projects.Web>(Services.WebApi)
    .WithReference(databaseServer)
    .WaitFor(databaseServer)
    .WithExternalHttpEndpoints()
    .WithAspNetCoreEnvironment()
    .WithUrlForEndpoint("http", url =>
    {
        url.DisplayText = "Scalar API Reference";
        url.Url = "/scalar";
    });

builder.Build().Run();
