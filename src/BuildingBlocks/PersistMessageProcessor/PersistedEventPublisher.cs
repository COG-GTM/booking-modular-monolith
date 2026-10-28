using BuildingBlocks.Core;
using BuildingBlocks.Core.Event;

namespace BuildingBlocks.PersistMessageProcessor;

public sealed class PersistedEventPublisher<TModule>(IPersistMessageProcessor<TModule> processor)
    : IEventPublisher<TModule>
{
    public Task PublishAsync(MessageEnvelope message, CancellationToken cancellationToken = default) =>
        processor.PublishMessageAsync(message, cancellationToken);

    public Task SendInternalAsync(IInternalCommand command, CancellationToken cancellationToken = default) =>
        processor.AddInternalMessageAsync(command, cancellationToken);
}
