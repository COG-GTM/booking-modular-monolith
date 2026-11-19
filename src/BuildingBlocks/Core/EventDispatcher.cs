using BuildingBlocks.Core.Event;
using Microsoft.Extensions.Logging;
using MessageEnvelope = BuildingBlocks.Core.Event.MessageEnvelope;

namespace BuildingBlocks.Core;

public class EventDispatcher(
    IEnumerable<IEventMapper> eventMappers,
    ILogger<EventDispatcher> logger,
    IIntegrationEventPublisher integrationEventPublisher,
    IEnumerable<IEventHeadersProvider> eventHeadersProviders
) : IEventDispatcher
{
    private readonly CompositeEventMapper _eventMapper = new(eventMappers);

    public async Task SendAsync<T>(
        IReadOnlyList<T> events,
        Type type = null,
        CancellationToken cancellationToken = default
    )
        where T : IEvent
    {
        if (events.Count > 0)
        {
            var eventType =
                type != null && type.IsAssignableTo(typeof(IInternalCommand))
                    ? EventType.InternalCommand
                    : EventType.DomainEvent;

            async Task PublishIntegrationEvent(IReadOnlyList<IIntegrationEvent> integrationEvents)
            {
                foreach (var integrationEvent in integrationEvents)
                {
                    await integrationEventPublisher.PublishAsync(
                        new MessageEnvelope(integrationEvent, SetHeaders()),
                        cancellationToken
                    );
                }
            }

            switch (events)
            {
                case IReadOnlyList<IDomainEvent> domainEvents:
                {
                    var integrationEvents = await MapDomainEventToIntegrationEventAsync(domainEvents)
                        .ConfigureAwait(false);

                    await PublishIntegrationEvent(integrationEvents);
                    break;
                }

                case IReadOnlyList<IIntegrationEvent> integrationEvents:
                    await PublishIntegrationEvent(integrationEvents);
                    break;
            }

            if (type != null && eventType == EventType.InternalCommand)
            {
                var internalMessages = await MapDomainEventToInternalCommandAsync(events as IReadOnlyList<IDomainEvent>)
                    .ConfigureAwait(false);

                foreach (var internalMessage in internalMessages)
                {
                    await integrationEventPublisher.AddInternalCommandAsync(internalMessage, cancellationToken);
                }
            }
        }
    }

    public async Task SendAsync<T>(T @event, Type type = null, CancellationToken cancellationToken = default)
        where T : IEvent => await SendAsync(new[] { @event }, type, cancellationToken);

    private Task<IReadOnlyList<IIntegrationEvent>> MapDomainEventToIntegrationEventAsync(
        IReadOnlyList<IDomainEvent> events
    )
    {
        logger.LogTrace("Processing integration events start...");

        var wrappedIntegrationEvents = GetWrappedIntegrationEvents(events.ToList())?.ToList();
        if (wrappedIntegrationEvents?.Count > 0)
            return Task.FromResult<IReadOnlyList<IIntegrationEvent>>(wrappedIntegrationEvents);

        var integrationEvents = new List<IIntegrationEvent>();
        foreach (var @event in events)
        {
            var eventType = @event.GetType();
            logger.LogTrace($"Handling domain event: {eventType.Name}");

            var integrationEvent = _eventMapper.MapToIntegrationEvent(@event);

            if (integrationEvent is null)
                continue;

            integrationEvents.Add(integrationEvent);
        }

        logger.LogTrace("Processing integration events done...");

        return Task.FromResult<IReadOnlyList<IIntegrationEvent>>(integrationEvents);
    }

    private Task<IReadOnlyList<IInternalCommand>> MapDomainEventToInternalCommandAsync(
        IReadOnlyList<IDomainEvent> events
    )
    {
        logger.LogTrace("Processing internal message start...");

        var internalCommands = new List<IInternalCommand>();
        foreach (var @event in events)
        {
            var eventType = @event.GetType();
            logger.LogTrace($"Handling domain event: {eventType.Name}");

            var integrationEvent = _eventMapper.MapToInternalCommand(@event);

            if (integrationEvent is null)
                continue;

            internalCommands.Add(integrationEvent);
        }

        logger.LogTrace("Processing internal message done...");

        return Task.FromResult<IReadOnlyList<IInternalCommand>>(internalCommands);
    }

    private IEnumerable<IIntegrationEvent> GetWrappedIntegrationEvents(IReadOnlyList<IDomainEvent> domainEvents)
    {
        foreach (var domainEvent in domainEvents.Where(x => x is IHaveIntegrationEvent))
        {
            var genericType = typeof(IntegrationEventWrapper<>).MakeGenericType(domainEvent.GetType());

            var domainNotificationEvent = (IIntegrationEvent)Activator.CreateInstance(genericType, domainEvent);

            yield return domainNotificationEvent;
        }
    }

    private IDictionary<string, object?> SetHeaders()
    {
        var headers = new Dictionary<string, object?>();

        foreach (var provider in eventHeadersProviders)
        {
            foreach (var header in provider.GetHeaders())
                headers[header.Key] = header.Value;
        }

        return headers;
    }
}

public sealed class EventDispatcher<TModule>(
    IEnumerable<IEventMapper> eventMappers,
    ILogger<EventDispatcher> logger,
    IIntegrationEventPublisher<TModule> integrationEventPublisher,
    IEnumerable<IEventHeadersProvider> eventHeadersProviders
) : EventDispatcher(eventMappers, logger, integrationEventPublisher, eventHeadersProviders), IEventDispatcher<TModule>;
