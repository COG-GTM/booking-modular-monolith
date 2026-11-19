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

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken
    )
    {
        var authorization = _httpContext.HttpContext?.Request.Headers.Authorization.ToString();
        var separator = authorization?.IndexOf(' ', StringComparison.Ordinal) ?? -1;

        if (
            separator > 0
            && AuthenticationHeaderValue.TryParse(authorization![..separator], out var header)
            && !string.IsNullOrWhiteSpace(authorization[(separator + 1)..])
        )
        {
            var parameter = authorization[(separator + 1)..].Trim();
            request.Headers.Authorization = string.Equals(header.Scheme, "Bearer", StringComparison.OrdinalIgnoreCase)
                ? new AuthenticationHeaderValue("Bearer", parameter)
                : new AuthenticationHeaderValue(header.Scheme, parameter);
        }

        return base.SendAsync(request, cancellationToken);
    }
}
