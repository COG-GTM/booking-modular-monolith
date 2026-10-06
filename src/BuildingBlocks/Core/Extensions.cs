using BuildingBlocks.Core.Event;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BuildingBlocks.Core;

public static class Extensions
{
    public static IServiceCollection AddEventDispatcher<TModule>(this IServiceCollection services)
        where TModule : class
    {
        services.TryAddScoped<IEventDispatcher<TModule>, EventDispatcher<TModule>>();
        services.TryAddScoped<IEventHeadersProvider, DefaultEventHeadersProvider>();
        return services;
    }

    public static IServiceCollection AddEventMapper<TMapper>(this IServiceCollection services)
        where TMapper : class, IEventMapper
    {
        services.AddScoped<IEventMapper, TMapper>();
        return services;
    }
}
