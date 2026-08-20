using System.Security.Claims;
using BuildingBlocks.Core.Event;
using Microsoft.AspNetCore.Http;

namespace BuildingBlocks.Web;

public class HttpContextEventHeadersProvider(IHttpContextAccessor httpContextAccessor) : IEventHeadersProvider
{
    public IDictionary<string, object?> GetHeaders()
    {
        var headers = new Dictionary<string, object?>();
        headers.Add("CorrelationId", httpContextAccessor?.HttpContext?.GetCorrelationId());
        headers.Add("UserId", httpContextAccessor?.HttpContext?.User?.FindFirstValue(ClaimTypes.NameIdentifier));
        headers.Add("UserName", httpContextAccessor?.HttpContext?.User?.FindFirstValue(ClaimTypes.Name));

        return headers;
    }
}
