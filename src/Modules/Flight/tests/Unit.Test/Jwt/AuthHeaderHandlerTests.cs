namespace Unit.Test.Jwt;

using System.Net;
using System.Net.Http.Headers;
using BuildingBlocks.Jwt;
using Microsoft.AspNetCore.Http;
using Xunit;

public class AuthHeaderHandlerTests
{
    [Theory]
    [InlineData("Bearer abc")]
    [InlineData("bearer abc")]
    public async Task should_forward_bearer_authorization(string authorization)
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers.Authorization = authorization;

        var innerHandler = new CapturingHttpMessageHandler();
        using var invoker = new HttpMessageInvoker(
            new AuthHeaderHandler(new HttpContextAccessor { HttpContext = httpContext }) { InnerHandler = innerHandler }
        );
        using var request = new HttpRequestMessage(HttpMethod.Get, "http://localhost");

        using var response = await invoker.SendAsync(request, CancellationToken.None);

        Assert.Equal(new AuthenticationHeaderValue("Bearer", "abc"), innerHandler.Authorization);
    }

    [Fact]
    public async Task should_not_set_authorization_when_header_is_missing()
    {
        var httpContext = new DefaultHttpContext();

        var innerHandler = new CapturingHttpMessageHandler();
        using var invoker = new HttpMessageInvoker(
            new AuthHeaderHandler(new HttpContextAccessor { HttpContext = httpContext }) { InnerHandler = innerHandler }
        );
        using var request = new HttpRequestMessage(HttpMethod.Get, "http://localhost");

        using var response = await invoker.SendAsync(request, CancellationToken.None);

        Assert.Null(innerHandler.Authorization);
    }

    private sealed class CapturingHttpMessageHandler : HttpMessageHandler
    {
        public AuthenticationHeaderValue Authorization { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            Authorization = request.Headers.Authorization;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }
    }
}
