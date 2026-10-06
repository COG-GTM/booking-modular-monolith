using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BuildingBlocks.Core;

public static class Extensions
{
    public static IServiceCollection AddEventMapper<TMapper>(this IServiceCollection services)
        where TMapper : class, IEventMapper
    {
        services.TryAddScoped<TMapper>();
        services.AddScoped<IEventMapper, TMapper>();
        return services;
    }
}
