using delivery_schedule_management.Application.Common.Interfaces;

namespace delivery_schedule_management.Application.UnitTests.Common.Fakes;

public class FakeDeliveryRouteLinks : IDeliveryRouteLinks
{
    public List<DeliveryRouteLinkSummary> Links { get; } = [];

    public Task<DeliveryRouteLinkSummary?> FindLatestForDayAsync(int dayId, CancellationToken cancellationToken) =>
        Task.FromResult(Links.Where(l => l.DayId == dayId).OrderByDescending(l => l.LinkedAt).FirstOrDefault());
}
