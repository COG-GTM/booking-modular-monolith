namespace BuildingBlocks.Core.Event;

public interface IIntegrationEventPublisher
{
    Task PublishAsync(MessageEnvelope messageEnvelope, CancellationToken cancellationToken = default);

    Task AddInternalMessageAsync<TCommand>(TCommand internalCommand, CancellationToken cancellationToken = default)
        where TCommand : class, IInternalCommand;
}

// Publisher backed by the outbox/inbox store owned by TModule.
public interface IIntegrationEventPublisher<TModule> : IIntegrationEventPublisher
    where TModule : class;
