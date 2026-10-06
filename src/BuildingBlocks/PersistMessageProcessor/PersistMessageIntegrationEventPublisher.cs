using BuildingBlocks.Core.Event;

namespace BuildingBlocks.PersistMessageProcessor;

public class PersistMessageIntegrationEventPublisher<TModule>(IPersistMessageProcessor<TModule> persistMessageProcessor)
    : IIntegrationEventPublisher<TModule>
    where TModule : class
{
    public Task PublishAsync(MessageEnvelope messageEnvelope, CancellationToken cancellationToken = default)
    {
        return persistMessageProcessor.PublishMessageAsync(messageEnvelope, cancellationToken);
    }

    public Task AddInternalMessageAsync<TCommand>(TCommand internalCommand, CancellationToken cancellationToken = default)
        where TCommand : class, IInternalCommand
    {
        return persistMessageProcessor.AddInternalMessageAsync(internalCommand, cancellationToken);
    }
}
