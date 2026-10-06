using Microsoft.Extensions.DependencyInjection;

namespace BuildingBlocks.Core;

public static class Extensions
{
    public static IServiceCollection AddEventMapper<TMapper>(this IServiceCollection services)
        where TMapper : class, IEventMapper
    {
        services.AddScoped<IEventMapper, TMapper>();
        return services;
    }
}
