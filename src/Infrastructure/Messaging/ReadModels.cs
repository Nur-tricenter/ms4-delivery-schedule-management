using delivery_schedule_management.Application.Common.Interfaces;
using delivery_schedule_management.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace delivery_schedule_management.Infrastructure.Messaging;

/// <summary>
/// One row per integration message already handled. Its primary key IS the message id, so a
/// redelivery collides on insert and is skipped (see <see cref="IdempotentConsumer{T,TInner}"/>).
/// </summary>
public class InboxMessage
{
    private InboxMessage() { } // EF Core

    public InboxMessage(Guid messageId, string messageType, string? correlationId, DateTimeOffset createdAt, DateTimeOffset processedOn)
    {
        MessageId = messageId;
        MessageType = messageType;
        CorrelationId = correlationId;
        // Npgsql only writes UTC DateTimeOffsets to timestamptz.
        MessageCreatedAt = createdAt.ToUniversalTime();
        ProcessedOn = processedOn.ToUniversalTime();
    }

    public Guid MessageId { get; private set; }

    public string MessageType { get; private set; } = string.Empty;

    public string? CorrelationId { get; private set; }

    public DateTimeOffset MessageCreatedAt { get; private set; }

    public DateTimeOffset ProcessedOn { get; private set; }
}

/// <summary>
/// MS4's projection of an MS2 meal plan, from <c>MealPlanPublished</c>. Not a domain entity: MS4
/// does not own plans, it only remembers what MS2 announced.
/// </summary>
public class PlanCatalogEntry
{
    private PlanCatalogEntry() { } // EF Core

    public PlanCatalogEntry(Guid planId, string name, int totalDays, int mealsPerDay, DateTimeOffset updatedAt)
    {
        PlanId = planId;
        Refresh(name, totalDays, mealsPerDay, updatedAt);
    }

    public Guid PlanId { get; private set; }

    public string Name { get; private set; } = string.Empty;

    public int TotalDays { get; private set; }

    public int MealsPerDay { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public void Refresh(string name, int totalDays, int mealsPerDay, DateTimeOffset updatedAt)
    {
        Name = name;
        TotalDays = totalDays;
        MealsPerDay = mealsPerDay;
        UpdatedAt = updatedAt.ToUniversalTime();
    }
}

public class InboxMessageConfiguration : IEntityTypeConfiguration<InboxMessage>
{
    public void Configure(EntityTypeBuilder<InboxMessage> builder)
    {
        builder.ToTable("InboxMessages");
        builder.HasKey(m => m.MessageId);
        builder.Property(m => m.MessageId).ValueGeneratedNever();
        builder.Property(m => m.MessageType).HasMaxLength(256).IsRequired();
        builder.Property(m => m.CorrelationId).HasMaxLength(128);
        builder.HasIndex(m => m.ProcessedOn);
    }
}

public class PlanCatalogEntryConfiguration : IEntityTypeConfiguration<PlanCatalogEntry>
{
    public void Configure(EntityTypeBuilder<PlanCatalogEntry> builder)
    {
        builder.ToTable("PlanCatalog");
        builder.HasKey(p => p.PlanId);
        builder.Property(p => p.PlanId).ValueGeneratedNever();
        builder.Property(p => p.Name).HasMaxLength(200).IsRequired();
    }
}

/// <summary><see cref="IPlanCatalog"/> over the <see cref="PlanCatalogEntry"/> projection.</summary>
public class PlanCatalog(ApplicationDbContext context) : IPlanCatalog
{
    public async Task<PlanSummary?> FindAsync(Guid planId, CancellationToken cancellationToken) =>
        await context.PlanCatalog
            .AsNoTracking()
            .Where(p => p.PlanId == planId)
            .Select(p => new PlanSummary(p.PlanId, p.Name, p.TotalDays))
            .FirstOrDefaultAsync(cancellationToken);
}
