using delivery_schedule_management.Domain.Entities;
using delivery_schedule_management.Domain.ValueObjects;
using delivery_schedule_management.Infrastructure.Data.Repositories;
using delivery_schedule_management.Infrastructure.Messaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Nur.Tricenter.Integration.Messages.Catalog;
using Nur.Tricenter.Integration.Messages.Logistics;

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

    private static async Task<int> SaveReadyDayAsync(bool cancel = false, Guid? patientId = null) =>
        (await SaveReadyDaysAsync(patientId ?? Guid.NewGuid(), [0], cancel))[0];

    /// <summary>One calendar for <paramref name="patientId"/>; the days at <paramref name="dayIndexes"/> become ReadyForProduction.</summary>
    private static async Task<int[]> SaveReadyDaysAsync(Guid patientId, int[] dayIndexes, bool cancel = false)
    {
        var calendar = DeliveryCalendar.Create(
            patientId, new ContractReference(Guid.NewGuid()), new PlanReference(Guid.NewGuid()),
            new PlanDuration(15), new DeliveryDate(new DateOnly(2026, 8, 1)));
        var days = dayIndexes.Select(i => calendar.Days.ElementAt(i)).ToList();
        foreach (var day in days)
        {
            day.SetAddress(new DeliveryAddress("Main St 1", "Springfield", new Coordinates(1, 1)));
            day.MarkReadyForProduction([new MenuItem("Lunch", 3, Guid.NewGuid(), "Salmon", 2)]);
            if (cancel)
                day.Cancel(new DateTimeOffset(2026, 7, 1, 0, 0, 0, TimeSpan.Zero));
        }

        await using var context = IntegrationTestSetup.CreateContext();
        new CalendarRepository(context).Add(calendar);
        await context.SaveChangesAsync();
        return days.Select(d => d.Id).ToArray();
    }

    private static async Task HandleAsync<T, TConsumer>(T message, Func<Infrastructure.Data.ApplicationDbContext, TConsumer> create)
        where T : Joseco.Communication.External.Contracts.Message.IntegrationMessage
        where TConsumer : class, Joseco.Communication.External.Contracts.Services.IIntegrationMessageConsumer<T>
    {
        await using var context = IntegrationTestSetup.CreateContext();
        var consumer = new IdempotentConsumer<T, TConsumer>(
            create(context), context, TimeProvider.System, NullLogger<IdempotentConsumer<T, TConsumer>>.Instance);
        await consumer.HandleAsync(message);
    }

    private static Task LinkAsync(Guid deliveryId, string packageId, string patientId = "p", int? deliveryDayId = null) =>
        HandleAsync(
            new DeliveryAddedToRoute("corr-3", "ms5")
            {
                RouteId = Guid.NewGuid(), DeliveryId = deliveryId, PackageId = packageId, PatientId = patientId, DeliveryDayId = deliveryDayId
            },
            ctx => new DeliveryAddedToRouteConsumer(ctx, TimeProvider.System, NullLogger<DeliveryAddedToRouteConsumer>.Instance));

    private static Task CompleteAsync(Guid deliveryId, string? photoUrl = null, string? signature = null) =>
        HandleAsync(
            new DeliveryCompleted("corr-4", "ms5")
            {
                DeliveryId = deliveryId, RouteId = Guid.NewGuid(), DeliveredAt = DateTime.UtcNow,
                EvidencePhotoUrl = photoUrl, DigitalSignature = signature
            },
            ctx => new DeliveryCompletedConsumer(ctx, NullLogger<DeliveryCompletedConsumer>.Instance));

    private static async Task<Domain.Enums.DayStatus> StatusOfAsync(int dayId)
    {
        await using var context = IntegrationTestSetup.CreateContext();
        var calendar = await new CalendarRepository(context).GetByDeliveryDayIdAsync(dayId, CancellationToken.None);
        return calendar!.Days.Single(d => d.Id == dayId).Status;
    }

    [Test]
    public async Task DeliveryCompleted_AfterLinking_ShouldMarkTheDayDelivered_WithEvidenceTimestamp()
    {
        var dayId = await SaveReadyDayAsync();
        var deliveryId = Guid.NewGuid();

        await LinkAsync(deliveryId, dayId.ToString());
        await CompleteAsync(deliveryId);

        await using var context = IntegrationTestSetup.CreateContext();
        var loaded = await new CalendarRepository(context).GetByDeliveryDayIdAsync(dayId, CancellationToken.None);
        var day = loaded!.Days.Single(d => d.Id == dayId);
        day.Status.ShouldBe(Domain.Enums.DayStatus.Delivered);
        day.Evidence.ShouldNotBeNull();
    }

    [Test]
    public async Task DeliveryCompleted_WithPhotoAndSignature_ShouldStoreThemAsEvidence()
    {
        var dayId = await SaveReadyDayAsync();
        var deliveryId = Guid.NewGuid();

        await LinkAsync(deliveryId, dayId.ToString());
        await CompleteAsync(deliveryId, "https://example.org/f.jpg", "https://example.org/firma.png");

        await using var context = IntegrationTestSetup.CreateContext();
        var loaded = await new CalendarRepository(context).GetByDeliveryDayIdAsync(dayId, CancellationToken.None);
        var evidence = loaded!.Days.Single(d => d.Id == dayId).Evidence;
        evidence.ShouldNotBeNull();
        evidence.PhotoUrl.ShouldBe("https://example.org/f.jpg");
        evidence.SignatureUrl.ShouldBe("https://example.org/firma.png");
    }

    [Test]
    public async Task DeliveryCompleted_WithASignatureLongerThanTheColumn_ShouldStillCloseTheDay_WithoutIt()
    {
        var dayId = await SaveReadyDayAsync();
        var deliveryId = Guid.NewGuid();

        await LinkAsync(deliveryId, dayId.ToString());
        await CompleteAsync(deliveryId, "https://example.org/f.jpg", new string('x', Domain.ValueObjects.DeliveryEvidence.MaxLength + 1));

        await using var context = IntegrationTestSetup.CreateContext();
        var loaded = await new CalendarRepository(context).GetByDeliveryDayIdAsync(dayId, CancellationToken.None);
        var day = loaded!.Days.Single(d => d.Id == dayId);
        day.Status.ShouldBe(Domain.Enums.DayStatus.Delivered);
        day.Evidence!.PhotoUrl.ShouldBe("https://example.org/f.jpg");
        day.Evidence.SignatureUrl.ShouldBeNull();
    }

    [Test]
    public async Task DeliveryFailed_AfterLinking_ShouldMarkTheDayFailed()
    {
        var dayId = await SaveReadyDayAsync();
        var deliveryId = Guid.NewGuid();

        await LinkAsync(deliveryId, dayId.ToString());
        await HandleAsync(
            new DeliveryFailed("corr-5", "ms5") { DeliveryId = deliveryId, RouteId = Guid.NewGuid(), Reason = "Patient absent" },
            ctx => new DeliveryFailedConsumer(ctx, TimeProvider.System, NullLogger<DeliveryFailedConsumer>.Instance));

        (await StatusOfAsync(dayId)).ShouldBe(Domain.Enums.DayStatus.Failed);
    }

    [Test]
    public async Task DeliveryCompleted_ReportedAgainWithNewMessageId_ShouldKeepTheDayDelivered()
    {
        var dayId = await SaveReadyDayAsync();
        var deliveryId = Guid.NewGuid();
        await LinkAsync(deliveryId, dayId.ToString());

        await CompleteAsync(deliveryId);
        await CompleteAsync(deliveryId);

        (await StatusOfAsync(dayId)).ShouldBe(Domain.Enums.DayStatus.Delivered);
    }

    [Test]
    public async Task DeliveryCompleted_ForUnlinkedDelivery_ShouldBeAcknowledgedWithoutChanges()
    {
        var dayId = await SaveReadyDayAsync();

        await Should.NotThrowAsync(() => CompleteAsync(Guid.NewGuid()));

        (await StatusOfAsync(dayId)).ShouldBe(Domain.Enums.DayStatus.ReadyForProduction);
    }

    [Test]
    public async Task DeliveryCompleted_AfterLinkingByDeliveryDayId_ShouldMarkTheDayDelivered()
    {
        // Packages from MS3's package-validated carry MS3's own GUID as PackageId; the day comes in DeliveryDayId.
        var dayId = await SaveReadyDayAsync();
        var deliveryId = Guid.NewGuid();

        await LinkAsync(deliveryId, Guid.NewGuid().ToString(), deliveryDayId: dayId);
        await CompleteAsync(deliveryId);

        (await StatusOfAsync(dayId)).ShouldBe(Domain.Enums.DayStatus.Delivered);
    }

    [Test]
    public async Task DeliveryAddedToRoute_WithNonNumericPackageId_ShouldNotCreateALink()
    {
        await LinkAsync(Guid.NewGuid(), "manual-package-7");

        await using var context = IntegrationTestSetup.CreateContext();
        (await context.DeliveryRouteLinks.CountAsync()).ShouldBe(0);
    }

    [Test]
    public async Task DeliveryAddedToRoute_WithMs3PackageGuid_ShouldLinkByPatient_AndCloseTheDay()
    {
        var patientId = Guid.NewGuid();
        var dayId = await SaveReadyDayAsync(patientId: patientId);
        var deliveryId = Guid.NewGuid();

        await LinkAsync(deliveryId, Guid.NewGuid().ToString(), patientId.ToString());
        await CompleteAsync(deliveryId);

        (await StatusOfAsync(dayId)).ShouldBe(Domain.Enums.DayStatus.Delivered);
    }

    [Test]
    public async Task DeliveryAddedToRoute_ByPatient_ShouldTakeTheEarliestDayNotLinkedYet()
    {
        var patientId = Guid.NewGuid();
        var dayIds = await SaveReadyDaysAsync(patientId, [1, 0]);
        var (laterDay, earlierDay) = (dayIds[0], dayIds[1]);
        var (first, second) = (Guid.NewGuid(), Guid.NewGuid());

        await LinkAsync(first, Guid.NewGuid().ToString(), patientId.ToString());
        await LinkAsync(second, Guid.NewGuid().ToString(), patientId.ToString());

        await using var context = IntegrationTestSetup.CreateContext();
        (await context.DeliveryRouteLinks.SingleAsync(l => l.DeliveryId == first)).DayId.ShouldBe(earlierDay);
        (await context.DeliveryRouteLinks.SingleAsync(l => l.DeliveryId == second)).DayId.ShouldBe(laterDay);
    }

    [Test]
    public async Task DeliveryAddedToRoute_ByPatient_WithNoDayInProduction_ShouldNotCreateALink()
    {
        var patientId = Guid.NewGuid();
        await SaveReadyDayAsync(cancel: true, patientId: patientId);

        await LinkAsync(Guid.NewGuid(), Guid.NewGuid().ToString(), patientId.ToString());

        await using var context = IntegrationTestSetup.CreateContext();
        (await context.DeliveryRouteLinks.CountAsync()).ShouldBe(0);
    }

    [Test]
    public async Task DeliveryCompleted_ForACancelledDay_ShouldBeAcknowledgedAndLeaveTheDayCancelled()
    {
        var dayId = await SaveReadyDayAsync(cancel: true);
        var deliveryId = Guid.NewGuid();
        await LinkAsync(deliveryId, dayId.ToString());

        await Should.NotThrowAsync(() => CompleteAsync(deliveryId));

        (await StatusOfAsync(dayId)).ShouldBe(Domain.Enums.DayStatus.Cancelled);
    }
}
