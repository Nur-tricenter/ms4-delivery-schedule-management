using delivery_schedule_management.Application.Common.Interfaces;

namespace delivery_schedule_management.Application.UnitTests.Common.Fakes;

public class FakeApplicationDbContext : IApplicationDbContext
{
    public Task<int> SaveChangesAsync(CancellationToken cancellationToken) => Task.FromResult(0);
}
