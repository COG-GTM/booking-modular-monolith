using BuildingBlocks.Core.Event;

namespace BuildingBlocks.Core;

public interface IEventDispatcher
{
    public Task SendAsync<T>(IReadOnlyList<T> events, Type type = null, CancellationToken cancellationToken = default)
        where T : IEvent;

    public Task SendAsync<T>(T @event, Type type = null, CancellationToken cancellationToken = default)
        where T : IEvent;
}

// Dispatcher bound to the outbox owned by TModule (the module's root marker type, e.g. FlightRoot).
public interface IEventDispatcher<TModule> : IEventDispatcher
    where TModule : class;
