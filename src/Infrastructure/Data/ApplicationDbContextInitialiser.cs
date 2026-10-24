using delivery_schedule_management.Domain.Entities;
using delivery_schedule_management.Domain.Enums;
using delivery_schedule_management.Domain.Services;
using delivery_schedule_management.Domain.ValueObjects;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace delivery_schedule_management.Infrastructure.Data;

public static class InitialiserExtensions
{
    public static async Task InitialiseDatabaseAsync(this WebApplication app, bool applyMigrations = true, bool seed = true)
    {
        if (!applyMigrations && !seed)
            return;

        using var scope = app.Services.CreateScope();

        var initialiser = scope.ServiceProvider.GetRequiredService<ApplicationDbContextInitialiser>();

        if (applyMigrations)
            await initialiser.InitialiseAsync();

        if (seed)
            await initialiser.SeedAsync();
    }

    /// <summary>
    /// Backs the <c>--migrate</c> one-shot job: applies pending migrations and returns an exit code
    /// (0 success, 1 failure) instead of starting the web host.
    /// </summary>
    public static async Task<int> MigrateDatabaseAndExitAsync(this WebApplication app)
    {
        try
        {
            await app.InitialiseDatabaseAsync(applyMigrations: true, seed: false);
            return 0;
        }
        catch
        {
            // Already logged by ApplicationDbContextInitialiser.
            return 1;
        }
    }
}

public class ApplicationDbContextInitialiser
{
    private readonly ILogger<ApplicationDbContextInitialiser> _logger;
    private readonly ApplicationDbContext _context;
    private readonly TimeProvider _timeProvider;

    public ApplicationDbContextInitialiser(ILogger<ApplicationDbContextInitialiser> logger, ApplicationDbContext context, TimeProvider timeProvider)
    {
        _logger = logger;
        _context = context;
        _timeProvider = timeProvider;
    }

    public async Task InitialiseAsync()
    {
        try
        {
            // See https://jasontaylor.dev/ef-core-database-initialisation-strategies
            await _context.Database.MigrateAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "An error occurred while initialising the database.");
            throw;
        }
    }

    public async Task SeedAsync()
    {
        try
        {
            await TrySeedAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "An error occurred while seeding the database.");
            throw;
        }
    }

    public async Task TrySeedAsync()
    {
        if (await _context.Calendars.AnyAsync())
        {
            return;
        }

        var now = _timeProvider.GetUtcNow();
        var today = DateOnly.FromDateTime(now.UtcDateTime);

        // Patient 1 — freshly created calendar, no address on any day yet (all Pending).
        // Demonstrates the state right after MS3 calls POST /schedule.
        var freshCalendar = DeliveryCalendar.Create(
            Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001"),
            new ContractReference(Guid.Parse("cccccccc-0000-0000-0000-000000000001")),
            new PlanReference(Guid.Parse("bbbbbbbb-0000-0000-0000-000000000001")),
            new PlanDuration(15),
            new DeliveryDate(today.AddDays(5)));

        // Patient 2 — tomorrow's day is already resolved and consolidated into a ProductionOrder,
        // so GET /calendars/active-tomorrow has data without running the catalog job first.
        // The day after has an address but hasn't been resolved yet.
        var tomorrowCalendar = DeliveryCalendar.Create(
            Guid.Parse("aaaaaaaa-0000-0000-0000-000000000002"),
            new ContractReference(Guid.Parse("cccccccc-0000-0000-0000-000000000002")),
            new PlanReference(Guid.Parse("bbbbbbbb-0000-0000-0000-000000000002")),
            new PlanDuration(15),
            new DeliveryDate(today.AddDays(1)));

        var tomorrowDay = tomorrowCalendar.Days.First();
        tomorrowDay.SetAddress(new DeliveryAddress("742 Evergreen Terrace", "Springfield", new Coordinates(10.5, -20.25)));
        tomorrowDay.MarkReadyForProduction(new RecipeReference("recipe-demo-tomorrow"), new PortionQuantity(2));

        var dayAfterTomorrow = tomorrowCalendar.Days.Skip(1).First();
        dayAfterTomorrow.SetAddress(new DeliveryAddress("123 Fake St", "Springfield", new Coordinates(11.0, -20.0)));

        // Patient 3 — closed-cycle history: one delivered, one failed (both in the past), and one
        // cancelled day (in the future, safely outside the 48h lock window). Rest stay Pending.
        var historyCalendar = DeliveryCalendar.Create(
            Guid.Parse("aaaaaaaa-0000-0000-0000-000000000003"),
            new ContractReference(Guid.Parse("cccccccc-0000-0000-0000-000000000003")),
            new PlanReference(Guid.Parse("bbbbbbbb-0000-0000-0000-000000000003")),
            new PlanDuration(30),
            new DeliveryDate(today.AddDays(-20)));

        var deliveredDay = historyCalendar.Days.ElementAt(0);
        deliveredDay.SetAddress(new DeliveryAddress("100 5th Ave", "Metropolis", new Coordinates(5, 5)));
        deliveredDay.MarkReadyForProduction(new RecipeReference("recipe-demo-delivered"), new PortionQuantity(3));
        deliveredDay.ApplyDeliveryResult(
            DeliveryResult.Delivered,
            new DeliveryEvidence(now.AddDays(-20), "https://example.com/evidence-delivered.jpg"),
            now.AddDays(-20));

        var failedDay = historyCalendar.Days.ElementAt(1);
        failedDay.SetAddress(new DeliveryAddress("101 5th Ave", "Metropolis", new Coordinates(5.1, 5.1)));
        failedDay.MarkReadyForProduction(new RecipeReference("recipe-demo-failed"), new PortionQuantity(1));
        failedDay.ApplyDeliveryResult(DeliveryResult.Failed, null, now.AddDays(-19));

        // startDate + 29 days = today + 9 days — furthest available day in this calendar,
        // maximises safety margin above the 48h lock window.
        var cancelledDay = historyCalendar.Days.ElementAt(29);
        cancelledDay.SetAddress(new DeliveryAddress("102 5th Ave", "Metropolis", new Coordinates(5.2, 5.2)));
        cancelledDay.Cancel(now);

        _context.Calendars.AddRange(freshCalendar, tomorrowCalendar, historyCalendar);

        // Save first so EF assigns real DeliveryDay ids — OrderConsolidationService builds
        // ProductionOrderLine.DeliveryDayId from Day.Id, which is still 0 before SaveChanges.
        await _context.SaveChangesAsync();

        var tomorrowOrder = OrderConsolidationService.Consolidate([tomorrowCalendar], tomorrowDay.Date, null, now);
        _context.ProductionOrders.Add(tomorrowOrder);

        await _context.SaveChangesAsync();
    }
}
