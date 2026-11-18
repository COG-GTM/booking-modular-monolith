using Microsoft.AspNetCore.Http;
using Microsoft.Net.Http.Headers;

namespace Booking.Extensions.Infrastructure;

public sealed class ForwardAuthorizationHeaderHandler(IHttpContextAccessor httpContextAccessor) : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken
    )
    {
        var authorizationHeader = httpContextAccessor.HttpContext?.Request.Headers[HeaderNames.Authorization];

        if (!string.IsNullOrEmpty(authorizationHeader) && !request.Headers.Contains(HeaderNames.Authorization))
        {
            request.Headers.TryAddWithoutValidation(HeaderNames.Authorization, authorizationHeader.ToString());
        }

        return base.SendAsync(request, cancellationToken);
    }
}
