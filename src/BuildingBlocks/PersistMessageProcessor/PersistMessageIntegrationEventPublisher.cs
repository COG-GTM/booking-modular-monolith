using BuildingBlocks.Core.Event;

namespace BuildingBlocks.PersistMessageProcessor;

public class PersistMessageIntegrationEventPublisher(IPersistMessageProcessor persistMessageProcessor)
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

public sealed class PersistMessageIntegrationEventPublisher<TModule>(
    IPersistMessageProcessor<TModule> persistMessageProcessor
) : PersistMessageIntegrationEventPublisher(persistMessageProcessor), IIntegrationEventPublisher<TModule>
    where TModule : class;
