using BuildingBlocks.Core.Event;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BuildingBlocks.Core;

public static class Extensions
{
    public static IServiceCollection AddEventHeadersProvider(this IServiceCollection services)
    {
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
