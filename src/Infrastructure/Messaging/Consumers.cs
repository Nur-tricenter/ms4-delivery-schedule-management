using delivery_schedule_management.Domain.Enums;
using delivery_schedule_management.Infrastructure.Data;
using Joseco.Communication.External.Contracts.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Nur.Tricenter.Integration.Messages.Catalog;

namespace delivery_schedule_management.Infrastructure.Messaging;

/// <summary>
/// <c>ms4.meal-plan-published</c> → upserts the plan into <see cref="PlanCatalogEntry"/>, which
/// <c>CreateScheduleCommand</c> uses to check a calendar's duration against the plan's.
/// Upsert, not insert: a replayed or re-published plan refreshes the row instead of failing.
/// </summary>
public sealed class MealPlanPublishedConsumer(
    ApplicationDbContext context,
    TimeProvider timeProvider,
    ILogger<MealPlanPublishedConsumer> logger) : IIntegrationMessageConsumer<MealPlanPublished>
{
    public async Task HandleAsync(MealPlanPublished message, CancellationToken cancellationToken = default)
    {
        var now = timeProvider.GetUtcNow();
        var entry = await context.PlanCatalog.FirstOrDefaultAsync(p => p.PlanId == message.PlanId, cancellationToken);

        if (entry is null)
            context.PlanCatalog.Add(new PlanCatalogEntry(message.PlanId, message.Name, message.TotalDays, message.MealsPerDay, now));
        else
            entry.Refresh(message.Name, message.TotalDays, message.MealsPerDay, now);

        logger.LogInformation("Projected MS2 plan {PlanId} ({Name}, {TotalDays} days).", message.PlanId, message.Name, message.TotalDays);
    }
}

/// <summary>
/// <c>ms4.recipe-updated</c> → refreshes the recipe-name snapshot on every not-yet-closed delivery
/// day whose menu includes that recipe (see <c>DeliveryDay.RenameRecipe</c>).
/// </summary>
public sealed class RecipeUpdatedConsumer(
    ApplicationDbContext context,
    ILogger<RecipeUpdatedConsumer> logger) : IIntegrationMessageConsumer<RecipeUpdated>
{
    private static readonly DayStatus[] ClosedStatuses = [DayStatus.Delivered, DayStatus.Failed, DayStatus.Cancelled];

    public async Task HandleAsync(RecipeUpdated message, CancellationToken cancellationToken = default)
    {
        var calendars = await context.Calendars
            .Include(c => c.Days)
            .Where(c => c.Days.Any(d => !ClosedStatuses.Contains(d.Status) && d.Menu.Any(m => m.RecipeId == message.RecipeId)))
            .ToListAsync(cancellationToken);

        var renamed = calendars
            .SelectMany(c => c.Days)
            .Count(day => day.RenameRecipe(message.RecipeId, message.Name));

        logger.LogInformation("Recipe {RecipeId} renamed to '{Name}' on {Count} open delivery day(s).",
            message.RecipeId, message.Name, renamed);
    }
}
