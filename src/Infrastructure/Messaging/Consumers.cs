using delivery_schedule_management.Domain.Enums;
using delivery_schedule_management.Domain.Exceptions;
using delivery_schedule_management.Domain.ValueObjects;
using delivery_schedule_management.Infrastructure.Data;
using Joseco.Communication.External.Contracts.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Nur.Tricenter.Integration.Messages.Catalog;
using Nur.Tricenter.Integration.Messages.Logistics;

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

/// <summary>
/// <c>ms4.delivery-added-to-route</c> -> remembers which MS4 delivery day an MS5 delivery carries.
/// </summary>
/// <remarks>
/// Two ways to find the day:
/// <list type="number">
/// <item><c>PackageId</c> is MS4's <c>packId</c> (the <c>DeliveryDay.Id</c>): the package was loaded
/// into MS5 with the id from <c>active-tomorrow</c>.</item>
/// <item>Otherwise, by patient. Packages that reach MS5 through MS3's <c>package-validated</c> carry
/// MS3's own package GUID, because that contract has no field for MS4's <c>packId</c>. The day is
/// then the patient's earliest day in production (<c>ReadyForProduction</c>/<c>InProduction</c>) that
/// no other delivery is linked to. A patient has at most one such day per delivery date, so this is
/// unambiguous as long as packages are dispatched in date order.</item>
/// </list>
/// A message matching neither (e.g. a package loaded by hand for an unknown patient) is ignored.
/// </remarks>
public sealed class DeliveryAddedToRouteConsumer(
    ApplicationDbContext context,
    TimeProvider timeProvider,
    ILogger<DeliveryAddedToRouteConsumer> logger) : IIntegrationMessageConsumer<DeliveryAddedToRoute>
{
    private static readonly DayStatus[] InProductionStatuses = [DayStatus.ReadyForProduction, DayStatus.InProduction];

    public async Task HandleAsync(DeliveryAddedToRoute message, CancellationToken cancellationToken = default)
    {
        int? dayId = int.TryParse(message.PackageId, out var packId)
            ? packId
            : await FindDayByPatientAsync(message, cancellationToken);

        if (dayId is null)
        {
            logger.LogWarning(
                "Delivery {DeliveryId}: package '{PackageId}' of patient '{PatientId}' matches no MS4 delivery day; not linked.",
                message.DeliveryId, message.PackageId, message.PatientId);
            return;
        }

        await LinkAsync(message, dayId.Value, cancellationToken);
    }

    private async Task<int?> FindDayByPatientAsync(DeliveryAddedToRoute message, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(message.PatientId, out var patientId))
            return null;

        var linkedDays = context.DeliveryRouteLinks
            .Where(l => l.DeliveryId != message.DeliveryId)
            .Select(l => l.DayId);

        var candidates = await context.Calendars
            .Where(c => c.PatientId == patientId)
            .SelectMany(c => c.Days)
            .Where(d => InProductionStatuses.Contains(d.Status) && !linkedDays.Contains(d.Id))
            .Select(d => new { d.Id, Date = d.Date.Value })
            .ToListAsync(cancellationToken);

        var day = candidates.OrderBy(d => d.Date).FirstOrDefault();
        if (day is not null)
        {
            logger.LogInformation(
                "Delivery {DeliveryId}: package '{PackageId}' is not an MS4 pack id; matched by patient {PatientId} to day {DayId} ({Date}).",
                message.DeliveryId, message.PackageId, patientId, day.Id, day.Date);
        }

        return day?.Id;
    }

    private async Task LinkAsync(DeliveryAddedToRoute message, int dayId, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var link = await context.DeliveryRouteLinks.FirstOrDefaultAsync(l => l.DeliveryId == message.DeliveryId, cancellationToken);

        if (link is null)
            context.DeliveryRouteLinks.Add(new DeliveryRouteLink(message.DeliveryId, message.RouteId, dayId, now));
        else
            link.Reassign(message.RouteId, dayId, now);

        logger.LogInformation("Linked MS5 delivery {DeliveryId} to delivery day {DayId}.", message.DeliveryId, dayId);
    }
}

/// <summary>Shared lookup + state change for the two delivery-outcome consumers.</summary>
internal static class DeliveryOutcome
{
    /// <summary>
    /// Applies <paramref name="result"/> to the day linked to <paramref name="deliveryId"/>. Never
    /// throws for business reasons: an unknown delivery or a day that can no longer take a result
    /// (e.g. cancelled) would fail on every retry, so it is logged and acknowledged instead.
    /// </summary>
    public static async Task ApplyAsync(
        ApplicationDbContext context, ILogger logger, Guid deliveryId, DeliveryResult result,
        DeliveryEvidence? evidence, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var link = await context.DeliveryRouteLinks.AsNoTracking().FirstOrDefaultAsync(l => l.DeliveryId == deliveryId, cancellationToken);
        if (link is null)
        {
            logger.LogWarning("{Result} reported for delivery {DeliveryId}, which is not linked to any MS4 day; ignored.", result, deliveryId);
            return;
        }

        var calendar = await context.Calendars.Include(c => c.Days)
            .FirstOrDefaultAsync(c => c.Days.Any(d => d.Id == link.DayId), cancellationToken);
        if (calendar is null)
        {
            logger.LogWarning("Delivery {DeliveryId} points to delivery day {DayId}, which no longer exists; ignored.", deliveryId, link.DayId);
            return;
        }

        try
        {
            calendar.Days.First(d => d.Id == link.DayId).ApplyDeliveryResult(result, evidence, now);
            logger.LogInformation("Delivery day {DayId} marked {Result} (MS5 delivery {DeliveryId}).", link.DayId, result, deliveryId);
        }
        catch (InvalidDeliveryDayTransitionException ex)
        {
            logger.LogWarning(ex, "{Result} for delivery {DeliveryId} rejected by day {DayId}; ignored.", result, deliveryId, link.DayId);
        }
    }
}

/// <summary>
/// <c>ms4.delivery-completed</c> -> delivery day becomes <c>Delivered</c> (same rule as
/// <c>PATCH .../delivery-result</c>). The event carries no photo or signature, only the timestamp.
/// </summary>
public sealed class DeliveryCompletedConsumer(
    ApplicationDbContext context,
    ILogger<DeliveryCompletedConsumer> logger) : IIntegrationMessageConsumer<DeliveryCompleted>
{
    public Task HandleAsync(DeliveryCompleted message, CancellationToken cancellationToken = default)
    {
        var deliveredAt = new DateTimeOffset(DateTime.SpecifyKind(message.DeliveredAt, DateTimeKind.Utc));
        return DeliveryOutcome.ApplyAsync(context, logger, message.DeliveryId, DeliveryResult.Delivered,
            new DeliveryEvidence(deliveredAt), deliveredAt, cancellationToken);
    }
}

/// <summary><c>ms4.delivery-failed</c> -> delivery day becomes <c>Failed</c>.</summary>
public sealed class DeliveryFailedConsumer(
    ApplicationDbContext context,
    TimeProvider timeProvider,
    ILogger<DeliveryFailedConsumer> logger) : IIntegrationMessageConsumer<DeliveryFailed>
{
    public Task HandleAsync(DeliveryFailed message, CancellationToken cancellationToken = default)
    {
        logger.LogInformation("Delivery {DeliveryId} failed: {Reason}", message.DeliveryId, message.Reason);
        return DeliveryOutcome.ApplyAsync(context, logger, message.DeliveryId, DeliveryResult.Failed,
            evidence: null, timeProvider.GetUtcNow(), cancellationToken);
    }
}
