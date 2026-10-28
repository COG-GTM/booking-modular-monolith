using BuildingBlocks.Core.Event;

namespace BuildingBlocks.Core;

public sealed class EventDispatcher(
    IEnumerable<IEventMapper> mappers,
    IEventPublisher publisher,
    IEventHeadersProvider headersProvider
) : IEventDispatcher
{
    private readonly CompositeEventMapper _mapper = new(mappers);

    public async Task SendAsync<T>(
        IReadOnlyList<T> events,
        Type type = null,
        CancellationToken cancellationToken = default
    )
        where T : IEvent
    {
        foreach (var @event in events)
        {
            var integrationEvent = @event switch
            {
                IIntegrationEvent integration => integration,
                IDomainEvent domain when domain is IHaveIntegrationEvent => (IIntegrationEvent)
                    Activator.CreateInstance(
                        typeof(IntegrationEventWrapper<>).MakeGenericType(domain.GetType()),
                        domain
                    )!,
                IDomainEvent domain => _mapper.MapToIntegrationEvent(domain),
                _ => null,
            };

            if (integrationEvent is not null)
            {
                await publisher.PublishAsync(
                    new MessageEnvelope(integrationEvent, headersProvider.GetHeaders()),
                    cancellationToken
                );
            }
        }

        if (type?.IsAssignableTo(typeof(IInternalCommand)) == true)
        {
            foreach (var domain in events.OfType<IDomainEvent>())
            {
                var command = _mapper.MapToInternalCommand(domain);
                if (command is not null)
                    await publisher.SendInternalAsync(command, cancellationToken);
            }
        }
    }

    public Task SendAsync<T>(T @event, Type type = null, CancellationToken cancellationToken = default)
        where T : IEvent => SendAsync(new[] { @event }, type, cancellationToken);
}
