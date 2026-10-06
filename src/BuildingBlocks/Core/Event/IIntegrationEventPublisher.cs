namespace BuildingBlocks.Core.Event;

public interface IIntegrationEventPublisher
{
    Task PublishAsync(MessageEnvelope messageEnvelope, CancellationToken cancellationToken = default);

    Task AddInternalMessageAsync<TCommand>(TCommand internalCommand, CancellationToken cancellationToken = default)
        where TCommand : class, IInternalCommand;
}

// Publisher bound to the outbox owned by TModule (the module's root marker type, e.g. FlightRoot).
public interface IIntegrationEventPublisher<TModule> : IIntegrationEventPublisher
    where TModule : class;
