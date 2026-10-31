using delivery_schedule_management.Application.Calendars.Commands.CreateSchedule;
using delivery_schedule_management.Domain.Entities;
using delivery_schedule_management.Domain.ValueObjects;
using delivery_schedule_management.Infrastructure.Data.Repositories;
using delivery_schedule_management.Infrastructure.Messaging;
using FluentValidation.Results;
using Joseco.Communication.External.Contracts.Message;
using Joseco.Communication.External.Contracts.Services;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Newtonsoft.Json;
using Nur.Tricenter.Integration.Messages.Catalog;
using Nur.Tricenter.Integration.Messages.Contracting;
using Nur.Tricenter.Integration.Messages.DeliveryCalendar;
using AppValidationException = delivery_schedule_management.Application.Common.Exceptions.ValidationException;

namespace delivery_schedule_management.Infrastructure.IntegrationTests;

/// <summary>
/// The messages that replaced MS4's synchronous REST calls, against real Postgres: the meal plan
/// projection behind <c>ICatalogPort</c>, the contract-activated consumer and the outbox that carries
/// <c>ProductionOrderReady</c> out.
/// </summary>
public class AsyncMessagingTests : RepositoryTestBase
{
    private static readonly Guid PlanId = Guid.Parse("aaaaaaaa-0000-0000-0000-0000000000a1");
    private static readonly Guid RecipeId = Guid.Parse("bbbbbbbb-0000-0000-0000-0000000000b1");

    private static MealPlanCompositionChanged Composition(string recipeName, int rations = 2, DateTimeOffset? createdAt = null, int dayNumber = 1)
    {
        var message = new MealPlanCompositionChanged("corr-c", "catalog")
        {
            PlanId = PlanId,
            Days =
            [
                new MealPlanDay
                {
                    DayNumber = dayNumber,
                    MealTimes =
                    [
                        new MealPlanMealTime
                        {
                            Name = "Lunch", Order = 3,
                            Recipes = [new MealPlanRecipe { RecipeId = RecipeId, RecipeName = recipeName, Rations = rations }]
                        },
                        new MealPlanMealTime
                        {
                            Name = "Breakfast", Order = 1,
                            Recipes = [new MealPlanRecipe { RecipeId = Guid.NewGuid(), RecipeName = "Oats", Rations = 1 }]
                        }
                    ]
                }
            ]
        };
        return createdAt is null ? message : message with { CreatedAt = createdAt.Value };
    }

    private static Task ProjectAsync(MealPlanCompositionChanged message) =>
        HandleAsync(message, ctx => new MealPlanCompositionChangedConsumer(ctx, NullLogger<MealPlanCompositionChangedConsumer>.Instance));

    private static async Task HandleAsync<T, TConsumer>(T message, Func<Infrastructure.Data.ApplicationDbContext, TConsumer> create)
        where T : IntegrationMessage
        where TConsumer : class, IIntegrationMessageConsumer<T>
    {
        await using var context = IntegrationTestSetup.CreateContext();
        var consumer = new IdempotentConsumer<T, TConsumer>(
            create(context), context, TimeProvider.System, NullLogger<IdempotentConsumer<T, TConsumer>>.Instance);
        await consumer.HandleAsync(message);
    }

    private static async Task<List<(string MealTime, string RecipeName, int Rations)>?> MenuAsync(int dayNumber)
    {
        await using var context = IntegrationTestSetup.CreateContext();
        var menu = await new ProjectedCatalogPort(context).GetDayMenuAsync(PlanId, dayNumber, CancellationToken.None);
        return menu?.Items.Select(i => (i.MealTime, i.RecipeName, i.Rations)).ToList();
    }

    // ---- meal plan composition projection (replaces MS4 -> MS2 REST) ----

    [Test]
    public async Task MealPlanCompositionChanged_ShouldProjectTheDayMenu_OrderedByMealTime()
    {
        await ProjectAsync(Composition("Quinoa bowl"));

        var menu = await MenuAsync(1);

        menu.ShouldNotBeNull();
        menu.Select(i => i.MealTime).ToList().ShouldBe(["Breakfast", "Lunch"]);
        menu.Single(i => i.MealTime == "Lunch").ShouldBe(("Lunch", "Quinoa bowl", 2));
    }

    [Test]
    public async Task ProjectedCatalogPort_ShouldReturnNull_ForAPlanDayNobodyAnnounced()
    {
        await ProjectAsync(Composition("Quinoa bowl"));

        (await MenuAsync(2)).ShouldBeNull();
    }

