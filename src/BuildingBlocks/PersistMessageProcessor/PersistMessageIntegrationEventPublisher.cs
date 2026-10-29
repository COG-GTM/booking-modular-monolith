using BuildingBlocks.Core;
using BuildingBlocks.Core.Event;

namespace BuildingBlocks.PersistMessageProcessor;

// Routes EventDispatcher output through the persist-message outbox / internal-command store.
public sealed class PersistMessageIntegrationEventPublisher(IPersistMessageProcessor persistMessageProcessor)
    : IIntegrationEventPublisher
{
    public Task PublishAsync(MessageEnvelope messageEnvelope, CancellationToken cancellationToken = default) =>
        persistMessageProcessor.PublishMessageAsync(messageEnvelope, cancellationToken);

    public Task AddInternalCommandAsync<TCommand>(TCommand internalCommand, CancellationToken cancellationToken = default)
        where TCommand : class, IInternalCommand =>
        persistMessageProcessor.AddInternalMessageAsync(internalCommand, cancellationToken);
}
