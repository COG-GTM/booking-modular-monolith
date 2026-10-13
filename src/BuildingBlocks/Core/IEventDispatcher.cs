using BuildingBlocks.Core.Event;

namespace BuildingBlocks.Core;

public interface IEventDispatcher
{
    public Task SendAsync<T>(IReadOnlyList<T> events, Type type = null, CancellationToken cancellationToken = default)
        where T : IEvent;
    public Task SendAsync<T>(T @event, Type type = null, CancellationToken cancellationToken = default)
        where T : IEvent;
}

// Dispatches events through the outbox/inbox store owned by TModule.
public interface IEventDispatcher<TModule> : IEventDispatcher
    where TModule : class;
