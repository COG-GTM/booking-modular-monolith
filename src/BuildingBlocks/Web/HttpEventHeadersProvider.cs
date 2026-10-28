using System.Security.Claims;
using BuildingBlocks.Core;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BuildingBlocks.Web;

public sealed class HttpEventHeadersProvider(IHttpContextAccessor accessor) : IEventHeadersProvider
{
    public IDictionary<string, object?> GetHeaders() =>
        new Dictionary<string, object?>
        {
            ["CorrelationId"] = accessor.HttpContext?.GetCorrelationId(),
            ["UserId"] = accessor.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier),
            ["UserName"] = accessor.HttpContext?.User.FindFirstValue(ClaimTypes.Name),
        };
}

public static class WebEventDispatchingExtensions
{
    public static IServiceCollection AddWebEventDispatcher(this IServiceCollection services)
    {
        services.AddEventDispatcher();
        services.AddHttpContextAccessor();
        services.Replace(ServiceDescriptor.Scoped<IEventHeadersProvider, HttpEventHeadersProvider>());
        return services;
    }
}
