using delivery_schedule_management.Application.Common.Interfaces;

namespace delivery_schedule_management.Application.UnitTests.Common.Fakes;

public class FakeCatalogPort(CatalogDayResolution? resolution = null) : ICatalogPort
{
    private readonly CatalogDayResolution _resolution = resolution ?? new CatalogDayResolution("recipe-default", 2);

    public Task<CatalogDayResolution> GetDayResolutionAsync(Guid planId, DateOnly date, CancellationToken cancellationToken) =>
        Task.FromResult(_resolution);
}
