using delivery_schedule_management.Domain.Entities;
using delivery_schedule_management.Domain.ValueObjects;
using delivery_schedule_management.Infrastructure.Data.Repositories;
using delivery_schedule_management.Infrastructure.Messaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Nur.Tricenter.Integration.Messages.Catalog;

namespace delivery_schedule_management.Infrastructure.IntegrationTests;

/// <summary>
/// The RabbitMQ consumer pipeline (minus the broker) against real Postgres: inbox idempotency and
/// the two projections MS4 keeps from MS2's events.
/// </summary>
public class MessagingConsumerTests : RepositoryTestBase
{
    private static MealPlanPublished PlanEvent(Guid planId, string name, int totalDays) =>
        new("corr-1", "catalog") { PlanId = planId, Name = name, DurationType = "FIFTEEN_DAYS", TotalDays = totalDays, MealsPerDay = 3 };

    private static async Task HandleAsync(MealPlanPublished message)
    {
        await using var context = IntegrationTestSetup.CreateContext();
        var inner = new MealPlanPublishedConsumer(context, TimeProvider.System, NullLogger<MealPlanPublishedConsumer>.Instance);
        var consumer = new IdempotentConsumer<MealPlanPublished, MealPlanPublishedConsumer>(
            inner, context, TimeProvider.System, NullLogger<IdempotentConsumer<MealPlanPublished, MealPlanPublishedConsumer>>.Instance);

        await consumer.HandleAsync(message);
    }

    [Test]
    public async Task MealPlanPublished_ShouldProjectPlan_AndBeReadableThroughPlanCatalog()
    {
        var planId = Guid.NewGuid();

        await HandleAsync(PlanEvent(planId, "Keto 15", 15));

        await using var context = IntegrationTestSetup.CreateContext();
        var plan = await new PlanCatalog(context).FindAsync(planId, CancellationToken.None);
        plan.ShouldNotBeNull();
        plan.Name.ShouldBe("Keto 15");
        plan.TotalDays.ShouldBe(15);
    }

    [Test]
    public async Task SameMessageDeliveredTwice_ShouldBeProcessedOnce()
    {
        var message = PlanEvent(Guid.NewGuid(), "Keto 15", 15);

        await HandleAsync(message);
        await HandleAsync(message);

        await using var context = IntegrationTestSetup.CreateContext();
        (await context.InboxMessages.CountAsync(m => m.MessageId == message.Id)).ShouldBe(1);
        (await context.PlanCatalog.CountAsync()).ShouldBe(1);
    }

    [Test]
    public async Task RepublishedPlan_WithNewMessageId_ShouldRefreshTheProjection()
    {
        var planId = Guid.NewGuid();

        await HandleAsync(PlanEvent(planId, "Keto 15", 15));
        await HandleAsync(PlanEvent(planId, "Keto 15 (rev. 2)", 15));

        await using var context = IntegrationTestSetup.CreateContext();
        (await context.PlanCatalog.SingleAsync(p => p.PlanId == planId)).Name.ShouldBe("Keto 15 (rev. 2)");
    }

    [Test]
    public async Task RecipeUpdated_ShouldRenameRecipe_OnOpenDaysOnly()
    {
        var recipeId = Guid.NewGuid();
        var calendar = DeliveryCalendar.Create(
            Guid.NewGuid(), new ContractReference(Guid.NewGuid()), new PlanReference(Guid.NewGuid()),
            new PlanDuration(15), new DeliveryDate(new DateOnly(2026, 8, 1)));

        var openDay = calendar.Days.ElementAt(0);
        var closedDay = calendar.Days.ElementAt(1);
        foreach (var day in new[] { openDay, closedDay })
        {
            day.SetAddress(new DeliveryAddress("Main St 1", "Springfield", new Coordinates(1, 1)));
            day.MarkReadyForProduction([new MenuItem("Lunch", 3, recipeId, "Salmon", 2)]);
        }
        closedDay.ApplyDeliveryResult(Domain.Enums.DeliveryResult.Delivered, null, DateTimeOffset.UtcNow);

        await using (var context = IntegrationTestSetup.CreateContext())
        {
            new CalendarRepository(context).Add(calendar);
            await context.SaveChangesAsync();
        }

        await using (var context = IntegrationTestSetup.CreateContext())
        {
            var consumer = new RecipeUpdatedConsumer(context, NullLogger<RecipeUpdatedConsumer>.Instance);
            await consumer.HandleAsync(new RecipeUpdated("corr-2", "catalog") { RecipeId = recipeId, Name = "Grilled salmon", Instructions = "" });
            await context.SaveChangesAsync();
        }

        await using (var context = IntegrationTestSetup.CreateContext())
        {
            var loaded = await new CalendarRepository(context).GetByIdAsync(calendar.Id, CancellationToken.None);
            loaded!.Days.Single(d => d.Id == openDay.Id).Menu.Single().RecipeName.ShouldBe("Grilled salmon");
            loaded.Days.Single(d => d.Id == closedDay.Id).Menu.Single().RecipeName.ShouldBe("Salmon");
        }
    }
}
