namespace delivery_schedule_management.Application.Common.Interfaces;

/// <summary>
/// Local read model of MS2's meal plans, projected from <c>MealPlanPublished</c> events
/// (queue <c>ms4.meal-plan-published</c>). Lets MS4 sanity-check a plan without calling MS2.
/// </summary>
/// <remarks>
/// It is eventually consistent and may be incomplete — a plan created before MS4 subscribed, or
/// whose event is still in flight, is simply absent. Callers must treat "not found" as "unknown",
/// never as "invalid".
/// </remarks>
public interface IPlanCatalog
{
    Task<PlanSummary?> FindAsync(Guid planId, CancellationToken cancellationToken);
}

public record PlanSummary(Guid PlanId, string Name, int TotalDays);
