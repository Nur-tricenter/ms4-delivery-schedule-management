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
    public async Task ResolvedMenu_ShouldRoundTrip_AsOwnedCollection_WithDayNumber()
    {
        var recipeId = Guid.NewGuid();
        var calendar = DeliveryCalendar.Create(
            Guid.NewGuid(),
            new ContractReference(Guid.NewGuid()),
            new PlanReference(Guid.NewGuid()),
            new PlanDuration(15),
            new DeliveryDate(new DateOnly(2026, 8, 1)));
        var day = calendar.Days.ElementAt(3);
        day.SetAddress(new DeliveryAddress("Main St 1", "Springfield", new Coordinates(1, 1)));
        day.MarkReadyForProduction(
        [
            new MenuItem("Breakfast", 1, Guid.NewGuid(), "Oats", 1),
            new MenuItem("Lunch", 3, recipeId, "Quinoa bowl", 2)
        ]);

        await using (var context = IntegrationTestSetup.CreateContext())
        {
            new CalendarRepository(context).Add(calendar);
            await context.SaveChangesAsync(CancellationToken.None);
        }

        // Rename in a separate context: replacing an owned-collection item must persist, not duplicate.
        await using (var context = IntegrationTestSetup.CreateContext())
        {
            var loaded = await new CalendarRepository(context).GetByIdAsync(calendar.Id, CancellationToken.None);
            loaded!.Days.Single(d => d.Id == day.Id).RenameRecipe(recipeId, "Quinoa bowl v2");
            await context.SaveChangesAsync(CancellationToken.None);
        }

        await using (var context = IntegrationTestSetup.CreateContext())
        {
            var loaded = await new CalendarRepository(context).GetByIdAsync(calendar.Id, CancellationToken.None);
            var loadedDay = loaded!.Days.Single(d => d.Id == day.Id);

            loadedDay.DayNumber.ShouldBe(4);
            loadedDay.Portions!.Value.ShouldBe(3);
            loadedDay.Menu.Count.ShouldBe(2);
            loadedDay.Menu.ShouldContain(m => m.RecipeId == recipeId && m.RecipeName == "Quinoa bowl v2" && m.Rations == 2);
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
