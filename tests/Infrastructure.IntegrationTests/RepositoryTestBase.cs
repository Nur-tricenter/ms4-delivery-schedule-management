namespace delivery_schedule_management.Infrastructure.IntegrationTests;

public abstract class RepositoryTestBase
{
    [SetUp]
    public async Task SetUp()
    {
        await IntegrationTestSetup.ResetStateAsync();
    }
}
