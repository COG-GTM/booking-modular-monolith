using BuildingBlocks.Core.Event;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BuildingBlocks.Core;

public static class Extensions
{
    public static IServiceCollection AddEventDispatcher(this IServiceCollection services)
    {
        services.TryAddScoped<IEventDispatcher, EventDispatcher>();
        services.TryAddScoped<IEventHeadersProvider, DefaultEventHeadersProvider>();

        return services;
    }

    // Each module/service registers only its own mapper; EventDispatcher composes every registered IEventMapper.
    public static IServiceCollection AddEventMapper<TMapper>(this IServiceCollection services)
        where TMapper : class, IEventMapper
    {
        services.TryAddScoped<TMapper>();
        services.TryAddEnumerable(
            ServiceDescriptor.Scoped<IEventMapper, TMapper>(sp => sp.GetRequiredService<TMapper>())
        );

        return services;
    }
}
