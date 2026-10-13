using System.Security.Claims;
using BuildingBlocks.Core.Event;
using Microsoft.AspNetCore.Http;

namespace BuildingBlocks.Web;

public sealed class HttpContextEventHeadersProvider(IHttpContextAccessor httpContextAccessor) : IEventHeadersProvider
{
    public IDictionary<string, object?> GetHeaders()
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
