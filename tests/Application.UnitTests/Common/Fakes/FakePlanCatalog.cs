using delivery_schedule_management.Application.Common.Interfaces;

namespace delivery_schedule_management.Application.UnitTests.Common.Fakes;

public class FakePlanCatalog : IPlanCatalog
{
    public Dictionary<Guid, PlanSummary> Plans { get; } = new();

    public Task<PlanSummary?> FindAsync(Guid planId, CancellationToken cancellationToken) =>
        Task.FromResult(Plans.GetValueOrDefault(planId));
}
