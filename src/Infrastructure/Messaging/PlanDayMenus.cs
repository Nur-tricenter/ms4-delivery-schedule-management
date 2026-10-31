using delivery_schedule_management.Application.Common.Interfaces;
using delivery_schedule_management.Domain.ValueObjects;
using delivery_schedule_management.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace delivery_schedule_management.Infrastructure.Messaging;

/// <summary>
/// MS4's projection of one day of an MS2 meal plan, from <c>MealPlanCompositionChanged</c>. Not a
/// domain entity: MS4 does not own plans, it only remembers the menu MS2 announced.
/// </summary>
public class PlanDayMenu
{
    private readonly List<MenuItem> _items = new();

    private PlanDayMenu() { } // EF Core

    public PlanDayMenu(Guid planId, int dayNumber)
    {
        PlanId = planId;
        DayNumber = dayNumber;
    }

    public Guid PlanId { get; private set; }

    /// <summary>MS2's <c>PlanDay.DayNumber</c> (1..N), the same number as <c>DeliveryDay.DayNumber</c>.</summary>
    public int DayNumber { get; private set; }

    public IReadOnlyCollection<MenuItem> Items => _items.AsReadOnly();

    /// <summary><c>CreatedAt</c> of the snapshot this row came from; an older snapshot never overwrites it.</summary>
    public DateTimeOffset SnapshotAt { get; private set; }

    /// <summary>True when a snapshot created at <paramref name="createdAt"/> must not replace this row.</summary>
    public bool IsNewerThan(DateTimeOffset createdAt) => SnapshotAt > createdAt.ToUniversalTime();

    public void Replace(IEnumerable<MenuItem> items, DateTimeOffset snapshotAt)
    {
        _items.Clear();
        _items.AddRange(items);
        SnapshotAt = snapshotAt.ToUniversalTime();
    }
}

public class PlanDayMenuConfiguration : IEntityTypeConfiguration<PlanDayMenu>
{
    public void Configure(EntityTypeBuilder<PlanDayMenu> builder)
    {
        builder.ToTable("PlanDayMenus");
        builder.HasKey(m => new { m.PlanId, m.DayNumber });
        builder.Property(m => m.PlanId).ValueGeneratedNever();
        builder.Property(m => m.DayNumber).ValueGeneratedNever();

        // Same owned-collection shape as DeliveryDay.Menu (CLAUDE.md §5).
        builder.OwnsMany(m => m.Items, items =>
        {
            items.ToTable("PlanDayMenuItems");
            items.WithOwner().HasForeignKey("PlanId", "DayNumber");
            items.Property<int>("Id");
            items.HasKey("Id");
            items.Property(i => i.MealTime).HasMaxLength(64).IsRequired();
            items.Property(i => i.MealTimeOrder);
            items.Property(i => i.RecipeId);
            items.Property(i => i.RecipeName).HasMaxLength(200).IsRequired();
            items.Property(i => i.Rations);
        });
        builder.Navigation(m => m.Items).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

/// <summary>
/// <see cref="ICatalogPort"/> over the <see cref="PlanDayMenu"/> projection: the menu of a plan day
/// comes from MS2's published composition, with no call to MS2.
/// </summary>
public class ProjectedCatalogPort(ApplicationDbContext context) : ICatalogPort
{
    public async Task<CatalogDayMenu?> GetDayMenuAsync(Guid planId, int dayNumber, CancellationToken cancellationToken)
    {
        var day = await context.PlanDayMenus
            .AsNoTracking()
            .FirstOrDefaultAsync(m => m.PlanId == planId && m.DayNumber == dayNumber, cancellationToken);

        if (day is null)
            return null;

        return new CatalogDayMenu(day.Items
            .OrderBy(i => i.MealTimeOrder)
            .Select(i => new CatalogMenuItem(i.MealTime, i.MealTimeOrder, i.RecipeId, i.RecipeName, i.Rations))
            .ToList());
    }
}
