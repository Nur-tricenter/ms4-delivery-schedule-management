using delivery_schedule_management.Infrastructure.Messaging;
using OpenTelemetry.Trace;

// MS4's RabbitMQ consumer process (Nur-Tricenter platform mode). Projects MS2 events into MS4's
// database through an idempotent inbox; see Infrastructure/Messaging/MessagingExtensions.cs.
var builder = Host.CreateApplicationBuilder(args);

builder.AddServiceDefaults();
builder.Services.ConfigureOpenTelemetryTracerProvider(tracing => tracing.AddSource(MessagingTelemetry.ActivitySourceName));

// The DbContext's domain-event interceptor needs MediatR, so the application layer comes along.
builder.AddApplicationServices();
builder.AddInfrastructureServices();

builder.Services.AddPlatformMessaging(builder.Configuration);

builder.Build().Run();
