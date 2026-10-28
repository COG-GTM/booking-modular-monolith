using BuildingBlocks.Core.Event;

namespace BuildingBlocks.Core;

public interface IEventPublisher
{
    Task PublishAsync(MessageEnvelope message, CancellationToken cancellationToken = default);
    Task SendInternalAsync(IInternalCommand command, CancellationToken cancellationToken = default);
}
