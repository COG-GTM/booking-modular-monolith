using System.Net.Http.Headers;
using Microsoft.AspNetCore.Http;

namespace BuildingBlocks.Jwt;

public class AuthHeaderHandler : DelegatingHandler
{
    private const string BearerScheme = "Bearer";
    private readonly IHttpContextAccessor _httpContext;

    public AuthHeaderHandler(IHttpContextAccessor httpContext)
    {
        _httpContext = httpContext;
    }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken
    )
    {
        var header = _httpContext?.HttpContext?.Request.Headers.Authorization.ToString();

        if (
            AuthenticationHeaderValue.TryParse(header, out var parsed)
            && string.Equals(parsed.Scheme, BearerScheme, StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(parsed.Parameter)
        )
        {
            request.Headers.Authorization = new AuthenticationHeaderValue(BearerScheme, parsed.Parameter);
        }

        return base.SendAsync(request, cancellationToken);
    }
}
