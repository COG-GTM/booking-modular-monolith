using System.Security.Claims;
using BuildingBlocks.Core;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BuildingBlocks.Web;

// Adds CorrelationId / UserId / UserName from the current HTTP request to dispatched integration events.
public sealed class HttpContextEventHeadersProvider(IHttpContextAccessor httpContextAccessor) : IEventHeadersProvider
{
    public IReadOnlyDictionary<string, object?> GetHeaders()
    {
        var httpContext = httpContextAccessor.HttpContext;

        return new Dictionary<string, object?>
        {
            ["CorrelationId"] = httpContext?.GetCorrelationId(),
            ["UserId"] = httpContext?.User?.FindFirstValue(ClaimTypes.NameIdentifier),
            ["UserName"] = httpContext?.User?.FindFirstValue(ClaimTypes.Name),
        };
    }
}

public static class EventHeadersExtensions
{
    public static IServiceCollection AddHttpContextEventHeaders(this IServiceCollection services)
    {
        services.AddHttpContextAccessor();
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IEventHeadersProvider, HttpContextEventHeadersProvider>());
        return services;
    }
}
