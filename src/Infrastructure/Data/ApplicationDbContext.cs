using System.Reflection;
using delivery_schedule_management.Application.Common.Interfaces;
using delivery_schedule_management.Domain.Entities;
using delivery_schedule_management.Infrastructure.Messaging;
using Microsoft.EntityFrameworkCore;

namespace delivery_schedule_management.Infrastructure.Data;

public class ApplicationDbContext : DbContext, IApplicationDbContext
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : base(options) { }

    public DbSet<DeliveryCalendar> Calendars => Set<DeliveryCalendar>();

    public DbSet<ProductionOrder> ProductionOrders => Set<ProductionOrder>();

    /// <summary>Integration messages already consumed (idempotent inbox).</summary>
    public DbSet<InboxMessage> InboxMessages => Set<InboxMessage>();

    /// <summary>Projection of MS2 meal plans (from <c>MealPlanPublished</c>).</summary>
    public DbSet<PlanCatalogEntry> PlanCatalog => Set<PlanCatalogEntry>();

    /// <summary>MS5 delivery id -> MS4 delivery day (from <c>DeliveryAddedToRoute</c>).</summary>
    public DbSet<DeliveryRouteLink> DeliveryRouteLinks => Set<DeliveryRouteLink>();

    /// <summary>Projection of MS2 meal plan day menus (from <c>MealPlanCompositionChanged</c>).</summary>
    public DbSet<PlanDayMenu> PlanDayMenus => Set<PlanDayMenu>();

    /// <summary>Integration messages MS4 still has to publish (transactional outbox).</summary>
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.ApplyConfigurationsFromAssembly(Assembly.GetExecutingAssembly());
    }
}
