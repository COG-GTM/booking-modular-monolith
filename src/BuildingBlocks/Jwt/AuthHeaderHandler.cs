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
        var token = (_httpContext?.HttpContext?.Request.Headers["Authorization"])?.ToString();

        if (!string.IsNullOrWhiteSpace(token) && IsTrustedDestination(request.RequestUri))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue(
                "Bearer",
                token.Replace("Bearer ", "", StringComparison.OrdinalIgnoreCase)
            );
        }

        return base.SendAsync(request, cancellationToken);
    }

    // Only forward the caller's bearer token over TLS or to the local process so a
    // misconfigured plaintext/external gRPC address cannot receive a reusable credential.
    private static bool IsTrustedDestination(Uri? uri)
    {
        return uri is not null && (uri.Scheme == Uri.UriSchemeHttps || uri.IsLoopback);
    }
}
