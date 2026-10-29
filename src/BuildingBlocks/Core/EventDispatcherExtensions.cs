using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BuildingBlocks.Core;

public static class EventDispatcherExtensions
{
    // Registers IEventDispatcher. The dispatcher composes every IEventMapper registered with AddEventMapper,
    // so each module/service registers only its own mappers.
    public static IServiceCollection AddEventDispatcher(this IServiceCollection services)
    {
        services.TryAddScoped(typeof(IEventDispatcher<>), typeof(EventDispatcher<>));
        return services;
    }

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
