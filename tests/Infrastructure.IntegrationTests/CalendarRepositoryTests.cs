using delivery_schedule_management.Domain.Entities;
using delivery_schedule_management.Domain.ValueObjects;
using delivery_schedule_management.Infrastructure.Data.Repositories;

namespace delivery_schedule_management.Infrastructure.IntegrationTests;

public class CalendarRepositoryTests : RepositoryTestBase
{
    [Test]
    public async Task AddThenGetById_ShouldRoundTripCalendarAndDays()
    {
        var contractId = Guid.NewGuid();
        var calendar = DeliveryCalendar.Create(
            Guid.NewGuid(),
            new ContractReference(contractId),
            new PlanReference(Guid.NewGuid()),
            new PlanDuration(15),
            new DeliveryDate(new DateOnly(2026, 8, 1)));

        await using (var context = IntegrationTestSetup.CreateContext())
        {
            var repository = new CalendarRepository(context);
            repository.Add(calendar);
            await context.SaveChangesAsync(CancellationToken.None);
        }

        await using (var context = IntegrationTestSetup.CreateContext())
        {
            var repository = new CalendarRepository(context);
            var loaded = await repository.GetByIdAsync(calendar.Id, CancellationToken.None);

            loaded.ShouldNotBeNull();
            loaded.ContractId.Value.ShouldBe(contractId);
            loaded.Days.Count.ShouldBe(15);
        }
    }

    [Test]
    public async Task GetByContractIdAsync_ShouldFindExistingCalendar_ForIdempotencyCheck()
    {
        var contractId = Guid.NewGuid();
        var calendar = DeliveryCalendar.Create(
            Guid.NewGuid(),
            new ContractReference(contractId),
            new PlanReference(Guid.NewGuid()),
            new PlanDuration(15),
            new DeliveryDate(new DateOnly(2026, 8, 1)));

        await using (var context = IntegrationTestSetup.CreateContext())
        {
            new CalendarRepository(context).Add(calendar);
            await context.SaveChangesAsync(CancellationToken.None);
        }

        await using var readContext = IntegrationTestSetup.CreateContext();
        var found = await new CalendarRepository(readContext).GetByContractIdAsync(contractId, CancellationToken.None);

        found.ShouldNotBeNull();
        found.Id.ShouldBe(calendar.Id);
    }

    [Test]
    public async Task SetAddress_ThenGetByDeliveryDayId_ShouldPersistAddressAndCoordinates()
    {
        var calendar = DeliveryCalendar.Create(
            Guid.NewGuid(),
            new ContractReference(Guid.NewGuid()),
            new PlanReference(Guid.NewGuid()),
            new PlanDuration(15),
            new DeliveryDate(new DateOnly(2026, 8, 1)));

        int dayId;

        await using (var context = IntegrationTestSetup.CreateContext())
        {
            new CalendarRepository(context).Add(calendar);
            await context.SaveChangesAsync(CancellationToken.None);
            dayId = calendar.Days.First().Id;
        }

        await using (var context = IntegrationTestSetup.CreateContext())
        {
            var repository = new CalendarRepository(context);
            var loaded = await repository.GetByIdAsync(calendar.Id, CancellationToken.None);
            var day = loaded!.Days.First(d => d.Id == dayId);

            day.SetAddress(new DeliveryAddress("Main St 123", "Springfield", new Coordinates(10.5, -20.25), "Gate 2", "Ring twice"));

            await context.SaveChangesAsync(CancellationToken.None);
        }

        await using (var context = IntegrationTestSetup.CreateContext())
        {
            var repository = new CalendarRepository(context);
            var loaded = await repository.GetByDeliveryDayIdAsync(dayId, CancellationToken.None);
            var day = loaded!.Days.First(d => d.Id == dayId);

            day.Address.ShouldNotBeNull();
            day.Address.Street.ShouldBe("Main St 123");
            day.Address.Coordinates.Latitude.ShouldBe(10.5);
            day.Address.Coordinates.Longitude.ShouldBe(-20.25);
        }
    }

    [Test]
    public async Task GetActiveForDeliveryDateAsync_ShouldReturnCalendarsWithMatchingDay()
    {
        var targetDate = new DateOnly(2026, 8, 10);
        var calendar = DeliveryCalendar.Create(
            Guid.NewGuid(),
            new ContractReference(Guid.NewGuid()),
            new PlanReference(Guid.NewGuid()),
            new PlanDuration(15),
            new DeliveryDate(targetDate));

        await using (var context = IntegrationTestSetup.CreateContext())
        {
            new CalendarRepository(context).Add(calendar);
            await context.SaveChangesAsync(CancellationToken.None);
        }

        await using var readContext = IntegrationTestSetup.CreateContext();
        var results = await new CalendarRepository(readContext).GetActiveForDeliveryDateAsync(targetDate, CancellationToken.None);

        results.ShouldHaveSingleItem();
        results[0].Id.ShouldBe(calendar.Id);
    }
}
