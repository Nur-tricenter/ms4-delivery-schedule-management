using delivery_schedule_management.Infrastructure.Messaging;
using Joseco.Communication.External.Contracts.Message;
using Joseco.Communication.External.Contracts.Services;
using Joseco.Communication.External.RabbitMQ;
using Joseco.Communication.External.RabbitMQ.Services;
using Microsoft.Extensions.Configuration;
using Nur.Tricenter.Integration.Messages.Catalog;
using Nur.Tricenter.Integration.Messages.Logistics;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// RabbitMQ consumers for the Nur-Tricenter platform. Called by the WorkerService only — the API
/// process never touches the broker.
/// </summary>
/// <remarks>
/// The queues and their fanout bindings are NOT declared here: nur-tricenter-local's
/// <c>topology/rabbitmq-definitions.json</c> owns them (<c>{consumer}.{exchange}</c> naming). Joseco
/// 1.1.0 ignores <c>RabbitMqSettings:Port</c>, so the broker must listen on 5672.
/// <para>
/// Not consumed on purpose (queues exist but nothing in MS4 uses them): <c>ms4.recipe-published</c>
/// (a recipe can't be on a resolved menu before it exists), <c>ms4.food-*</c>, <c>ms4.user-*</c>.
/// </para>
/// </remarks>
public static class MessagingExtensions
{
    public const string MealPlanPublishedQueue = "ms4.meal-plan-published";
    public const string RecipeUpdatedQueue = "ms4.recipe-updated";
    public const string DeliveryAddedToRouteQueue = "ms4.delivery-added-to-route";
    public const string DeliveryCompletedQueue = "ms4.delivery-completed";
    public const string DeliveryFailedQueue = "ms4.delivery-failed";

    public static IServiceCollection AddPlatformMessaging(this IServiceCollection services, IConfiguration configuration)
    {
        var settings = configuration.GetSection("RabbitMqSettings").Get<RabbitMqSettings>()
            ?? throw new InvalidOperationException("Configuration section 'RabbitMqSettings' is missing or empty.");

        // Must precede AddRabbitMQ, which only TryAdds its own copy.
        services.AddSingleton(settings);
        services.AddRabbitMQ(settings);

        services.AddConsumer<MealPlanPublished, MealPlanPublishedConsumer>(MealPlanPublishedQueue);
        services.AddConsumer<RecipeUpdated, RecipeUpdatedConsumer>(RecipeUpdatedQueue);
        services.AddConsumer<DeliveryAddedToRoute, DeliveryAddedToRouteConsumer>(DeliveryAddedToRouteQueue);
        services.AddConsumer<DeliveryCompleted, DeliveryCompletedConsumer>(DeliveryCompletedQueue);
        services.AddConsumer<DeliveryFailed, DeliveryFailedConsumer>(DeliveryFailedQueue);

        return services;
    }

    /// <summary>Registers <c>Tracing → Idempotent(inbox) → TConsumer</c> on <paramref name="queue"/>.</summary>
    private static void AddConsumer<T, TConsumer>(this IServiceCollection services, string queue)
        where T : IntegrationMessage
        where TConsumer : class, IIntegrationMessageConsumer<T>
    {
        services.AddTransient<TConsumer>();
        services.AddTransient<IdempotentConsumer<T, TConsumer>>();
        services.AddTransient<TracingConsumer<T, IdempotentConsumer<T, TConsumer>>>();
        services.AddRabbitMqConsumer<T, TracingConsumer<T, IdempotentConsumer<T, TConsumer>>>(queue);
    }
}