    [Test]
    public async Task NewerSnapshot_ShouldReplaceTheDayMenu_WithoutDuplicatingRows()
    {
        var t0 = DateTimeOffset.UtcNow;
        await ProjectAsync(Composition("Quinoa bowl", createdAt: t0));
        await ProjectAsync(Composition("Quinoa bowl v2", rations: 3, createdAt: t0.AddSeconds(5)));

        var menu = await MenuAsync(1);

        menu!.Count.ShouldBe(2);
        menu.Single(i => i.MealTime == "Lunch").ShouldBe(("Lunch", "Quinoa bowl v2", 3));
        await using var context = IntegrationTestSetup.CreateContext();
        (await context.PlanDayMenus.CountAsync()).ShouldBe(1);
    }

    [Test]
    public async Task OlderSnapshotArrivingLate_ShouldBeIgnored()
    {
        var t0 = DateTimeOffset.UtcNow;
        await ProjectAsync(Composition("Quinoa bowl v2", createdAt: t0.AddSeconds(5)));
        await ProjectAsync(Composition("Quinoa bowl", createdAt: t0));

        (await MenuAsync(1))!.Single(i => i.MealTime == "Lunch").RecipeName.ShouldBe("Quinoa bowl v2");
    }

    [Test]
    public async Task SnapshotWithAnEmptiedMealTime_ShouldProjectAnEmptyMenu()
    {
        await ProjectAsync(Composition("Quinoa bowl", createdAt: DateTimeOffset.UtcNow));
        var emptied = new MealPlanCompositionChanged("corr-c", "catalog")
        {
            PlanId = PlanId,
            Days = [new MealPlanDay { DayNumber = 1, MealTimes = [new MealPlanMealTime { Name = "Lunch", Order = 3 }] }],
            CreatedAt = DateTimeOffset.UtcNow.AddSeconds(5)
        };

        await ProjectAsync(emptied);

        (await MenuAsync(1))!.ShouldBeEmpty();
    }

    [Test]
    public async Task SameCompositionDeliveredTwice_ShouldBeProcessedOnce()
    {
        var message = Composition("Quinoa bowl");

        await ProjectAsync(message);
        await ProjectAsync(message);

        await using var context = IntegrationTestSetup.CreateContext();
        (await context.InboxMessages.CountAsync(m => m.MessageId == message.Id)).ShouldBe(1);
    }

    // ---- contract activated (replaces MS3 -> MS4 REST POST /schedule) ----

