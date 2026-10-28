using BuildingBlocks.Core.Event;

namespace BuildingBlocks.Core;

public interface IEventDispatcher<TModule> : IEventDispatcher { }

public sealed class ModuleEventDispatcher<TModule>(IEventDispatcher dispatcher) : IEventDispatcher<TModule>
{
    public Task SendAsync<T>(IReadOnlyList<T> events, Type type = null, CancellationToken cancellationToken = default)
        where T : IEvent => dispatcher.SendAsync(events, type, cancellationToken);

    public Task SendAsync<T>(T @event, Type type = null, CancellationToken cancellationToken = default)
        where T : IEvent => dispatcher.SendAsync(@event, type, cancellationToken);
}
