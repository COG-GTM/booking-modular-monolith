using BuildingBlocks.Core.Event;

namespace BuildingBlocks.PersistMessageProcessor;

public sealed class PersistMessageIntegrationEventPublisher(IPersistMessageProcessor persistMessageProcessor)
    : IIntegrationEventPublisher
{
    public Task PublishAsync(MessageEnvelope messageEnvelope, CancellationToken cancellationToken = default) =>
        persistMessageProcessor.PublishMessageAsync(messageEnvelope, cancellationToken);

    public Task AddInternalMessageAsync<TCommand>(
        TCommand internalCommand,
        CancellationToken cancellationToken = default
    )
        where TCommand : class, IInternalCommand =>
        persistMessageProcessor.AddInternalMessageAsync(internalCommand, cancellationToken);
}