    private sealed class FakeSender(Func<object, object?> onSend) : ISender
    {
        public List<object> Requests { get; } = [];

        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            return Task.FromResult((TResponse)onSend(request)!);
        }

        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default) where TRequest : IRequest =>
            throw new NotSupportedException();

        public Task<object?> Send(object request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private static ContractActivated Activated() => new("corr-a", "contracting")
    {
        ContractId = Guid.NewGuid(), PatientId = Guid.NewGuid(), CatalogPlanId = PlanId,
        DurationDays = 15, StartDate = new DateOnly(2026, 9, 1)
    };

    [Test]
    public async Task ContractActivated_ShouldSendCreateScheduleCommand_WithTheMessageData()
    {
        var sender = new FakeSender(_ => new CreateScheduleResult(7, []));
        var message = Activated();

        await new ContractActivatedConsumer(sender, NullLogger<ContractActivatedConsumer>.Instance).HandleAsync(message);

        sender.Requests.Single().ShouldBe(new CreateScheduleCommand(
            message.ContractId, message.PatientId, message.CatalogPlanId, 15, new DateOnly(2026, 9, 1)));
    }

    [Test]
    public async Task ContractActivated_RejectedByValidation_ShouldBeAcknowledged_NotRetried()
    {
        var sender = new FakeSender(_ => throw new AppValidationException([new ValidationFailure("Duration", "Duration must be 15 or 30 days.")]));

        await Should.NotThrowAsync(() =>
            new ContractActivatedConsumer(sender, NullLogger<ContractActivatedConsumer>.Instance).HandleAsync(Activated()));
    }

    // ---- production order ready (replaces MS3 -> MS4 REST GET active-tomorrow) ----

    private static async Task<(ProductionOrder Order, DeliveryCalendar Calendar)> SaveOrderAsync()
    {
        var deliveryDate = new DateOnly(2026, 8, 10);
        var calendar = DeliveryCalendar.Create(
            Guid.NewGuid(), new ContractReference(Guid.NewGuid()), new PlanReference(PlanId),
            new PlanDuration(15), new DeliveryDate(deliveryDate));
        var day = calendar.Days.First();
        day.SetAddress(new DeliveryAddress("Main St 1", "Springfield", new Coordinates(-17.78, -63.18)));
        day.MarkReadyForProduction([new MenuItem("Lunch", 3, RecipeId, "Quinoa bowl", 2)]);

        await using var context = IntegrationTestSetup.CreateContext();
        new CalendarRepository(context).Add(calendar);
        await context.SaveChangesAsync();

        var order = Domain.Services.OrderConsolidationService.Consolidate([calendar], new DeliveryDate(deliveryDate), null, DateTimeOffset.UtcNow);
        new ProductionOrderRepository(context).Add(order);
        await context.SaveChangesAsync();
        return (order, calendar);
    }

    private sealed class FakePublisher : IExternalPublisher
    {
        public List<(IntegrationMessage Message, string? Destination, bool Declare)> Published { get; } = [];

        public bool Fail { get; set; }

        public Task PublishAsync<T>(T message, string? destination = null, bool declareDestination = false) where T : IntegrationMessage
        {
            if (Fail)
                throw new InvalidOperationException("broker down");

            Published.Add((message, destination, declareDestination));
            return Task.CompletedTask;
        }
    }

    private static async Task<int> DispatchAsync(FakePublisher publisher)
    {
        await using var context = IntegrationTestSetup.CreateContext();
        return await new OutboxPublisher(context, publisher, TimeProvider.System, NullLogger<OutboxPublisher>.Instance)
            .PublishPendingAsync(CancellationToken.None);
    }

    private static async Task StageEventAsync(ProductionOrder order, DeliveryCalendar calendar)
    {
        await using var context = IntegrationTestSetup.CreateContext();
        var loaded = await new CalendarRepository(context).GetByIdAsync(calendar.Id, CancellationToken.None);
        new ProductionOrderEvents(context, TimeProvider.System).ProductionOrderReady(order, [loaded!]);
        await context.SaveChangesAsync();
    }

    [Test]
    public async Task ProductionOrderReady_ShouldBeStaged_OnlyWhenTheCallerSaves()
    {
        var (order, calendar) = await SaveOrderAsync();

        await using (var context = IntegrationTestSetup.CreateContext())
        {
            var loaded = await new CalendarRepository(context).GetByIdAsync(calendar.Id, CancellationToken.None);
            new ProductionOrderEvents(context, TimeProvider.System).ProductionOrderReady(order, [loaded!]);
            await using var other = IntegrationTestSetup.CreateContext();
            (await other.OutboxMessages.CountAsync()).ShouldBe(0);
            await context.SaveChangesAsync();
        }

        await using var check = IntegrationTestSetup.CreateContext();
        var row = await check.OutboxMessages.SingleAsync();
        row.Type.ShouldBe("ProductionOrderReady");
        row.Destination.ShouldBe("production-order-ready");
        row.PublishedAt.ShouldBeNull();
    }

    [Test]
    public async Task OutboxPublisher_ShouldPublishTheFullSnapshot_ThenMarkItPublished()
    {
        var (order, calendar) = await SaveOrderAsync();
        await StageEventAsync(order, calendar);
        var publisher = new FakePublisher();

        (await DispatchAsync(publisher)).ShouldBe(1);
        (await DispatchAsync(publisher)).ShouldBe(0); // already published

        var (message, destination, declare) = publisher.Published.ShouldHaveSingleItem();
        destination.ShouldBe("production-order-ready");
        declare.ShouldBeTrue();
        var ready = message.ShouldBeOfType<ProductionOrderReady>();
        ready.ProductionOrderId.ShouldBe(order.Id);
        ready.DeliveryDate.ShouldBe(new DateOnly(2026, 8, 10));
        ready.ProductionDate.ShouldBe(new DateOnly(2026, 8, 9));
        var line = ready.Lines.ShouldHaveSingleItem();
        line.DeliveryDayId.ShouldBe(calendar.Days.First().Id);
        line.PlanId.ShouldBe(PlanId);
        line.Address.ShouldBe("Main St 1, Springfield");
        line.Latitude.ShouldBe(-17.78);
        var item = line.Menu.ShouldHaveSingleItem();
        (item.MealTime, item.RecipeId, item.RecipeName, item.Rations).ShouldBe(("Lunch", RecipeId, "Quinoa bowl", 2));

        await using var check = IntegrationTestSetup.CreateContext();
        (await check.OutboxMessages.SingleAsync()).PublishedAt.ShouldNotBeNull();
    }

    [Test]
    public async Task OutboxPublisher_WhenTheBrokerFails_ShouldKeepTheMessagePending_AndKeepItsId()
    {
        var (order, calendar) = await SaveOrderAsync();
        await StageEventAsync(order, calendar);
        var publisher = new FakePublisher { Fail = true };

        (await DispatchAsync(publisher)).ShouldBe(0);
        publisher.Fail = false;
        (await DispatchAsync(publisher)).ShouldBe(1);

        await using var check = IntegrationTestSetup.CreateContext();
        var payload = JsonConvert.DeserializeObject<ProductionOrderReady>((await check.OutboxMessages.SingleAsync()).Payload)!;
        publisher.Published.Single().Message.Id.ShouldBe(payload.Id); // a retry must not mint a new message id
    }
}
