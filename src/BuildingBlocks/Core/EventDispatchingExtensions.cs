using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BuildingBlocks.Core;

public static class EventDispatchingExtensions
{
    public static IServiceCollection AddEventDispatcher(this IServiceCollection services)
    {
        services.TryAddScoped<IEventDispatcher, EventDispatcher>();
        services.TryAddSingleton<IEventHeadersProvider, EmptyEventHeadersProvider>();
        return services;
    }

    public static IServiceCollection AddEventMapper<TMapper>(this IServiceCollection services)
        where TMapper : class, IEventMapper
    {
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IEventMapper, TMapper>());
        return services;
    }

    private sealed class EmptyEventHeadersProvider : IEventHeadersProvider
    {
        public IDictionary<string, object?> GetHeaders() => new Dictionary<string, object?>();
    }
}
