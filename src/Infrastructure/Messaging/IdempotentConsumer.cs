using delivery_schedule_management.Infrastructure.Data;
using Joseco.Communication.External.Contracts.Message;
using Joseco.Communication.External.Contracts.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace delivery_schedule_management.Infrastructure.Messaging;

/// <summary>
/// Inbox decorator: RabbitMQ delivers at-least-once, so the same message can arrive twice. The
/// claim (an <see cref="InboxMessage"/> row keyed by message id) and the inner consumer's changes
/// are committed in ONE <c>SaveChanges</c>: either both land or neither does.
/// </summary>
/// <remarks>
/// <typeparamref name="TInner"/> is the concrete consumer, not <see cref="IIntegrationMessageConsumer{T}"/>:
/// <c>AddRabbitMqConsumer</c> registers the decorated pipeline under that interface, so resolving the
/// inner one through it would be a circular dependency. Inner consumers never call SaveChanges.
/// </remarks>
public sealed class IdempotentConsumer<T, TInner>(
    TInner inner,
    ApplicationDbContext context,
    TimeProvider timeProvider,
    ILogger<IdempotentConsumer<T, TInner>> logger) : IIntegrationMessageConsumer<T>
    where T : IntegrationMessage
    where TInner : class, IIntegrationMessageConsumer<T>
{
    public async Task HandleAsync(T message, CancellationToken cancellationToken = default)
    {
        if (await context.InboxMessages.AsNoTracking().AnyAsync(m => m.MessageId == message.Id, cancellationToken))
        {
            logger.LogInformation("Skipping {MessageType} {MessageId}: already in the inbox.", typeof(T).Name, message.Id);
            return;
        }

        context.InboxMessages.Add(new InboxMessage(
            message.Id, typeof(T).Name, message.CorrelationId, message.CreatedAt, timeProvider.GetUtcNow()));

        await inner.HandleAsync(message, cancellationToken);

        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // A concurrent delivery of the same message won the primary-key race.
            logger.LogInformation("{MessageType} {MessageId} was claimed by a concurrent delivery; skipping.",
                typeof(T).Name, message.Id);
        }
    }
}
