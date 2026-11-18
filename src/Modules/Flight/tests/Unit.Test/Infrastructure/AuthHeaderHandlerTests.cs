using System.Net;
using BuildingBlocks.Jwt;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace Unit.Test.Infrastructure;

public class AuthHeaderHandlerTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("   ")]
    public async Task send_should_not_add_an_empty_bearer_header(string? authorization)
    {
        var context = new DefaultHttpContext();
        if (authorization is not null)
        {
            context.Request.Headers.Authorization = authorization;
        }

        using var handler = new AuthHeaderHandler(new HttpContextAccessor { HttpContext = context })
        {
            InnerHandler = new StubTransport(),
        };
        using var client = new HttpMessageInvoker(handler, disposeHandler: false);
        using var request = new HttpRequestMessage(HttpMethod.Get, "http://flight.test");

        using var response = await client.SendAsync(request, CancellationToken.None);

        request.Headers.Authorization.Should().BeNull();
    }

    private sealed class StubTransport : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        ) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
    }
}
