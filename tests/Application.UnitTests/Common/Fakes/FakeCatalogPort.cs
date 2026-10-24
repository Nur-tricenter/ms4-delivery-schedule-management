using delivery_schedule_management.Application.Common.Interfaces;

namespace delivery_schedule_management.Application.UnitTests.Common.Fakes;

public class FakeCatalogPort(CatalogDayMenu? menu = null) : ICatalogPort
{
    private readonly CatalogDayMenu? _menu = menu ?? new CatalogDayMenu(
        [new CatalogMenuItem("Lunch", 3, Guid.Parse("11111111-1111-1111-1111-111111111111"), "recipe-default", 2)]);

    public List<(Guid PlanId, int DayNumber)> Calls { get; } = [];

    /// <summary>When set, every call throws it — simulates MS2 being unreachable.</summary>
    public Exception? ThrowOnCall { get; set; }

    public Task<CatalogDayMenu?> GetDayMenuAsync(Guid planId, int dayNumber, CancellationToken cancellationToken)
    {
        Calls.Add((planId, dayNumber));

        if (ThrowOnCall is not null)
            throw ThrowOnCall;

        return Task.FromResult(_menu);
    }
}
