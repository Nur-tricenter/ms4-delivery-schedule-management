using delivery_schedule_management.Infrastructure.Data;
using Scalar.AspNetCore;

// `--migrate` is a bare switch, which the command-line configuration provider rejects ("unrecognized
// argument format"), so it is stripped before the host sees the arguments.
var migrateOnly = args.Any(a => string.Equals(a, "--migrate", StringComparison.OrdinalIgnoreCase));
args = args.Where(a => !string.Equals(a, "--migrate", StringComparison.OrdinalIgnoreCase)).ToArray();

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.AddServiceDefaults();

builder.AddApplicationServices();
builder.AddInfrastructureServices();
builder.AddWebServices();

var app = builder.Build();

// `--migrate`: one-shot job (Nur-Tricenter platform convention, see ms4.compose.yml). Applies EF
// Core migrations and exits 0/1 without starting the web host — so Consul registration and the HTTP
// listener never start for the job container.
if (migrateOnly)
{
    return await app.MigrateDatabaseAndExitAsync();
}

// Configure the HTTP request pipeline.
// Both default to on in Development (standalone/Aspire flow) and off elsewhere. On the platform the
// migrate job owns the schema, and seed data is opt-in (its plan ids don't exist in the real MS2).
await app.InitialiseDatabaseAsync(
    applyMigrations: app.Configuration.GetValue("Database:ApplyMigrationsOnStartup", app.Environment.IsDevelopment()),
    seed: app.Configuration.GetValue("Database:Seed", app.Environment.IsDevelopment()));

if (!app.Environment.IsDevelopment())
{
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

// Off on the platform: the container only listens on plain HTTP :8080 behind the gateway.
if (app.Configuration.GetValue("HttpsRedirection:Enabled", true))
{
    app.UseHttpsRedirection();
}

if (app.Configuration.GetValue("OpenApi:Enabled", true))
{
    app.MapOpenApi();
    app.MapScalarApiReference();
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/openapi/v1.json", "MS4 — Delivery Calendar v1");
        options.RoutePrefix = "swagger";
    });
}

app.UseExceptionHandler(options => { });

app.UseAuthentication();
app.UseAuthorization();

app.MapDefaultEndpoints();
app.MapEndpoints(typeof(Program).Assembly);

app.Run();

return 0;
