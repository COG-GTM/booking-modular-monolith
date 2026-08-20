namespace BuildingBlocks.Core.Event;

public interface IIntegrationEventPublisher
{
    Task PublishAsync(MessageEnvelope messageEnvelope, CancellationToken cancellationToken = default);

    Task AddInternalMessageAsync<TCommand>(TCommand internalCommand, CancellationToken cancellationToken = default)
        where TCommand : class, IInternalCommand;
}
