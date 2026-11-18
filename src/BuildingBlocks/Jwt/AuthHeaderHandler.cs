using System.Net.Http.Headers;
using Microsoft.AspNetCore.Http;

namespace BuildingBlocks.Jwt;

public class AuthHeaderHandler : DelegatingHandler
{
    private readonly IHttpContextAccessor _httpContext;

    public AuthHeaderHandler(IHttpContextAccessor httpContext)
    {
        _httpContext = httpContext;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var header = _httpContext?.HttpContext?.Request.Headers["Authorization"].ToString();

        if (
            AuthenticationHeaderValue.TryParse(header, out var value)
            && string.Equals(value.Scheme, "Bearer", StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(value.Parameter)
        )
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", value.Parameter);
        }

        return base.SendAsync(request, cancellationToken);
    }
}