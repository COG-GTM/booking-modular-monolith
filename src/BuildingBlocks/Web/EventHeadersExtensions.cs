using BuildingBlocks.Core.Event;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BuildingBlocks.Web;

public static class EventHeadersExtensions
{
    public static IServiceCollection AddHttpContextEventHeaders(this IServiceCollection services)
    {
        services.AddHttpContextAccessor();
        services.Replace(ServiceDescriptor.Scoped<IEventHeadersProvider, HttpContextEventHeadersProvider>());

        return services;
    }
}
