using Microsoft.Extensions.DependencyInjection;

namespace BuildingBlocks.Core;

public static class EventMapperExtensions
{
    public static IServiceCollection AddModuleEventMapper<TMapper>(this IServiceCollection services)
        where TMapper : class, IEventMapper
    {
        services.AddScoped<TMapper>();
        services.AddScoped<IEventMapperRegistration, EventMapperRegistration<TMapper>>();

        return services;
    }

    public static IServiceCollection AddCompositeEventMapper(this IServiceCollection services)
    {
        services.AddScoped<IEventMapper>(sp =>
        {
            var mappers = sp.GetServices<IEventMapperRegistration>()
                .Select(registration => registration.Mapper)
                .ToArray();

            return new CompositeEventMapper(mappers);
        });

        return services;
    }
}
