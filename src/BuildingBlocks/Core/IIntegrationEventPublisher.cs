using BuildingBlocks.Core.Event;

namespace BuildingBlocks.Core;

// Sink used by EventDispatcher; implemented by the persist-message (outbox) library or any other transport.
public interface IIntegrationEventPublisher
{
    Task PublishAsync(MessageEnvelope messageEnvelope, CancellationToken cancellationToken = default);

    Task AddInternalCommandAsync<TCommand>(TCommand internalCommand, CancellationToken cancellationToken = default)
        where TCommand : class, IInternalCommand;
}

public interface IIntegrationEventPublisher<TModule> : IIntegrationEventPublisher;
