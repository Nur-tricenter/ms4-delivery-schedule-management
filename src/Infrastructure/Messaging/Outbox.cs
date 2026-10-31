using delivery_schedule_management.Application.Common.Interfaces;
using delivery_schedule_management.Domain.Entities;
using delivery_schedule_management.Infrastructure.Data;
using Joseco.Communication.External.Contracts.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Nur.Tricenter.Integration.Messages.DeliveryCalendar;

namespace delivery_schedule_management.Infrastructure.Messaging;

/// <summary>
/// One integration message MS4 still has to publish. Written in the same <c>SaveChanges</c> as the
/// change that caused it, so the change and its event are committed or lost together; the worker's
/// <see cref="OutboxDispatcher"/> publishes it afterwards and stamps <see cref="PublishedAt"/>.
/// </summary>
/// <remarks>
/// A minimal own outbox instead of <c>Joseco.Outbox.EFCore</c>: that package pins MediatR 12 and EF
/// Core 9, while MS4 runs MediatR 14 and EF Core 10.
/// </remarks>
public class OutboxMessage
{
    private OutboxMessage() { } // EF Core

    public OutboxMessage(string type, string destination, string payload, DateTimeOffset createdAt)
    {
        Id = Guid.NewGuid();
        Type = type;
        Destination = destination;
        Payload = payload;
        CreatedAt = createdAt.ToUniversalTime();
    }

    public Guid Id { get; private set; }

    /// <summary>Contract name, e.g. <c>ProductionOrderReady</c>; the dispatcher deserializes by it.</summary>
    public string Type { get; private set; } = string.Empty;

    /// <summary>Exchange the message is published to.</summary>
    public string Destination { get; private set; } = string.Empty;

    /// <summary>The kernel contract serialized as JSON (jsonb column).</summary>
    public string Payload { get; private set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? PublishedAt { get; private set; }

    public void MarkPublished(DateTimeOffset publishedAt) => PublishedAt = publishedAt.ToUniversalTime();
}

public class OutboxMessageConfiguration : IEntityTypeConfiguration<OutboxMessage>
{
    public void Configure(EntityTypeBuilder<OutboxMessage> builder)
    {
        builder.ToTable("OutboxMessages");
        builder.HasKey(m => m.Id);
        builder.Property(m => m.Id).ValueGeneratedNever();
        builder.Property(m => m.Type).HasMaxLength(128).IsRequired();
        builder.Property(m => m.Destination).HasMaxLength(128).IsRequired();
        builder.Property(m => m.Payload).HasColumnType("jsonb").IsRequired();
        builder.HasIndex(m => m.PublishedAt);
    }
}

/// <summary>
/// <see cref="IProductionOrderEvents"/>: maps the order to the kernel <see cref="ProductionOrderReady"/>
/// snapshot and stages it as an <see cref="OutboxMessage"/> in the current unit of work.
/// </summary>
public class ProductionOrderEvents(ApplicationDbContext context, TimeProvider timeProvider) : IProductionOrderEvents
{
    public const string Destination = "production-order-ready";
    public const string Source = "delivery-calendar";

    public void ProductionOrderReady(ProductionOrder order, IEnumerable<DeliveryCalendar> calendars)
    {
        var menusByDay = calendars
            .SelectMany(calendar => calendar.Days)
            .Where(day => day.Date == order.DeliveryDate)
            .ToDictionary(day => day.Id, day => day.Menu);

        var message = new ProductionOrderReady(correlationId: null, Source)
        {
            ProductionOrderId = order.Id,
            DeliveryDate = order.DeliveryDate.Value,
            ProductionDate = order.ProductionDate.Value,
            Lines = order.Lines
                .Select(line => new ProductionOrderReadyLine
                {
                    DeliveryDayId = line.DeliveryDayId,
                    PatientId = line.PatientId,
                    PlanId = line.PlanRef.Value,
                    Hour = line.Hour?.ToString("HH:mm"),
                    Address = $"{line.Address.Street}, {line.Address.City}",
                    Latitude = line.Coordinates.Latitude,
                    Longitude = line.Coordinates.Longitude,
                    Menu = menusByDay.GetValueOrDefault(line.DeliveryDayId, [])
                        .Select(item => new ProductionOrderReadyMenuItem
                        {
                            MealTime = item.MealTime,
                            MealTimeOrder = item.MealTimeOrder,
                            RecipeId = item.RecipeId,
                            RecipeName = item.RecipeName,
                            Rations = item.Rations
                        })
                        .ToList()
                })
                .ToList()
        };

        context.OutboxMessages.Add(new OutboxMessage(
            nameof(ProductionOrderReady), Destination, JsonConvert.SerializeObject(message), timeProvider.GetUtcNow()));
    }
}

/// <summary>
/// Publishes the pending <see cref="OutboxMessage"/> rows, oldest first. One message failing stops the
/// batch (order matters for snapshots) and is retried on the next run.
/// </summary>
public class OutboxPublisher(
    ApplicationDbContext context,
    IExternalPublisher publisher,
    TimeProvider timeProvider,
    ILogger<OutboxPublisher> logger)
{
    private const int BatchSize = 50;

    /// <returns>How many messages were published.</returns>
    public async Task<int> PublishPendingAsync(CancellationToken cancellationToken)
    {
        var pending = await context.OutboxMessages
            .Where(m => m.PublishedAt == null)
            .OrderBy(m => m.CreatedAt)
            .Take(BatchSize)
            .ToListAsync(cancellationToken);

        var published = 0;
        foreach (var row in pending)
        {
            try
            {
                await PublishAsync(row);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Could not publish outbox message {MessageId} ({Type}); retrying on the next run.", row.Id, row.Type);
                break;
            }

            row.MarkPublished(timeProvider.GetUtcNow());
            await context.SaveChangesAsync(cancellationToken);
            published++;
        }

        return published;
    }

    private Task PublishAsync(OutboxMessage row)
    {
        switch (row.Type)
        {
            case nameof(ProductionOrderReady):
                var message = JsonConvert.DeserializeObject<ProductionOrderReady>(row.Payload)
                    ?? throw new InvalidOperationException($"Outbox message {row.Id} has an empty payload.");
                logger.LogInformation("Publishing {Type} {MessageId} to {Destination}.", row.Type, message.Id, row.Destination);
                // declareDestination: the publish is mandatory, an undeclared exchange fails the call.
                return publisher.PublishAsync(message, row.Destination, declareDestination: true);

            default:
                throw new InvalidOperationException($"Outbox message {row.Id} has the unknown type '{row.Type}'.");
        }
    }
}

/// <summary>Worker host loop: every few seconds, publish whatever the outbox holds.</summary>
public sealed class OutboxDispatcher(IServiceScopeFactory scopeFactory, ILogger<OutboxDispatcher> logger) : BackgroundService
{
    internal static readonly TimeSpan Interval = TimeSpan.FromSeconds(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);
        do
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<OutboxPublisher>().PublishPendingAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Outbox dispatch run failed; retrying in {Interval}.", Interval);
            }
        }
        while (await WaitAsync(timer, stoppingToken));
    }

    private static async Task<bool> WaitAsync(PeriodicTimer timer, CancellationToken cancellationToken)
    {
        try
        {
            return await timer.WaitForNextTickAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }
}
